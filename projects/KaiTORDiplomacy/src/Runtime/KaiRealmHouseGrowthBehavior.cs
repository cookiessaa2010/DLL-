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
/// WeeklyTick only queues one candidate. Actual clan graph mutation runs later from
/// HourlyTick on the open campaign map, never from settlement-entry callbacks and never
/// inside TOR's Daily/Weekly hero-generation burst.
///
/// v0.6.1 crash fix: the commit path uses Bannerlord's native
/// Clan.CreateCompanionToLordClan factory and ChangeKingdomAction instead of manually
/// wiring Clan/Kingdom/Hero/Leader references. A small stage log is written immediately
/// before every graph-changing native call so a native access violation has a precise
/// last-known boundary.
/// </summary>
public sealed class KaiRealmHouseGrowthBehavior : CampaignBehaviorBase
{
    private const int MaximumTargetNobleClans = 12;
    private const int MinimumClanDeficitForNewHouse = 1;
    private const int PerKingdomCooldownDays = 42;
    private const int GlobalCooldownDays = 14;
    private const int FailureCooldownDays = 7;
    private const int PendingDelayDays = 1;
    private const int NewHouseMinimumRulerGold = 30000;
    private const int NewHouseSeedGold = 10000;
    private const int CommitHour = 4;
    private const string TorSpecialSettlementId = "castle_BK1";

    private const string KingdomCooldownSaveKey = "kaitor_realm_house_kingdom_cooldown_v2";
    private const string GlobalCooldownSaveKey = "kaitor_realm_house_global_cooldown_v2";
    private const string PendingKingdomSaveKey = "kaitor_realm_house_pending_kingdom_v2";
    private const string PendingFounderSaveKey = "kaitor_realm_house_pending_founder_v2";
    private const string PendingHomeSaveKey = "kaitor_realm_house_pending_home_v2";
    private const string PendingReadySaveKey = "kaitor_realm_house_pending_ready_v2";
    private const string LastCreatedClanSaveKey = "kaitor_realm_house_last_created_v2";

    private Dictionary<string, double> _kingdomCooldownUntilDays = new();
    private double _globalCooldownUntilDay;
    private string _pendingKingdomId;
    private string _pendingFounderId;
    private string _pendingHomeSettlementId;
    private double _pendingReadyAfterDay;
    private string _lastCreatedClanId;
    private bool _commitInProgress;

    public override void RegisterEvents()
    {
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
        CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(KingdomCooldownSaveKey, ref _kingdomCooldownUntilDays);
        dataStore.SyncData(GlobalCooldownSaveKey, ref _globalCooldownUntilDay);
        dataStore.SyncData(PendingKingdomSaveKey, ref _pendingKingdomId);
        dataStore.SyncData(PendingFounderSaveKey, ref _pendingFounderId);
        dataStore.SyncData(PendingHomeSaveKey, ref _pendingHomeSettlementId);
        dataStore.SyncData(PendingReadySaveKey, ref _pendingReadyAfterDay);
        dataStore.SyncData(LastCreatedClanSaveKey, ref _lastCreatedClanId);
        _kingdomCooldownUntilDays ??= new Dictionary<string, double>();
    }

    private void OnWeeklyTick()
    {
        if (Campaign.Current == null || HasPendingHouse() || _commitInProgress)
            return;

        var now = CampaignTime.Now.ToDays;
        if (now < _globalCooldownUntilDay)
            return;

        var candidates = Kingdom.All
            .Where(k => k != null && !k.IsEliminated && k.Leader != null && k.Leader != Hero.MainHero)
            .Select(k => new KingdomNeed(k, GetTargetNobleClanCount(k) - GetCurrentNobleClanCount(k)))
            .Where(x => x.Deficit >= MinimumClanDeficitForNewHouse)
            .OrderByDescending(x => x.Deficit)
            .ThenByDescending(x => x.Kingdom.Settlements.Count(s => s != null && s.IsFortification))
            .ThenBy(x => x.Kingdom.StringId, StringComparer.Ordinal)
            .ToArray();

        foreach (var need in candidates)
        {
            if (TryQueueNewHouse(need.Kingdom, now))
                break;
        }
    }

