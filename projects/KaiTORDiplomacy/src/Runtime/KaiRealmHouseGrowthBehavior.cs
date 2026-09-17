using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Safe AI realm-growth path for existing TOR saves.
///
/// v0.6.3.3:
/// - every kingdom owns an independent pending slot/cooldown; one failed realm can no
///   longer block Dawi or every other kingdom for a week;
/// - only one graph mutation is committed per safe campaign day, selecting the realm
///   with the largest current clan deficit first;
/// - TOR AI companions/wanderer-style founders are promoted through Hero.SetNewOccupation
///   after Bannerlord's native CreateCompanionToLordClan transaction, matching the
///   occupation transition required by Bannerlord's lord semantics;
/// - incomplete companion-clans left by v0.6.3.1/2 are repaired on the next hourly tick
///   when their only missing postcondition is leader Occupation.Lord.
///
/// Graph ownership remains engine-side: no direct founder.Clan/newClan.Kingdom writes.
/// </summary>
public sealed class KaiRealmHouseGrowthBehavior : CampaignBehaviorBase
{
    private const int MaximumTargetNobleClans = 12;
    private const int MinimumClanDeficitForNewHouse = 1;
    private const int PerKingdomCooldownDays = 42;
    private const int FailureCooldownDays = 7;
    private const double PendingDelayDays = 0.25d;
    private const int NewHouseMinimumRulerGold = 30000;
    private const int NewHouseSeedGold = 10000;
    private const int CommitHour = 4;
    private const string TorSpecialSettlementId = "castle_BK1";

    // Keep the v2 cooldown key so existing saves retain successful realm cooldowns.
    private const string KingdomCooldownSaveKey = "kaitor_realm_house_kingdom_cooldown_v2";
    private const string PendingFounderByKingdomSaveKey = "kaitor_realm_house_pending_founders_v3";
    private const string PendingHomeByKingdomSaveKey = "kaitor_realm_house_pending_homes_v3";
    private const string PendingReadyByKingdomSaveKey = "kaitor_realm_house_pending_ready_v3";
    private const string LastCreatedClanSaveKey = "kaitor_realm_house_last_created_v2";

    private Dictionary<string, double> _kingdomCooldownUntilDays = new();
    private Dictionary<string, string> _pendingFounderByKingdom = new();
    private Dictionary<string, string> _pendingHomeByKingdom = new();
    private Dictionary<string, double> _pendingReadyByKingdom = new();
    private string _lastCreatedClanId;
    private bool _commitInProgress;
    private bool _legacyRepairDone;

    public override void RegisterEvents()
    {
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
        CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(KingdomCooldownSaveKey, ref _kingdomCooldownUntilDays);
        dataStore.SyncData(PendingFounderByKingdomSaveKey, ref _pendingFounderByKingdom);
        dataStore.SyncData(PendingHomeByKingdomSaveKey, ref _pendingHomeByKingdom);
        dataStore.SyncData(PendingReadyByKingdomSaveKey, ref _pendingReadyByKingdom);
        dataStore.SyncData(LastCreatedClanSaveKey, ref _lastCreatedClanId);

        _kingdomCooldownUntilDays ??= new Dictionary<string, double>();
        _pendingFounderByKingdom ??= new Dictionary<string, string>();
        _pendingHomeByKingdom ??= new Dictionary<string, string>();
        _pendingReadyByKingdom ??= new Dictionary<string, double>();
    }

    private void OnWeeklyTick()
    {
        if (Campaign.Current == null || _commitInProgress)
            return;

        var now = CampaignTime.Now.ToDays;
        foreach (var kingdom in Kingdom.All
                     .Where(k => k != null && !k.IsEliminated && k.Leader != null && k.Leader != Hero.MainHero)
                     .OrderByDescending(k => GetTargetNobleClanCount(k) - GetCurrentNobleClanCount(k))
                     .ThenBy(k => k.StringId, StringComparer.Ordinal))
        {
            var deficit = GetTargetNobleClanCount(kingdom) - GetCurrentNobleClanCount(kingdom);
            if (deficit < MinimumClanDeficitForNewHouse)
                continue;
            if (HasPendingHouse(kingdom.StringId))
                continue;
            if (_kingdomCooldownUntilDays.TryGetValue(kingdom.StringId, out var cooldownUntil) && now < cooldownUntil)
                continue;

            TryQueueNewHouse(kingdom, now, deficit);
        }
    }

