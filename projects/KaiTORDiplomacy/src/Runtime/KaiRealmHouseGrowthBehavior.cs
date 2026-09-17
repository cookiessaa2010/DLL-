using System;
using System.Collections.Generic;
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
/// No hero is generated. An existing unattached adult lord or eligible clan companion is
/// elevated and becomes the founder of a new landless noble house. This lets Dawi,
/// greenskins and other realms expand politically even when biological family growth is
/// unavailable or intentionally disabled.
/// </summary>
public sealed class KaiRealmHouseGrowthBehavior : CampaignBehaviorBase
{
    private const int MaximumTargetNobleClans = 12;
    private const int MinimumClanDeficitForNewHouse = 1;
    private const int PerKingdomCooldownDays = 42;
    private const int GlobalCooldownDays = 14;
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
        if (Campaign.Current == null || HasPendingHouse())
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
        if (Campaign.Current == null || !HasPendingHouse())
            return;
        if (CampaignTime.Now.ToDays < _pendingReadyAfterDay)
            return;
        if (CampaignTime.Now.GetHourOfDay != CommitHour)
            return;
        if (!IsSafeWorldMutationWindow())
            return;

        var kingdom = Kingdom.All.FirstOrDefault(k => k != null && string.Equals(k.StringId, _pendingKingdomId, StringComparison.Ordinal));
        var founder = Hero.AllAliveHeroes.FirstOrDefault(h => h != null && string.Equals(h.StringId, _pendingFounderId, StringComparison.Ordinal));
        var homeSettlement = Settlement.All.FirstOrDefault(s => s != null && string.Equals(s.StringId, _pendingHomeSettlementId, StringComparison.Ordinal));

        ClearPending();

        if (!CanCommitPendingHouse(kingdom, founder, homeSettlement))
            return;
        if (!TryCreateNewHouseNativeOrder(kingdom, founder, homeSettlement, out var newClan))
            return;

        _lastCreatedClanId = newClan.StringId;
        var now = CampaignTime.Now.ToDays;
        _kingdomCooldownUntilDays[kingdom.StringId] = now + PerKingdomCooldownDays;
        _globalCooldownUntilDay = now + GlobalCooldownDays;
    }

    private static bool IsSafeWorldMutationWindow()
    {
        var mainParty = MobileParty.MainParty;
        if (mainParty == null)
            return false;

        // Do not change the faction graph while a settlement/menu/encounter/battle is active.
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

    private static bool TryCreateNewHouseNativeOrder(Kingdom kingdom, Hero founder, Settlement homeSettlement, out Clan newClan)
    {
        newClan = null;

        var ruler = kingdom?.Leader;
        var sourceClan = founder?.Clan;
        if (kingdom == null || ruler == null || sourceClan == null || sourceClan == Clan.PlayerClan)
            return false;
        if (sourceClan.Kingdom != kingdom || homeSettlement?.MapFaction != kingdom)
            return false;

        var culture = founder.Culture ?? sourceClan.Culture ?? kingdom.Culture;
        var dayStamp = Math.Abs((int)CampaignTime.Now.ToDays);
        var clanId = $"kaitor_house_{kingdom.StringId}_{founder.StringId}_{dayStamp}";
        if (Clan.FindFirst(clan => string.Equals(clan.StringId, clanId, StringComparison.Ordinal)) != null)
            return false;

        // Mirror Bannerlord's companion-to-lord order: first clear companion state,
        // then promote occupation, then construct and fully initialize the clan.
        if (founder.CompanionOf != null)
            RemoveCompanionAction.ApplyByByTurningToLord(founder.CompanionOf, founder);

        if (!founder.IsLord)
            founder.SetNewOccupation(Occupation.Lord);
        if (!founder.IsLord)
            return false;

        newClan = Clan.CreateClan(clanId);
        var clanName = NameGenerator.Current.GenerateClanName(culture, homeSettlement) ?? founder.Name;
        newClan.ChangeClanName(clanName, clanName);
        newClan.Culture = culture;
        newClan.Banner = Banner.CreateRandomClanBanner(-1);

        // Bannerlord's own CreateCompanionToLordClan assigns Kingdom before moving the
        // founder and before firing OnClanCreated. Use the public Kingdom setter so its
        // internal kingdom/clan caches are updated in the engine's normal path.
        newClan.Kingdom = kingdom;
        newClan.SetInitialHomeSettlement(homeSettlement);
        newClan.IsNoble = true;

        founder.Clan = newClan;
        newClan.SetLeader(founder);

        var startingTier = Campaign.Current.Models.ClanTierModel.CompanionToLordClanStartingTier;
        var startingRenown = Campaign.Current.Models.ClanTierModel.GetRequiredRenownForTier(startingTier);
        newClan.AddRenown(startingRenown, false);

        // Keep the new house landless in this transaction. Fief assignment is left to
        // ordinary Bannerlord/TOR kingdom decisions, avoiding clan+hero+settlement graph
        // mutation in the same frame.
        if (ruler.Gold >= NewHouseSeedGold)
            GiveGoldAction.ApplyBetweenCharacters(ruler, founder, NewHouseSeedGold, false);

        CampaignEventDispatcher.Instance.OnClanCreated(newClan, true);

        return newClan.Kingdom == kingdom &&
               newClan.Leader == founder &&
               founder.Clan == newClan &&
               founder.IsLord &&
               newClan.IsNoble;
    }

    public IEnumerable<string> DescribeStatus()
    {
        var pending = HasPendingHouse()
            ? $"pending={_pendingKingdomId}/{_pendingFounderId}/{_pendingHomeSettlementId}, readyIn={Math.Max(0d, _pendingReadyAfterDay - CampaignTime.Now.ToDays):0.0}d"
            : "pending=none";

        yield return $"Realm-house growth: {pending}; globalCooldown={Math.Max(0d, _globalCooldownUntilDay - CampaignTime.Now.ToDays):0.0}d; perKingdomCooldown={PerKingdomCooldownDays}d; minimumDeficit={MinimumClanDeficitForNewHouse}; founders=lords+eligible companions; commit=hourly-open-map; lastCreated={_lastCreatedClanId ?? "none"}.";
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
