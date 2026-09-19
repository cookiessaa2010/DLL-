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
/// v0.6.4 scans clan deficits daily, admits TOR AI companions who are ordinary members
/// of safe lord parties, removes such a founder from the party roster using the same
/// TroopRoster operation used by Bannerlord's CompanionRolesCampaignBehavior, and then
/// delegates the hero->clan graph transaction to Clan.CreateCompanionToLordClan.
/// No KaiTOR code directly assigns Hero.Clan or Clan.Kingdom.
/// </summary>
public sealed class KaiRealmHouseGrowthBehavior : CampaignBehaviorBase
{
    private const int MaximumTargetNobleClans = 12;
    private const int MinimumClanDeficitForNewHouse = 1;
    // A realm with a real clan deficit should be able to recover during play rather
    // than waiting six campaign weeks for every successful house.
    private const int PerKingdomCooldownDays = 3;
    private const int FailureCooldownDays = 1;
    private const double PendingDelayDays = 1d / 24d;
    private const int MaxSuccessfulCommitsPerDay = 4;
    private const int NewHouseMinimumRulerGold = 30000;
    private const int NewHouseSeedGold = 10000;
    private const string TorSpecialSettlementId = "castle_BK1";

    // Preserve all existing save keys for v0.6.3.3 compatibility.
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
    private int _successfulCommitDay = -1;
    private int _successfulCommitsToday;