    private void OnHourlyTick()
    {
        if (Campaign.Current == null)
            return;

        if (!_legacyRepairDone)
        {
            RepairLegacyIncompleteHouses();
            _legacyRepairDone = true;
        }

        if (_commitInProgress || _pendingReadyByKingdom.Count == 0)
            return;
        if (CampaignTime.Now.GetHourOfDay != CommitHour)
            return;
        if (!IsSafeWorldMutationWindow())
            return;

        var now = CampaignTime.Now.ToDays;
        var pendingKingdomId = SelectReadyPendingKingdom(now);
        if (string.IsNullOrWhiteSpace(pendingKingdomId))
            return;

        _commitInProgress = true;
        try
        {
            var founderId = _pendingFounderByKingdom.TryGetValue(pendingKingdomId, out var f) ? f : null;
            var homeId = _pendingHomeByKingdom.TryGetValue(pendingKingdomId, out var h) ? h : null;
            var kingdom = Kingdom.All.FirstOrDefault(k => k != null && string.Equals(k.StringId, pendingKingdomId, StringComparison.Ordinal));
            var founder = Hero.AllAliveHeroes.FirstOrDefault(hero => hero != null && string.Equals(hero.StringId, founderId, StringComparison.Ordinal));
            var homeSettlement = Settlement.All.FirstOrDefault(s => s != null && string.Equals(s.StringId, homeId, StringComparison.Ordinal));

            LogStage("COMMIT_VALIDATE", $"kingdom={pendingKingdomId}; founder={founderId}; home={homeId}");

            if (!CanCommitPendingHouse(kingdom, founder, homeSettlement))
            {
                LogStage("COMMIT_CANCEL", $"kingdom={pendingKingdomId}; pending candidate no longer satisfies safety conditions");
                ClearPending(pendingKingdomId);
                _kingdomCooldownUntilDays[pendingKingdomId] = now + FailureCooldownDays;
                return;
            }

            if (!TryCreateNewHouseNativeFactory(kingdom, founder, homeSettlement, out var newClan))
            {
                LogStage("COMMIT_FAILED", $"kingdom={kingdom.StringId}; founder={founder.StringId}");
                ClearPending(pendingKingdomId);
                _kingdomCooldownUntilDays[pendingKingdomId] = now + FailureCooldownDays;
                return;
            }

            _lastCreatedClanId = newClan.StringId;
            _kingdomCooldownUntilDays[kingdom.StringId] = now + PerKingdomCooldownDays;

            LogStage("COMMIT_SUCCESS", $"clan={newClan.StringId}; kingdom={kingdom.StringId}; founder={founder.StringId}");
            ClearPending(pendingKingdomId);
        }
        catch (Exception ex)
        {
            LogStage("COMMIT_MANAGED_EXCEPTION", $"kingdom={pendingKingdomId}; type={ex.GetType().FullName}; message={ex.Message}");
            ClearPending(pendingKingdomId);
            _kingdomCooldownUntilDays[pendingKingdomId] = now + FailureCooldownDays;
        }
        finally
        {
            _commitInProgress = false;
        }
    }

    private string SelectReadyPendingKingdom(double now)
    {
        return _pendingReadyByKingdom
            .Where(kv => kv.Value <= now && HasPendingHouse(kv.Key))
            .Select(kv => new
            {
                KingdomId = kv.Key,
                Kingdom = Kingdom.All.FirstOrDefault(k => k != null && string.Equals(k.StringId, kv.Key, StringComparison.Ordinal))
            })
            .Where(x => x.Kingdom != null)
            .OrderByDescending(x => GetTargetNobleClanCount(x.Kingdom) - GetCurrentNobleClanCount(x.Kingdom))
            .ThenBy(x => x.KingdomId, StringComparer.Ordinal)
            .Select(x => x.KingdomId)
            .FirstOrDefault();
    }

    private static bool IsSafeWorldMutationWindow()
    {
        var campaign = Campaign.Current;
        var mainParty = MobileParty.MainParty;
        if (campaign == null || mainParty == null)
            return false;

        if (campaign.CurrentMenuContext != null)
            return false;
        if (mainParty.CurrentSettlement != null)
            return false;
        if (mainParty.MapEvent != null)
            return false;
        if (mainParty.BesiegedSettlement != null)
            return false;
        if (PlayerEncounter.Current != null)
            return false;
        if (Hero.MainHero?.IsPrisoner == true)
            return false;

        return true;
    }

