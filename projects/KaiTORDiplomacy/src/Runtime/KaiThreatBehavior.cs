using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Expansion/threat memory. TOR remains the owner of alliance/war/peace/trade decisions;
/// this behavior only produces a persistent reputation score and additive modifiers.
/// </summary>
public sealed class KaiThreatBehavior : CampaignBehaviorBase
{
    private const string BaseThreatSaveKey = "kaitor_threat_v1_base";
    private const string WinStreakSaveKey = "kaitor_threat_v1_win_streak";
    private const string LastConquerorSaveKey = "kaitor_threat_v1_last_conqueror";
    private const string LastExpansionSaveKey = "kaitor_threat_v1_last_expansion";

    private const float DailyDecayPeace = 0.18f;
    private const float DailyDecayWar = 0.08f;
    private const float TownConquestThreat = 14f;
    private const float CastleConquestThreat = 8f;
    private const float KingdomDestructionThreat = 15f;
    private const float TreatyBreachThreat = 10f;

    private Dictionary<string, float> _baseThreat = new();
    private Dictionary<string, int> _winStreak = new();
    private Dictionary<string, string> _lastConqueror = new();
    private Dictionary<string, double> _lastExpansion = new();

    public override void RegisterEvents()
    {
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
        CampaignEvents.KingdomDestroyedEvent.AddNonSerializedListener(this, OnKingdomDestroyed);
        CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(BaseThreatSaveKey, ref _baseThreat);
        dataStore.SyncData(WinStreakSaveKey, ref _winStreak);
        dataStore.SyncData(LastConquerorSaveKey, ref _lastConqueror);
        dataStore.SyncData(LastExpansionSaveKey, ref _lastExpansion);

        _baseThreat ??= new Dictionary<string, float>();
        _winStreak ??= new Dictionary<string, int>();
        _lastConqueror ??= new Dictionary<string, string>();
        _lastExpansion ??= new Dictionary<string, double>();
    }

    public float GetThreat(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.IsEliminated)
            return 0f;

        var stored = _baseThreat.TryGetValue(kingdom.StringId, out var value)
            ? Math.Max(0f, value)
            : 0f;