    private void OnHourlyTick()
    {
        if (Campaign.Current == null || !HasPendingHouse() || _commitInProgress)
            return;
        if (CampaignTime.Now.ToDays < _pendingReadyAfterDay)
            return;
        if (CampaignTime.Now.GetHourOfDay != CommitHour)
            return;
        if (!IsSafeWorldMutationWindow())
            return;

        _commitInProgress = true;
        try
        {
            var kingdom = Kingdom.All.FirstOrDefault(k => k != null && string.Equals(k.StringId, _pendingKingdomId, StringComparison.Ordinal));
            var founder = Hero.AllAliveHeroes.FirstOrDefault(h => h != null && string.Equals(h.StringId, _pendingFounderId, StringComparison.Ordinal));
            var homeSettlement = Settlement.All.FirstOrDefault(s => s != null && string.Equals(s.StringId, _pendingHomeSettlementId, StringComparison.Ordinal));

            LogStage("COMMIT_VALIDATE", $"kingdom={_pendingKingdomId}; founder={_pendingFounderId}; home={_pendingHomeSettlementId}");

            if (!CanCommitPendingHouse(kingdom, founder, homeSettlement))
            {
                LogStage("COMMIT_CANCEL", "pending candidate no longer satisfies safety conditions");
                ClearPending();
                return;
            }

            if (!TryCreateNewHouseNativeFactory(kingdom, founder, homeSettlement, out var newClan))
            {
                LogStage("COMMIT_FAILED", $"kingdom={kingdom.StringId}; founder={founder.StringId}");
                ClearPending();
                _globalCooldownUntilDay = Math.Max(_globalCooldownUntilDay, CampaignTime.Now.ToDays + FailureCooldownDays);
                return;
            }

            _lastCreatedClanId = newClan.StringId;
            var now = CampaignTime.Now.ToDays;
            _kingdomCooldownUntilDays[kingdom.StringId] = now + PerKingdomCooldownDays;
            _globalCooldownUntilDay = now + GlobalCooldownDays;

            LogStage("COMMIT_SUCCESS", $"clan={newClan.StringId}; kingdom={kingdom.StringId}; founder={founder.StringId}");
            ClearPending();
        }
        catch (Exception ex)
        {
            LogStage("COMMIT_MANAGED_EXCEPTION", $"type={ex.GetType().FullName}; message={ex.Message}");
            ClearPending();
            _globalCooldownUntilDay = Math.Max(_globalCooldownUntilDay, CampaignTime.Now.ToDays + FailureCooldownDays);
        }
        finally
        {
            _commitInProgress = false;
        }
    }