    public override void RegisterEvents()
    {
        // Master-TZ v0.6.4: first missing house must be discovered daily, not weekly.
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
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

    private void OnDailyTick()
    {
        if (Campaign.Current == null || _commitInProgress)
            return;

        var now = CampaignTime.Now.ToDays;
        foreach (var kingdom in Kingdom.All
                     .Where(k => k != null && !k.IsEliminated && k.Leader != null && k.Leader != Hero.MainHero)
                     .OrderByDescending(k => GetTargetNobleClanCount(k) - GetCurrentNobleClanCount(k))
                     .ThenBy(k => k.StringId, StringComparer.Ordinal))
        {
            var current = GetCurrentNobleClanCount(kingdom);
            var target = GetTargetNobleClanCount(kingdom);
            var deficit = target - current;

            if (deficit < MinimumClanDeficitForNewHouse)
                continue;

            if (HasPendingHouse(kingdom.StringId))
            {
                LogStage("REALM_SCAN", $"kingdom={kingdom.StringId}; currentClans={current}; targetClans={target}; deficit={deficit}; state=pending");
                continue;
            }

            if (_kingdomCooldownUntilDays.TryGetValue(kingdom.StringId, out var cooldownUntil) && now < cooldownUntil)
            {
                LogStage("REALM_SCAN", $"kingdom={kingdom.StringId}; currentClans={current}; targetClans={target}; deficit={deficit}; state=cooldown; remaining={cooldownUntil - now:0.0}d");
                continue;
            }

            TryQueueNewHouse(kingdom, now, deficit, current, target);
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

        var now = CampaignTime.Now.ToDays;
        var currentDay = (int)Math.Floor(now);
        if (_successfulCommitDay != currentDay)
        {
            _successfulCommitDay = currentDay;
            _successfulCommitsToday = 0;
        }

        if (_commitInProgress || _pendingReadyByKingdom.Count == 0)
            return;
        if (_successfulCommitsToday >= MaxSuccessfulCommitsPerDay)
            return;
        if (!IsSafeWorldMutationWindow())
            return;
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

            if (!CanCommitPendingHouse(kingdom, founder, homeSettlement, out var commitReason))
            {
                LogStage("REALM_CREATE_FAIL", $"kingdom={pendingKingdomId}; founder={founderId}; stage=validate; reason={commitReason}");
                KaiRuntimeLog.Write("REALM_CREATE_FAIL", $"kingdom={pendingKingdomId}; founder={founderId}; reason={commitReason}");
                ClearPending(pendingKingdomId);

                // Most live-test failures were founders becoming party leaders between
                // queue and commit. Re-scan immediately instead of wasting a full week.
                if (kingdom != null && IsFounderRetryReason(commitReason))
                {
                    var current = GetCurrentNobleClanCount(kingdom);
                    var target = GetTargetNobleClanCount(kingdom);
                    var deficit = target - current;
                    if (deficit >= MinimumClanDeficitForNewHouse &&
                        TryQueueNewHouse(kingdom, now, deficit, current, target))
                    {
                        LogStage("REALM_REQUEUE", $"kingdom={pendingKingdomId}; oldFounder={founderId}; reason={commitReason}; retryDelay={PendingDelayDays:0.000}d");
                        return;
                    }
                }

                _kingdomCooldownUntilDays[pendingKingdomId] = now + FailureCooldownDays;
                return;
            }

            if (!TryCreateNewHouseNativeFactory(kingdom, founder, homeSettlement, out var newClan))
            {
                LogStage("REALM_CREATE_FAIL", $"kingdom={kingdom.StringId}; founder={founder.StringId}; stage=native_factory");
                KaiRuntimeLog.Write("REALM_CREATE_FAIL", $"kingdom={kingdom.StringId}; founder={founder.StringId}; stage=native_factory");
                ClearPending(pendingKingdomId);
                _kingdomCooldownUntilDays[pendingKingdomId] = now + FailureCooldownDays;
                return;
            }

            _lastCreatedClanId = newClan.StringId;
            _kingdomCooldownUntilDays[kingdom.StringId] = now + PerKingdomCooldownDays;
            _successfulCommitsToday++;

            LogStage("REALM_CREATE_SUCCESS", $"clan={newClan.StringId}; kingdom={kingdom.StringId}; founder={founder.StringId}; cooldown={PerKingdomCooldownDays}d");
            KaiRuntimeLog.Write("REALM_CREATE_SUCCESS", $"clan={newClan.StringId}; kingdom={kingdom.StringId}; founder={founder.StringId}");
            ClearPending(pendingKingdomId);
        }
        catch (Exception ex)
        {
            LogStage("REALM_CREATE_FAIL", $"kingdom={pendingKingdomId}; stage=managed_exception; type={ex.GetType().FullName}; message={ex.Message}");
            KaiRuntimeLog.Exception("REALM_CREATE_FAIL", ex, $"kingdom={pendingKingdomId}; stage=managed_exception");
            ClearPending(pendingKingdomId);
            _kingdomCooldownUntilDays[pendingKingdomId] = now + FailureCooldownDays;
        }
        finally
        {
            _commitInProgress = false;
        }
    }

    private static bool IsFounderRetryReason(string reason)
        => !string.IsNullOrWhiteSpace(reason) &&
           (reason.StartsWith("founder_", StringComparison.Ordinal) ||
            string.Equals(reason, "source_clan_invalid", StringComparison.Ordinal));

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
        var mainParty = MobileParty.MainParty;
        if (Campaign.Current == null || mainParty == null)
            return false;

        // Being parked in a town/castle is not dangerous and must not freeze realm
        // growth indefinitely. Only actual campaign-graph hazard states are blocked.
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

    private bool TryQueueNewHouse(Kingdom kingdom, double now, int deficit, int current, int target)
    {
        var ruler = kingdom?.Leader;
        if (kingdom == null || ruler == null || ruler == Hero.MainHero)
            return false;

        if (ruler.Gold < NewHouseMinimumRulerGold)
        {
            LogStage("REALM_SKIP", $"kingdom={kingdom.StringId}; currentClans={current}; targetClans={target}; deficit={deficit}; reason=ruler_gold; gold={ruler.Gold}");
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
            LogStage("REALM_SKIP", $"kingdom={kingdom.StringId}; currentClans={current}; targetClans={target}; deficit={deficit}; reason=no_safe_home");
            return false;
        }

        var scan = ScanFounders(kingdom, ruler);
        LogStage(
            "REALM_SCAN",
            $"kingdom={kingdom.StringId}; currentClans={current}; targetClans={target}; deficit={deficit}; founderCandidates={scan.Eligible.Count}; " +
            $"rejectedByParty={scan.RejectedByParty}; rejectedByPartyLeader={scan.RejectedByPartyLeader}; rejectedByFamily={scan.RejectedByFamily}; " +
            $"rejectedByGovernor={scan.RejectedByGovernor}; rejectedByPrison={scan.RejectedByPrison}; rejectedByMapEvent={scan.RejectedByMapEvent}; " +
            $"rejectedByOccupation={scan.RejectedByOccupation}; rejectedOther={scan.RejectedOther}");

        var founder = scan.Eligible
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Hero.StringId, StringComparer.Ordinal)
            .Select(x => x.Hero)
            .FirstOrDefault();

        if (founder == null)
        {
            LogStage("REALM_SKIP", $"kingdom={kingdom.StringId}; currentClans={current}; targetClans={target}; deficit={deficit}; reason=no_eligible_founder");
            return false;
        }

        _pendingFounderByKingdom[kingdom.StringId] = founder.StringId;
        _pendingHomeByKingdom[kingdom.StringId] = homeSettlement.StringId;
        _pendingReadyByKingdom[kingdom.StringId] = now + PendingDelayDays;

        LogStage(
            "QUEUE",
            $"kingdom={kingdom.StringId}; deficit={deficit}; founder={founder.StringId}; isLord={founder.IsLord}; aiCompanion={TorFamilySafety.IsAiCompanion(founder)}; " +
            $"party={founder.PartyBelongedTo?.StringId ?? "none"}; home={homeSettlement.StringId}; readyAfter={now + PendingDelayDays:0.000}");
        KaiRuntimeLog.Write("REALM_QUEUE", $"kingdom={kingdom.StringId}; founder={founder.StringId}; home={homeSettlement.StringId}; deficit={deficit}");
        return true;
    }