        return Clamp(stored + GetStrengthPressure(kingdom));
    }

    public IEnumerable<string> DescribeStatus()
    {
        var kingdoms = Kingdom.All
            .Where(k => k != null && !k.IsEliminated)
            .OrderByDescending(GetThreat)
            .ThenBy(k => k.StringId, StringComparer.Ordinal)
            .ToArray();

        if (kingdoms.Length == 0)
        {
            yield return "Threat: no active kingdoms.";
            yield break;
        }

        foreach (var kingdom in kingdoms.Take(20))
        {
            _baseThreat.TryGetValue(kingdom.StringId, out var stored);
            _winStreak.TryGetValue(kingdom.StringId, out var streak);
            yield return
                $"{kingdom.StringId}: threat={GetThreat(kingdom):0.0}; " +
                $"persistent={Math.Max(0f, stored):0.0}; strengthPressure={GetStrengthPressure(kingdom):0.0}; " +
                $"winStreak={Math.Max(0, streak)}";
        }
    }

    public void RecordTreatyBreach(Kingdom breaker, Kingdom victim, string reason)
    {
        if (breaker == null || victim == null || breaker == victim)
            return;

        AddThreat(
            breaker,
            TreatyBreachThreat,
            "treaty_breach:" + (string.IsNullOrWhiteSpace(reason) ? "unknown" : reason));
    }

    public float GetNapPenalty(Kingdom proposer)
        => proposer == null ? 0f : GetThreat(proposer) * 0.45f;

    private void OnDailyTick()
    {
        foreach (var kingdom in Kingdom.All.Where(k => k != null && !k.IsEliminated))
        {
            if (!_baseThreat.TryGetValue(kingdom.StringId, out var value) || value <= 0f)
                continue;

            var atWar = kingdom.FactionsAtWarWith.Any(f => f is Kingdom);
            var decay = atWar ? DailyDecayWar : DailyDecayPeace;
            var after = Math.Max(0f, value - decay);

            if (after <= 0.01f)
                _baseThreat.Remove(kingdom.StringId);
            else
                _baseThreat[kingdom.StringId] = after;
        }
    }

    private void OnSettlementOwnerChanged(
        Settlement settlement,
        bool openToClaim,
        Hero newOwner,
        Hero oldOwner,
        Hero capturerHero,
        ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
    {
        if (settlement == null || !settlement.IsFortification)
            return;

        var newKingdom = newOwner?.Clan?.Kingdom;
        var oldKingdom = oldOwner?.Clan?.Kingdom;

        if (detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege &&
            newKingdom != null &&
            oldKingdom != null &&
            newKingdom != oldKingdom)
        {
            AddThreat(
                newKingdom,
                settlement.IsTown ? TownConquestThreat : CastleConquestThreat,
                settlement.IsTown ? "town_conquest" : "castle_conquest");

            _lastConqueror[oldKingdom.StringId] = newKingdom.StringId;
            _lastExpansion[newKingdom.StringId] = CampaignTime.Now.ToDays;
            return;
        }

        // Voluntary release/transfer of a holding slightly reduces expansionist reputation.
        if ((detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByGift ||
             detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByBarter) &&
            oldKingdom != null &&
            newKingdom != null &&
            oldKingdom != newKingdom)
        {
            ReduceThreat(
                oldKingdom,
                settlement.IsTown ? 7f : 4f,
                settlement.IsTown ? "town_released" : "castle_released");
        }
    }

    private void OnKingdomDestroyed(Kingdom destroyed)
    {
        if (destroyed == null ||
            !_lastConqueror.TryGetValue(destroyed.StringId, out var conquerorId))
            return;

        var conqueror = Kingdom.All.FirstOrDefault(k =>
            k != null &&
            !k.IsEliminated &&
            string.Equals(k.StringId, conquerorId, StringComparison.Ordinal));

        if (conqueror != null)
            AddThreat(conqueror, KingdomDestructionThreat, "kingdom_destroyed:" + destroyed.StringId);

        _lastConqueror.Remove(destroyed.StringId);
    }

    private void OnMapEventEnded(MapEvent mapEvent)
    {
        if (mapEvent == null || !mapEvent.HasWinner || mapEvent.WinningSide == BattleSideEnum.None)
            return;

        var winnerParty = mapEvent.GetLeaderParty(mapEvent.WinningSide);
        var loserSide = mapEvent.GetOtherSide(mapEvent.WinningSide);
        var loserParty = mapEvent.GetLeaderParty(loserSide);

        var winner = winnerParty?.MapFaction as Kingdom;
        var loser = loserParty?.MapFaction as Kingdom;
        if (winner == null || loser == null || winner == loser || !winner.IsAtWarWith(loser))
            return;

        _winStreak.TryGetValue(winner.StringId, out var streak);
        streak = Math.Min(20, Math.Max(0, streak) + 1);
        _winStreak[winner.StringId] = streak;
        _winStreak[loser.StringId] = 0;

        if (streak >= 3)
            AddThreat(
                winner,
                Math.Min(1.5f, 0.25f * (streak - 2)),
                "victory_streak:" + streak);
    }

    private float GetStrengthPressure(Kingdom kingdom)
    {
        var active = Kingdom.All
            .Where(k => k != null && !k.IsEliminated && k.CurrentTotalStrength > 0f)
            .ToArray();

        if (active.Length <= 1 || kingdom.CurrentTotalStrength <= 0f)
            return 0f;

        var average = active.Average(k => Math.Max(1f, k.CurrentTotalStrength));
        if (average <= 0.01f)
            return 0f;

        var ratio = kingdom.CurrentTotalStrength / average;
        return ratio <= 1f
            ? 0f
            : Math.Min(25f, (ratio - 1f) * 15f);
    }

    private void AddThreat(Kingdom kingdom, float amount, string reason)
    {
        if (kingdom == null || kingdom.IsEliminated || amount <= 0f)
            return;

        var before = _baseThreat.TryGetValue(kingdom.StringId, out var current)
            ? Math.Max(0f, current)
            : 0f;
        var after = Clamp(before + amount);
        _baseThreat[kingdom.StringId] = after;

        KaiRuntimeLog.Write(
            "THREAT_CHANGE",
            $"kingdom={kingdom.StringId}; reason={reason}; delta={after - before:0.00}; " +
            $"persistent={after:0.00}; effective={GetThreat(kingdom):0.00}");
    }

    private void ReduceThreat(Kingdom kingdom, float amount, string reason)
    {
        if (kingdom == null || amount <= 0f)
            return;

        var before = _baseThreat.TryGetValue(kingdom.StringId, out var current)
            ? Math.Max(0f, current)
            : 0f;
        var after = Math.Max(0f, before - amount);

        if (after <= 0.01f)
            _baseThreat.Remove(kingdom.StringId);
        else
            _baseThreat[kingdom.StringId] = after;

        if (before > after + 0.001f)
            KaiRuntimeLog.Write(
                "THREAT_CHANGE",
                $"kingdom={kingdom.StringId}; reason={reason}; delta={after - before:0.00}; " +
                $"persistent={after:0.00}; effective={GetThreat(kingdom):0.00}");
    }

    private static float Clamp(float value)
        => Math.Max(0f, Math.Min(100f, value));
}