    private static bool IsSafeWorldMutationWindow()
    {
        var campaign = Campaign.Current;
        var mainParty = MobileParty.MainParty;
        if (campaign == null || mainParty == null)
            return false;

        // Do not change the faction graph while any campaign menu, settlement,
        // encounter, siege or map event is active. The previous v0.6.0 gate did not
        // reject GameMenu contexts, which allowed a commit near siege-strategy menus.
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

    private bool TryQueueNewHouse(Kingdom kingdom, double now)
    {
        var ruler = kingdom?.Leader;
        if (kingdom == null || ruler == null || ruler == Hero.MainHero)
            return false;
        if (ruler.Gold < NewHouseMinimumRulerGold)
            return false;
        if (_kingdomCooldownUntilDays.TryGetValue(kingdom.StringId, out var cooldownUntil) && now < cooldownUntil)
            return false;

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
            return false;

        var founder = FindBestFounder(kingdom, ruler);
        if (founder == null)
            return false;

        _pendingKingdomId = kingdom.StringId;
        _pendingFounderId = founder.StringId;
        _pendingHomeSettlementId = homeSettlement.StringId;
        _pendingReadyAfterDay = now + PendingDelayDays;

        LogStage("QUEUE", $"kingdom={kingdom.StringId}; founder={founder.StringId}; home={homeSettlement.StringId}; readyAfter={_pendingReadyAfterDay:0.000}");
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

        var canBeElevated = hero.IsLord || hero.CompanionOf == sourceClan || TorFamilySafety.IsAiCompanion(hero);
        if (!canBeElevated)
            return false;

        // Avoid moving a family graph in the same transaction. Existing family systems
        // can still grow normally; this path is for unattached founders only.
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

        if (!hero.IsLord && (hero.CompanionOf != null || TorFamilySafety.IsAiCompanion(hero)))
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
        var wasCompanion = founder.CompanionOf == sourceClan;

        // CRASH BOUNDARY A. No manual founder.Clan/newClan.Kingdom/newClan.SetLeader
        // writes are allowed here. Bannerlord owns the complete hero->clan transaction.
        LogStage("NATIVE_FACTORY_BEGIN", $"kingdom={kingdom.StringId}; sourceClan={sourceClanId}; founder={founder.StringId}; wasLord={wasLord}; wasCompanion={wasCompanion}; home={homeSettlement.StringId}");
        newClan = Clan.CreateCompanionToLordClan(founder, homeSettlement, clanName, -1);
        if (newClan == null)
        {
            LogStage("NATIVE_FACTORY_NULL", $"founder={founder.StringId}; sourceClan={sourceClanId}");
            return false;
        }

        LogStage("NATIVE_FACTORY_OK", $"clan={newClan.StringId}; founderClan={founder.Clan?.StringId ?? "null"}; currentKingdom={newClan.Kingdom?.StringId ?? "null"}");

        // CRASH BOUNDARY B. Join through Bannerlord's faction action so kingdom caches,
        // diplomacy state and event dispatch stay engine-owned.
        if (newClan.Kingdom != kingdom)
        {
            LogStage("JOIN_KINGDOM_BEGIN", $"clan={newClan.StringId}; target={kingdom.StringId}");
            ChangeKingdomAction.ApplyByJoinToKingdom(newClan, kingdom, CampaignTime.DaysFromNow(365f), true);
            LogStage("JOIN_KINGDOM_OK", $"clan={newClan.StringId}; currentKingdom={newClan.Kingdom?.StringId ?? "null"}");
        }

        if (newClan.Kingdom != kingdom || newClan.Leader != founder || founder.Clan != newClan || !founder.IsLord || !newClan.IsNoble)
        {
            LogStage("POSTCONDITION_FAILED", $"clan={newClan.StringId}; kingdom={newClan.Kingdom?.StringId ?? "null"}; leader={newClan.Leader?.StringId ?? "null"}; founderClan={founder.Clan?.StringId ?? "null"}; isLord={founder.IsLord}; isNoble={newClan.IsNoble}");
            return false;
        }

        // Keep the house landless in this transaction. Fiefs remain the responsibility
        // of ordinary Bannerlord/TOR kingdom decisions.
        if (ruler.Gold >= NewHouseSeedGold)
        {
            LogStage("SEED_GOLD_BEGIN", $"ruler={ruler.StringId}; founder={founder.StringId}; amount={NewHouseSeedGold}");
            GiveGoldAction.ApplyBetweenCharacters(ruler, founder, NewHouseSeedGold, false);
            LogStage("SEED_GOLD_OK", $"clan={newClan.StringId}");
        }

        return true;
    }

    public IEnumerable<string> DescribeStatus()
    {
        var pending = HasPendingHouse()
            ? $"pending={_pendingKingdomId}/{_pendingFounderId}/{_pendingHomeSettlementId}, readyIn={Math.Max(0d, _pendingReadyAfterDay - CampaignTime.Now.ToDays):0.0}d"
            : "pending=none";

        yield return $"Realm-house growth: {pending}; commitBusy={_commitInProgress}; globalCooldown={Math.Max(0d, _globalCooldownUntilDay - CampaignTime.Now.ToDays):0.0}d; perKingdomCooldown={PerKingdomCooldownDays}d; minimumDeficit={MinimumClanDeficitForNewHouse}; founders=lords+eligible companions; commit=hourly-open-map-native-factory; lastCreated={_lastCreatedClanId ?? "none"}.";
    }

    private bool HasPendingHouse()
        => !string.IsNullOrWhiteSpace(_pendingKingdomId) &&
           !string.IsNullOrWhiteSpace(_pendingFounderId) &&
           !string.IsNullOrWhiteSpace(_pendingHomeSettlementId);

    private void ClearPending()
    {
        _pendingKingdomId = null;
        _pendingFounderId = null;
        _pendingHomeSettlementId = null;
        _pendingReadyAfterDay = 0d;
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
            // Diagnostics must never be allowed to affect campaign simulation.
        }
    }

    private sealed class KingdomNeed
    {
        public KingdomNeed(Kingdom kingdom, int deficit)
        {
            Kingdom = kingdom;
            Deficit = deficit;
        }

        public Kingdom Kingdom { get; }
        public int Deficit { get; }
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
