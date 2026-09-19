using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Additive war-memory layer. Bannerlord/TOR remain owners of war and peace.
/// KaiTOR samples native StanceLink statistics and stores only exhaustion/memory needed
/// for long-term diplomacy and negotiated peace terms.
/// </summary>
public sealed class KaiWarExhaustionBehavior : CampaignBehaviorBase
{
    private const string ExhaustionSaveKey = "kaitor_war_exhaustion_v1";
    private const string PrevCasualtiesSaveKey = "kaitor_war_prev_casualties_v1";
    private const string PrevRaidsSaveKey = "kaitor_war_prev_raids_v1";
    private const string PrevSiegesSaveKey = "kaitor_war_prev_sieges_v1";
    private const string PrevTownsSaveKey = "kaitor_war_prev_towns_v1";

    private Dictionary<string, float> _eventExhaustion = new();
    private Dictionary<string, int> _prevCasualties = new();
    private Dictionary<string, int> _prevRaids = new();
    private Dictionary<string, int> _prevSieges = new();
    private Dictionary<string, int> _prevTowns = new();

    public override void RegisterEvents()
    {
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
        CampaignEvents.MakePeace.AddNonSerializedListener(this, OnPeaceMade);
        CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnHeroPrisonerTaken);
        CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        CampaignEvents.ArmyDispersed.AddNonSerializedListener(this, OnArmyDispersed);
        CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(ExhaustionSaveKey, ref _eventExhaustion);
        dataStore.SyncData(PrevCasualtiesSaveKey, ref _prevCasualties);
        dataStore.SyncData(PrevRaidsSaveKey, ref _prevRaids);
        dataStore.SyncData(PrevSiegesSaveKey, ref _prevSieges);
        dataStore.SyncData(PrevTownsSaveKey, ref _prevTowns);