    private bool TryQueueNewHouse(Kingdom kingdom, double now, int deficit)
    {
        var ruler = kingdom?.Leader;
        if (kingdom == null || ruler == null || ruler == Hero.MainHero)
            return false;
        if (ruler.Gold < NewHouseMinimumRulerGold)
        {
            LogStage("QUEUE_SKIP", $"kingdom={kingdom.StringId}; reason=ruler_gold; gold={ruler.Gold}; deficit={deficit}");
            return false;
        }

        var homeSettlement = kingdom.Settlements
            .Where(settlement =>
                settlement != null &&
                settlement.IsFortification &&
                !settlement.IsUnderSiege &&
                !string.Equals(settlement.StringId, TorSpecialSettlementId, StringComparison.Ordinal))
            .OrderByDescending(settlement => settlement.IsTown)
            .ThenBy(settlement => settlement.StringId, StringComparer.Ordinal)
            .FirstOrDefault();

        if (homeSettlement == null)
        {
            LogStage("QUEUE_SKIP", $"kingdom={kingdom.StringId}; reason=no_safe_home; deficit={deficit}");
            return false;
        }

        var founder = FindBestFounder(kingdom, ruler);
        if (founder == null)
        {
            LogStage("QUEUE_SKIP", $"kingdom={kingdom.StringId}; reason=no_eligible_founder; deficit={deficit}; clans={GetCurrentNobleClanCount(kingdom)}; target={GetTargetNobleClanCount(kingdom)}");
            return false;
        }

        _pendingFounderByKingdom[kingdom.StringId] = founder.StringId;
        _pendingHomeByKingdom[kingdom.StringId] = homeSettlement.StringId;
        _pendingReadyByKingdom[kingdom.StringId] = now + PendingDelayDays;

        LogStage(
            "QUEUE",
            $"kingdom={kingdom.StringId}; deficit={deficit}; founder={founder.StringId}; isLord={founder.IsLord}; aiCompanion={TorFamilySafety.IsAiCompanion(founder)}; home={homeSettlement.StringId}; readyAfter={now + PendingDelayDays:0.000}");
        return true;
    }