    private static FounderScan ScanFounders(Kingdom kingdom, Hero ruler)
    {
        var scan = new FounderScan();
        foreach (var clan in kingdom.Clans.Where(clan => IsEligibleSourceClan(clan, kingdom)))
        {
            foreach (var hero in Hero.AllAliveHeroes.Where(hero => hero != null && hero.Clan == clan))
            {
                var reason = GetFounderRejectionReason(hero, clan, ruler);
                if (reason == FounderRejection.None)
                {
                    scan.Eligible.Add(new HeroCandidate(hero, ScoreFounder(hero, clan, ruler)));
                    continue;
                }

                switch (reason)
                {
                    case FounderRejection.Party: scan.RejectedByParty++; break;
                    case FounderRejection.PartyLeader: scan.RejectedByPartyLeader++; break;
                    case FounderRejection.Family: scan.RejectedByFamily++; break;
                    case FounderRejection.Governor: scan.RejectedByGovernor++; break;
                    case FounderRejection.Prison: scan.RejectedByPrison++; break;
                    case FounderRejection.MapEvent: scan.RejectedByMapEvent++; break;
                    case FounderRejection.Occupation: scan.RejectedByOccupation++; break;
                    default: scan.RejectedOther++; break;
                }
            }
        }
        return scan;
    }

    private static bool IsEligibleSourceClan(Clan clan, Kingdom kingdom)
    {
        if (clan == null || clan == Clan.PlayerClan || clan.Kingdom != kingdom)
            return false;
        if (clan.IsEliminated || clan.IsBanditFaction || clan.IsRebelClan || clan.IsMinorFaction || clan.IsClanTypeMercenary)
            return false;
        return clan.Leader != null && clan.Leader.IsAlive;
    }

    private static bool CanCommitPendingHouse(Kingdom kingdom, Hero founder, Settlement homeSettlement, out string reason)
    {
        reason = string.Empty;
        if (kingdom == null || kingdom.IsEliminated || kingdom.Leader == null || kingdom.Leader == Hero.MainHero)
        {
            reason = "kingdom_invalid";
            return false;
        }
        if (GetTargetNobleClanCount(kingdom) - GetCurrentNobleClanCount(kingdom) < MinimumClanDeficitForNewHouse)
        {
            reason = "deficit_closed";
            return false;
        }
        if (kingdom.Leader.Gold < NewHouseMinimumRulerGold)
        {
            reason = "ruler_gold";
            return false;
        }

        var sourceClan = founder?.Clan;
        if (!IsEligibleSourceClan(sourceClan, kingdom))
        {
            reason = "source_clan_invalid";
            return false;
        }

        var rejection = GetFounderRejectionReason(founder, sourceClan, kingdom.Leader);
        if (rejection != FounderRejection.None)
        {
            reason = "founder_" + rejection;
            return false;
        }

        if (homeSettlement == null || !homeSettlement.IsFortification || homeSettlement.IsUnderSiege)
        {
            reason = "home_invalid";
            return false;
        }
        if (homeSettlement.MapFaction != kingdom)
        {
            reason = "home_wrong_faction";
            return false;
        }
        if (string.Equals(homeSettlement.StringId, TorSpecialSettlementId, StringComparison.Ordinal))
        {
            reason = "special_home";
            return false;
        }

        return true;
    }