        _eventExhaustion ??= new Dictionary<string, float>();
        _prevCasualties ??= new Dictionary<string, int>();
        _prevRaids ??= new Dictionary<string, int>();
        _prevSieges ??= new Dictionary<string, int>();
        _prevTowns ??= new Dictionary<string, int>();
    }

    public float GetExhaustion(Kingdom side, Kingdom enemy)
    {
        if (side == null || enemy == null || side == enemy)
            return 0f;

        var key = SideKey(side, enemy);
        var value = _eventExhaustion.TryGetValue(key, out var stored) ? stored : 0f;

        var stance = side.GetStanceWith(enemy);
        if (stance?.IsAtWar == true)
        {
            // Duration pressure is derived, not accumulated, so missed ticks/load order
            // cannot distort it. It saturates at 25 points.
            var days = Math.Max(0f, stance.WarStartDate.ElapsedDaysUntilNow);
            value += Math.Min(25f, days * 0.20f);
        }

        return Clamp(value);
    }

    public float GetPeacePressureModifier(Kingdom side, Kingdom enemy)
    {
        // 0..125 score points at maximum exhaustion. This remains a modifier on top of
        // TOR's own strength/religion/lore/alliance-war scoring rather than replacing it.
        return GetExhaustion(side, enemy) * 1.25f;
    }

    public IEnumerable<string> DescribeWars()
    {
        var kingdoms = Kingdom.All
            .Where(k => k != null && !k.IsEliminated)
            .OrderBy(k => k.StringId, StringComparer.Ordinal)
            .ToArray();

        var any = false;
        for (var i = 0; i < kingdoms.Length; i++)
        {
            for (var j = i + 1; j < kingdoms.Length; j++)
            {
                var first = kingdoms[i];
                var second = kingdoms[j];
                var stance = first.GetStanceWith(second);
                if (stance?.IsAtWar != true)
                    continue;

                any = true;
                yield return
                    $"{first.StringId}<->{second.StringId}: " +
                    $"{first.StringId}={GetExhaustion(first, second):0.0}; " +
                    $"{second.StringId}={GetExhaustion(second, first):0.0}; " +
                    $"days={stance.WarStartDate.ElapsedDaysUntilNow:0.0}; " +
                    $"casualties={stance.GetCasualties(first)}/{stance.GetCasualties(second)}; " +
                    $"raids={stance.GetSuccessfulRaids(first)}/{stance.GetSuccessfulRaids(second)}; " +
                    $"sieges={stance.GetSuccessfulSieges(first)}/{stance.GetSuccessfulSieges(second)}";
            }
        }

        if (!any)
            yield return "War exhaustion: no active kingdom wars.";
    }

    private void OnDailyTick()
    {
        var kingdoms = Kingdom.All
            .Where(k => k != null && !k.IsEliminated)
            .OrderBy(k => k.StringId, StringComparer.Ordinal)
            .ToArray();

        for (var i = 0; i < kingdoms.Length; i++)
        {
            for (var j = i + 1; j < kingdoms.Length; j++)
            {
                var first = kingdoms[i];
                var second = kingdoms[j];
                var stance = first.GetStanceWith(second);
                if (stance?.IsAtWar != true)
                    continue;

                SampleSide(first, second, stance);
                SampleSide(second, first, stance);

                // Having no fief while still existing as a kingdom produces steady pressure.
                if (!first.Fiefs.Any())
                    Add(first, second, 0.75f, "landless");
                if (!second.Fiefs.Any())
                    Add(second, first, 0.75f, "landless");
            }
        }
    }

    private void SampleSide(Kingdom side, Kingdom enemy, StanceLink stance)
    {
        var key = SideKey(side, enemy);

        var casualties = Math.Max(0, stance.GetCasualties(side));
        var oldCasualties = Get(_prevCasualties, key);
        var casualtyDelta = Math.Max(0, casualties - oldCasualties);
        if (casualtyDelta > 0)
        {
            // About +1 exhaustion per 100 new casualties at the beginning; accumulated
            // losses gradually give diminishing marginal pressure.
            var diminishing = 1f / (float)Math.Sqrt(1f + oldCasualties / 750f);
            Add(side, enemy, Math.Min(8f, casualtyDelta * 0.01f * diminishing),
                "casualties:" + casualtyDelta);
        }
        _prevCasualties[key] = casualties;

        // Successful raids/sieges are recorded for the attacker in StanceLink, therefore
        // enemy successes increase this side's exhaustion.
        var enemyRaids = Math.Max(0, stance.GetSuccessfulRaids(enemy));
        var oldEnemyRaids = Get(_prevRaids, key);
        for (var n = oldEnemyRaids; n < enemyRaids; n++)
            Add(side, enemy, 2.5f / (float)Math.Sqrt(1f + n * 0.5f), "raid_lost");
        _prevRaids[key] = enemyRaids;

        var enemySieges = Math.Max(0, stance.GetSuccessfulSieges(enemy));
        var enemyTowns = Math.Max(0, stance.GetSuccessfulTownSieges(enemy));
        var oldEnemySieges = Get(_prevSieges, key);
        var oldEnemyTowns = Get(_prevTowns, key);

        var townDelta = Math.Max(0, enemyTowns - oldEnemyTowns);
        var siegeDelta = Math.Max(0, enemySieges - oldEnemySieges);
        var castleDelta = Math.Max(0, siegeDelta - townDelta);

        for (var n = oldEnemyTowns; n < oldEnemyTowns + townDelta; n++)
            Add(side, enemy, 12f / (float)Math.Sqrt(1f + n * 0.5f), "town_lost");

        var priorCastles = Math.Max(0, oldEnemySieges - oldEnemyTowns);
        for (var n = priorCastles; n < priorCastles + castleDelta; n++)
            Add(side, enemy, 6f / (float)Math.Sqrt(1f + n * 0.5f), "castle_lost");

        _prevSieges[key] = enemySieges;
        _prevTowns[key] = enemyTowns;
    }

    private void OnWarDeclared(
        IFaction firstFaction,
        IFaction secondFaction,
        DeclareWarAction.DeclareWarDetail detail)
    {
        if (firstFaction is not Kingdom first || secondFaction is not Kingdom second || first == second)
            return;

        ResetWar(first, second);
        KaiRuntimeLog.Write(
            "WAR_EXHAUSTION_CHANGE",
            $"source={first.StringId}; target={second.StringId}; event=war_started; detail={detail}; first=0; second=0");
    }

    private void OnPeaceMade(
        IFaction firstFaction,
        IFaction secondFaction,
        MakePeaceAction.MakePeaceDetail detail)
    {
        if (firstFaction is not Kingdom first || secondFaction is not Kingdom second || first == second)
            return;

        var firstValue = GetExhaustion(first, second);
        var secondValue = GetExhaustion(second, first);
        KaiRuntimeLog.Write(
            "WAR_EXHAUSTION_CHANGE",
            $"source={first.StringId}; target={second.StringId}; event=peace; detail={detail}; first={firstValue:0.0}; second={secondValue:0.0}");

        ResetWar(first, second);
    }

    private void OnHeroPrisonerTaken(PartyBase capturer, Hero prisoner)
    {
        var victim = prisoner?.MapFaction as Kingdom;
        var attacker = capturer?.MapFaction as Kingdom;
        if (victim == null || attacker == null || victim == attacker || !victim.IsAtWarWith(attacker))
            return;
        if (prisoner.IsLord)
            Add(victim, attacker, 3f, "noble_captured:" + prisoner.StringId);
    }

    private void OnHeroKilled(
        Hero victim,
        Hero killer,
        KillCharacterAction.KillCharacterActionDetail detail,
        bool showNotification)
    {
        if (victim?.IsLord != true)
            return;

        var victimKingdom = victim.MapFaction as Kingdom;
        var killerKingdom = killer?.MapFaction as Kingdom;
        if (victimKingdom == null || killerKingdom == null ||
            victimKingdom == killerKingdom || !victimKingdom.IsAtWarWith(killerKingdom))
            return;

        Add(victimKingdom, killerKingdom, 5f, "noble_killed:" + victim.StringId);
    }

    private void OnArmyDispersed(Army army, Army.ArmyDispersionReason reason, bool isPlayersArmy)
    {
        if (army?.Kingdom == null)
            return;

        if (reason != Army.ArmyDispersionReason.NotEnoughTroop &&
            reason != Army.ArmyDispersionReason.LeaderPartyRemoved &&
            reason != Army.ArmyDispersionReason.PlayerTakenPrisoner &&
            reason != Army.ArmyDispersionReason.ArmyLeaderIsDead &&
            reason != Army.ArmyDispersionReason.CannotElectNewLeader)
            return;

        var enemies = Kingdom.All
            .Where(k => k != null && !k.IsEliminated && k != army.Kingdom && army.Kingdom.IsAtWarWith(k))
            .ToArray();

        // Attribute an army collapse only when the responsible war is unambiguous.
        if (enemies.Length == 1)
            Add(army.Kingdom, enemies[0], 4f, "army_destroyed:" + reason);
    }

    private void OnMobilePartyDestroyed(MobileParty destroyedParty, PartyBase destroyerParty)
    {
        if (destroyedParty?.IsCaravan != true)
            return;

        var victim = destroyedParty.MapFaction as Kingdom;
        var attacker = destroyerParty?.MapFaction as Kingdom;
        if (victim == null || attacker == null || victim == attacker || !victim.IsAtWarWith(attacker))
            return;

        Add(victim, attacker, 1.5f, "caravan_destroyed");
    }

    private void Add(Kingdom side, Kingdom enemy, float amount, string reason)
    {
        if (side == null || enemy == null || amount <= 0f || !side.IsAtWarWith(enemy))
            return;

        var key = SideKey(side, enemy);
        var before = _eventExhaustion.TryGetValue(key, out var current) ? current : 0f;
        var after = Clamp(before + amount);
        if (after <= before + 0.001f)
            return;

        _eventExhaustion[key] = after;
        KaiRuntimeLog.Write(
            "WAR_EXHAUSTION_CHANGE",
            $"side={side.StringId}; enemy={enemy.StringId}; reason={reason}; delta={after - before:0.00}; eventTotal={after:0.00}; effective={GetExhaustion(side, enemy):0.00}");
    }

    private void ResetWar(Kingdom first, Kingdom second)
    {
        foreach (var key in new[] { SideKey(first, second), SideKey(second, first) })
        {
            _eventExhaustion.Remove(key);
            _prevCasualties.Remove(key);
            _prevRaids.Remove(key);
            _prevSieges.Remove(key);
            _prevTowns.Remove(key);
        }
    }

    private static int Get(Dictionary<string, int> values, string key)
        => values.TryGetValue(key, out var value) ? Math.Max(0, value) : 0;

    private static string SideKey(Kingdom side, Kingdom enemy)
        => side.StringId + "->" + enemy.StringId;

    private static float Clamp(float value)
        => Math.Max(0f, Math.Min(100f, value));
}