    private static Hero FindBestFounder(Kingdom kingdom, Hero ruler)
    {
        return kingdom.Clans
            .Where(clan => IsEligibleSourceClan(clan, kingdom))
            .SelectMany(clan => Hero.AllAliveHeroes
                .Where(hero => hero != null && hero.Clan == clan)
                .Where(hero => IsEligibleFounder(hero, clan, ruler))
                .Select(hero => new HeroCandidate(hero, ScoreFounder(hero, clan, ruler))))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Hero.StringId, StringComparer.Ordinal)
            .Select(x => x.Hero)
            .FirstOrDefault();
    }

    private static bool IsEligibleSourceClan(Clan clan, Kingdom kingdom)
    {
        if (clan == null || clan == Clan.PlayerClan || clan.Kingdom != kingdom)
            return false;
        if (clan.IsEliminated || clan.IsBanditFaction || clan.IsRebelClan || clan.IsMinorFaction || clan.IsClanTypeMercenary)
            return false;
        return clan.Leader != null && clan.Leader.IsAlive;
    }

    private static bool CanCommitPendingHouse(Kingdom kingdom, Hero founder, Settlement homeSettlement)
    {
        if (kingdom == null || kingdom.IsEliminated || kingdom.Leader == null || kingdom.Leader == Hero.MainHero)
            return false;
        if (GetTargetNobleClanCount(kingdom) - GetCurrentNobleClanCount(kingdom) < MinimumClanDeficitForNewHouse)
            return false;
        if (kingdom.Leader.Gold < NewHouseMinimumRulerGold)
            return false;

        var sourceClan = founder?.Clan;
        if (!IsEligibleSourceClan(sourceClan, kingdom) || !IsEligibleFounder(founder, sourceClan, kingdom.Leader))
            return false;

        if (homeSettlement == null || !homeSettlement.IsFortification || homeSettlement.IsUnderSiege)
            return false;
        if (homeSettlement.MapFaction != kingdom)
            return false;
        if (string.Equals(homeSettlement.StringId, TorSpecialSettlementId, StringComparison.Ordinal))
            return false;

        return true;
    }

    private static bool IsEligibleFounder(Hero hero, Clan sourceClan, Hero ruler)
    {
        if (hero == null || sourceClan == null || sourceClan == Clan.PlayerClan)
            return false;
        if (hero == ruler || hero == ruler.Spouse || hero == sourceClan.Leader)
            return false;
        if (!hero.IsAlive || !hero.IsActive || hero.IsTemplate || hero.IsMinorFactionHero)
            return false;
        if (hero.Clan != sourceClan)
            return false;
        if (hero.Age < Campaign.Current.Models.AgeModel.HeroComesOfAge)
            return false;

        // TOR marks a number of unattached house members as AI companions even though
        // Bannerlord's CompanionOf link is null. They are valid founders once their
        // occupation is promoted to Lord during the native clan transaction.
        var canBeElevated = hero.IsLord || TorFamilySafety.IsAiCompanion(hero);
        if (!canBeElevated)
            return false;

        if (hero.Spouse != null || hero.Children.Count > 0)
            return false;
        if (hero.PartyBelongedToAsPrisoner != null || hero.IsPrisoner)
            return false;
        if (hero.PartyBelongedTo != null || hero.GovernorOf != null)
            return false;

        return true;
    }

    private static int ScoreFounder(Hero hero, Clan sourceClan, Hero ruler)
    {
        var score = 0;

        if (sourceClan == ruler.Clan)
            score += 45;
        if (hero.Father == ruler || hero.Mother == ruler)
            score += 120;
        else if ((ruler.Father != null && hero.Father == ruler.Father) ||
                 (ruler.Mother != null && hero.Mother == ruler.Mother))
            score += 90;
        else if (hero.IsLord)
            score += 55;

        if (!hero.IsLord && TorFamilySafety.IsAiCompanion(hero))
            score += 40;

        score += ruler.GetRelation(hero);
        score += Math.Min(30, hero.Level / 2);
        return score;
    }

    private static bool TryCreateNewHouseNativeFactory(Kingdom kingdom, Hero founder, Settlement homeSettlement, out Clan newClan)
    {
        newClan = null;

        var ruler = kingdom?.Leader;
        var sourceClan = founder?.Clan;
        if (kingdom == null || ruler == null || sourceClan == null || sourceClan == Clan.PlayerClan)
            return false;
        if (sourceClan.Kingdom != kingdom || homeSettlement?.MapFaction != kingdom)
            return false;

        var culture = founder.Culture ?? sourceClan.Culture ?? kingdom.Culture;
        var clanName = NameGenerator.Current.GenerateClanName(culture, homeSettlement) ?? founder.Name;
        var sourceClanId = sourceClan.StringId;
        var wasLord = founder.IsLord;
        var wasAiCompanion = TorFamilySafety.IsAiCompanion(founder);

        LogStage(
            "NATIVE_FACTORY_BEGIN",
            $"kingdom={kingdom.StringId}; sourceClan={sourceClanId}; founder={founder.StringId}; wasLord={wasLord}; aiCompanion={wasAiCompanion}; home={homeSettlement.StringId}");

        // Bannerlord owns the hero->new-clan transaction. We deliberately retain this
        // factory because it proved native-stable in the v0.6.1 crashfix. The missing
        // step in v0.6.3.1/2 was the occupation transition for TOR AI companions.
        newClan = Clan.CreateCompanionToLordClan(founder, homeSettlement, clanName, -1);
        if (newClan == null)
        {
            LogStage("NATIVE_FACTORY_NULL", $"founder={founder.StringId}; sourceClan={sourceClanId}");
            return false;
        }

        LogStage(
            "NATIVE_FACTORY_OK",
            $"clan={newClan.StringId}; founderClan={founder.Clan?.StringId ?? "null"}; currentKingdom={newClan.Kingdom?.StringId ?? "null"}; isLord={founder.IsLord}");

        if (!founder.IsLord)
        {
            LogStage("PROMOTE_OCCUPATION_BEGIN", $"founder={founder.StringId}; clan={newClan.StringId}; from={founder.Occupation}");
            founder.SetNewOccupation(Occupation.Lord);
            LogStage("PROMOTE_OCCUPATION_OK", $"founder={founder.StringId}; isLord={founder.IsLord}; occupation={founder.Occupation}");
        }

        if (newClan.Kingdom != kingdom)
        {
            LogStage("JOIN_KINGDOM_BEGIN", $"clan={newClan.StringId}; target={kingdom.StringId}");
            ChangeKingdomAction.ApplyByJoinToKingdom(newClan, kingdom, CampaignTime.DaysFromNow(365f), true);
            LogStage("JOIN_KINGDOM_OK", $"clan={newClan.StringId}; currentKingdom={newClan.Kingdom?.StringId ?? "null"}");
        }

        if (newClan.Kingdom != kingdom || newClan.Leader != founder || founder.Clan != newClan || !founder.IsLord || !newClan.IsNoble)
        {
            LogStage(
                "POSTCONDITION_FAILED",
                $"clan={newClan.StringId}; kingdom={newClan.Kingdom?.StringId ?? "null"}; leader={newClan.Leader?.StringId ?? "null"}; founderClan={founder.Clan?.StringId ?? "null"}; isLord={founder.IsLord}; isNoble={newClan.IsNoble}");
            return false;
        }

        if (ruler.Gold >= NewHouseSeedGold)
        {
            LogStage("SEED_GOLD_BEGIN", $"ruler={ruler.StringId}; founder={founder.StringId}; amount={NewHouseSeedGold}");
            GiveGoldAction.ApplyBetweenCharacters(ruler, founder, NewHouseSeedGold, false);
            LogStage("SEED_GOLD_OK", $"clan={newClan.StringId}");
        }

        return true;
    }

    private static void RepairLegacyIncompleteHouses()
    {
        foreach (var clan in Clan.All
                     .Where(c => c != null && !c.IsEliminated && c.IsNoble && c.Leader != null)
                     .Where(c => c.StringId != null && c.StringId.IndexOf("_companion_clan", StringComparison.OrdinalIgnoreCase) >= 0)
                     .Where(c => c.Leader.Clan == c && !c.Leader.IsLord)
                     .ToArray())
        {
            try
            {
                var leader = clan.Leader;
                LogStage("LEGACY_REPAIR_BEGIN", $"clan={clan.StringId}; kingdom={clan.Kingdom?.StringId ?? "null"}; leader={leader.StringId}; occupation={leader.Occupation}");
                leader.SetNewOccupation(Occupation.Lord);
                LogStage("LEGACY_REPAIR_OK", $"clan={clan.StringId}; leader={leader.StringId}; isLord={leader.IsLord}");
            }
            catch (Exception ex)
            {
                LogStage("LEGACY_REPAIR_FAILED", $"clan={clan.StringId}; type={ex.GetType().FullName}; message={ex.Message}");
            }
        }
    }

    public IEnumerable<string> DescribeStatus()
    {
        var now = CampaignTime.Now.ToDays;
        var pending = _pendingReadyByKingdom
            .Where(kv => HasPendingHouse(kv.Key))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}:{Math.Max(0d, kv.Value - now):0.0}d")
            .ToArray();

        yield return
            $"Realm-house growth: pending={pending.Length} [{string.Join(",", pending)}]; commitBusy={_commitInProgress}; " +
            $"perKingdomCooldown={PerKingdomCooldownDays}d; failureCooldown={FailureCooldownDays}d; minimumDeficit={MinimumClanDeficitForNewHouse}; " +
            $"founders=lords+TOR-ai-companions; commit=one-per-safe-day-largest-deficit; lastCreated={_lastCreatedClanId ?? "none"}.";
    }

    private bool HasPendingHouse(string kingdomId)
        => !string.IsNullOrWhiteSpace(kingdomId) &&
           _pendingFounderByKingdom.TryGetValue(kingdomId, out var founderId) && !string.IsNullOrWhiteSpace(founderId) &&
           _pendingHomeByKingdom.TryGetValue(kingdomId, out var homeId) && !string.IsNullOrWhiteSpace(homeId) &&
           _pendingReadyByKingdom.ContainsKey(kingdomId);

    private void ClearPending(string kingdomId)
    {
        if (string.IsNullOrWhiteSpace(kingdomId))
            return;
        _pendingFounderByKingdom.Remove(kingdomId);
        _pendingHomeByKingdom.Remove(kingdomId);
        _pendingReadyByKingdom.Remove(kingdomId);
    }

    private static int GetCurrentNobleClanCount(Kingdom kingdom)
        => kingdom.Clans.Count(clan =>
            clan != null &&
            !clan.IsEliminated &&
            !clan.IsMinorFaction &&
            !clan.IsClanTypeMercenary);

    private static int GetTargetNobleClanCount(Kingdom kingdom)
    {
        var fortifications = kingdom.Settlements.Count(settlement => settlement != null && settlement.IsFortification);
        var target = 2 + (int)Math.Ceiling(fortifications / 2d);
        return Math.Max(3, Math.Min(MaximumTargetNobleClans, target));
    }

    private static void LogStage(string stage, string details)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KaiTORDiplomacy");
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, "KaiTORRealmHouse.log");
            var line = $"{DateTime.UtcNow:O}|{stage}|{details}{Environment.NewLine}";
            File.AppendAllText(path, line);
        }
        catch
        {
            // Diagnostics must never affect campaign simulation.
        }
    }

    private sealed class HeroCandidate
    {
        public HeroCandidate(Hero hero, int score)
        {
            Hero = hero;
            Score = score;
        }

        public Hero Hero { get; }
        public int Score { get; }
    }
}