    private static FounderRejection GetFounderRejectionReason(Hero hero, Clan sourceClan, Hero ruler)
    {
        if (hero == null || sourceClan == null || sourceClan == Clan.PlayerClan)
            return FounderRejection.Other;
        if (hero == ruler || hero == ruler.Spouse || hero == sourceClan.Leader)
            return FounderRejection.Other;
        if (!hero.IsAlive || !hero.IsActive || hero.IsTemplate || hero.IsMinorFactionHero)
            return FounderRejection.Other;
        if (hero.Clan != sourceClan)
            return FounderRejection.Other;
        if (hero.Age < Campaign.Current.Models.AgeModel.HeroComesOfAge)
            return FounderRejection.Other;

        var canBeElevated = hero.IsLord || TorFamilySafety.IsAiCompanion(hero);
        if (!canBeElevated)
            return FounderRejection.Occupation;

        if (hero.Spouse != null || hero.Children.Count > 0)
            return FounderRejection.Family;
        if (hero.PartyBelongedToAsPrisoner != null || hero.IsPrisoner)
            return FounderRejection.Prison;
        if (hero.GovernorOf != null)
            return FounderRejection.Governor;

        var party = hero.PartyBelongedTo;
        if (party == null)
            return FounderRejection.None;

        if (party.LeaderHero == hero)
            return FounderRejection.PartyLeader;
        if (party.MapEvent != null)
            return FounderRejection.MapEvent;
        if (party.BesiegedSettlement != null)
            return FounderRejection.MapEvent;
        if (party.Army != null)
            return FounderRejection.Party;
        if (hero.CharacterObject == null || party.MemberRoster.GetTroopCount(hero.CharacterObject) <= 0)
            return FounderRejection.Party;

        // Ordinary non-leader member of a safe lord party: eligible in v0.6.4.
        return FounderRejection.None;
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

        // Prefer already-unattached heroes over party detachment when scores are close.
        if (hero.PartyBelongedTo == null)
            score += 20;

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

        var sourceParty = founder.PartyBelongedTo;
        var detachedFromParty = false;

        try
        {
            if (sourceParty != null)
            {
                if (!TryDetachFounderFromOrdinaryParty(founder, sourceParty))
                {
                    LogStage("REALM_CREATE_FAIL", $"kingdom={kingdom.StringId}; founder={founder.StringId}; stage=detach_party");
                    return false;
                }
                detachedFromParty = true;
            }

            var companionClan = founder.CompanionOf;
            if (companionClan != null)
            {
                LogStage("FOUNDER_COMPANION_RELEASE", $"founder={founder.StringId}; companionOf={companionClan.StringId}");
                RemoveCompanionAction.ApplyByByTurningToLord(companionClan, founder);
            }

            var culture = founder.Culture ?? sourceClan.Culture ?? kingdom.Culture;
            var clanName = NameGenerator.Current.GenerateClanName(culture, homeSettlement) ?? founder.Name;
            var sourceClanId = sourceClan.StringId;
            var wasLord = founder.IsLord;
            var wasAiCompanion = TorFamilySafety.IsAiCompanion(founder);

            LogStage(
                "NATIVE_FACTORY_BEGIN",
                $"kingdom={kingdom.StringId}; sourceClan={sourceClanId}; founder={founder.StringId}; wasLord={wasLord}; aiCompanion={wasAiCompanion}; home={homeSettlement.StringId}; detachedParty={sourceParty?.StringId ?? "none"}");

            // Engine-owned graph transaction. KaiTOR never assigns Hero.Clan/Clan.Kingdom.
            newClan = Clan.CreateCompanionToLordClan(founder, homeSettlement, clanName, -1);
            if (newClan == null)
            {
                LogStage("REALM_CREATE_FAIL", $"founder={founder.StringId}; sourceClan={sourceClanId}; stage=native_factory_null");
                RestoreFounderPartyIfSafe(founder, sourceClan, sourceParty, detachedFromParty);
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

            var stillInOldRoster = sourceParty != null && founder.CharacterObject != null &&
                                   sourceParty.MemberRoster.GetTroopCount(founder.CharacterObject) > 0;

            if (newClan.Kingdom != kingdom ||
                newClan.Leader != founder ||
                founder.Clan != newClan ||
                !founder.IsLord ||
                !newClan.IsNoble ||
                stillInOldRoster)
            {
                LogStage(
                    "REALM_CREATE_FAIL",
                    $"stage=postcondition; clan={newClan.StringId}; kingdom={newClan.Kingdom?.StringId ?? "null"}; leader={newClan.Leader?.StringId ?? "null"}; " +
                    $"founderClan={founder.Clan?.StringId ?? "null"}; isLord={founder.IsLord}; isNoble={newClan.IsNoble}; stillInOldRoster={stillInOldRoster}");
                return false;
            }

            if (ruler.Gold >= NewHouseSeedGold)
            {
                GiveGoldAction.ApplyBetweenCharacters(ruler, founder, NewHouseSeedGold, false);
                LogStage("SEED_GOLD_OK", $"clan={newClan.StringId}; amount={NewHouseSeedGold}");
            }

            return true;
        }
        catch (Exception ex)
        {
            LogStage("REALM_CREATE_FAIL", $"kingdom={kingdom.StringId}; founder={founder.StringId}; stage=factory_exception; type={ex.GetType().FullName}; message={ex.Message}");
            RestoreFounderPartyIfSafe(founder, sourceClan, sourceParty, detachedFromParty);
            return false;
        }
    }

    private static bool TryDetachFounderFromOrdinaryParty(Hero founder, MobileParty party)
    {
        if (founder?.CharacterObject == null || party == null)
            return false;
        if (party.LeaderHero == founder || party.MapEvent != null || party.BesiegedSettlement != null || party.Army != null)
            return false;

        var count = party.MemberRoster.GetTroopCount(founder.CharacterObject);
        if (count <= 0)
            return false;

        // This is the same native TroopRoster operation Bannerlord uses after granting
        // a companion a clan. Only one hero entry is removed.
        party.MemberRoster.AddToCounts(founder.CharacterObject, -1, false, 0, 0, true, -1);
        var remaining = party.MemberRoster.GetTroopCount(founder.CharacterObject);
        LogStage("FOUNDER_PARTY_DETACH", $"founder={founder.StringId}; party={party.StringId}; before={count}; after={remaining}");
        return remaining < count;
    }

    private static void RestoreFounderPartyIfSafe(Hero founder, Clan originalClan, MobileParty originalParty, bool wasDetached)
    {
        if (!wasDetached || founder?.CharacterObject == null || originalClan == null || originalParty == null)
            return;
        if (founder.Clan != originalClan || founder.PartyBelongedTo != null)
            return;

        try
        {
            if (originalParty.MemberRoster.GetTroopCount(founder.CharacterObject) <= 0)
            {
                originalParty.MemberRoster.AddToCounts(founder.CharacterObject, 1, false, 0, 0, true, -1);
                LogStage("FOUNDER_PARTY_RESTORE", $"founder={founder.StringId}; party={originalParty.StringId}");
            }
        }
        catch (Exception ex)
        {
            LogStage("FOUNDER_PARTY_RESTORE_FAIL", $"founder={founder.StringId}; party={originalParty.StringId}; type={ex.GetType().FullName}; message={ex.Message}");
        }
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
            $"scan=daily; perKingdomCooldown={PerKingdomCooldownDays}d; failureCooldown={FailureCooldownDays}d; minimumDeficit={MinimumClanDeficitForNewHouse}; " +
            $"founders=unattached+safe-party-member TOR AI companions/lords; commit=up-to-{MaxSuccessfulCommitsPerDay}-per-safe-day; today={_successfulCommitsToday}; lastCreated={_lastCreatedClanId ?? "none"}.";
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

    private sealed class FounderScan
    {
        public readonly List<HeroCandidate> Eligible = new();
        public int RejectedByParty;
        public int RejectedByPartyLeader;
        public int RejectedByFamily;
        public int RejectedByGovernor;
        public int RejectedByPrison;
        public int RejectedByMapEvent;
        public int RejectedByOccupation;
        public int RejectedOther;
    }

    private enum FounderRejection
    {
        None,
        Party,
        PartyLeader,
        Family,
        Governor,
        Prison,
        MapEvent,
        Occupation,
        Other
    }
}
