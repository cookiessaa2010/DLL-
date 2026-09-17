using System;
using System.Collections.Generic;
using System.Linq;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Staged new-house path for TOR saves. WeeklyTick only selects a candidate and stores IDs.
/// Runtime mutation is split across separate safe hourly steps while the player is stationary
/// on the campaign map, away from settlements, battles and the daily midnight update window.
/// No new hero is generated: an existing unattached lord/eligible clan companion becomes the
/// founder. New houses start landless and later receive fiefs through normal kingdom systems.
/// </summary>
public sealed class KaiCadetHouseSafeBehavior : CampaignBehaviorBase
{
    private const int MaximumTargetNobleClans = 12;
    private const int MinimumClanDeficitForNewHouse = 2;
    private const int PerKingdomCooldownDays = 84;
    private const int GlobalCooldownDays = 42;
    private const int PendingDelayDays = 2;
    private const int PhaseDelayHours = 6;
    private const int NewHouseMinimumRulerGold = 50000;
    private const int NewHouseSeedGold = 10000;
    private const string TorSpecialSettlementId = "castle_BK1";

    private const string KingdomCooldownSaveKey = "kaitor_cadet_safe_kingdom_cooldown_v1";
    private const string GlobalCooldownSaveKey = "kaitor_cadet_safe_global_cooldown_v1";
    private const string PendingKingdomSaveKey = "kaitor_cadet_safe_pending_kingdom_v1";
    private const string PendingFounderSaveKey = "kaitor_cadet_safe_pending_founder_v1";
    private const string PendingHomeSaveKey = "kaitor_cadet_safe_pending_fief_v1";
    private const string PendingReadySaveKey = "kaitor_cadet_safe_pending_ready_v1";
    private const string PendingPhaseSaveKey = "kaitor_cadet_safe_pending_phase_v2";
    private const string PendingNextHourSaveKey = "kaitor_cadet_safe_pending_next_hour_v2";

    private Dictionary<string, double> _kingdomCooldownUntilDays = new();
    private double _globalCooldownUntilDay;
    private string _pendingKingdomId;
    private string _pendingFounderId;
    private string _pendingHomeSettlementId;
    private double _pendingReadyAfterDay;
    private int _pendingPhase;
    private double _pendingNextHour;

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
        dataStore.SyncData(PendingPhaseSaveKey, ref _pendingPhase);
        dataStore.SyncData(PendingNextHourSaveKey, ref _pendingNextHour);
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
        _pendingPhase = 0;
        _pendingNextHour = CampaignTime.Now.ToHours + PhaseDelayHours;
        return true;
    }

    private void OnHourlyTick()
    {
        if (Campaign.Current == null || !HasPendingHouse())
            return;
        if (CampaignTime.Now.ToDays < _pendingReadyAfterDay || CampaignTime.Now.ToHours < _pendingNextHour)
            return;
        if (!IsSafeMutationWindow())
            return;

        var kingdom = Kingdom.All.FirstOrDefault(k => k != null && string.Equals(k.StringId, _pendingKingdomId, StringComparison.Ordinal));
        var founder = Hero.AllAliveHeroes.FirstOrDefault(h => h != null && string.Equals(h.StringId, _pendingFounderId, StringComparison.Ordinal));
        var homeSettlement = Settlement.All.FirstOrDefault(s => s != null && string.Equals(s.StringId, _pendingHomeSettlementId, StringComparison.Ordinal));

        if (!CanCommitPendingHouse(kingdom, founder, homeSettlement))
        {
            ClearPending();
            return;
        }

        // Phase 0 only elevates a companion. No clan graph is changed in the same tick.
        if (_pendingPhase == 0)
        {
            if (!PrepareFounder(founder))
            {
                ClearPending();
                return;
            }

            _pendingPhase = 1;
            _pendingNextHour = CampaignTime.Now.ToHours + PhaseDelayHours;
            return;
        }

        // Phase 1 creates and initializes the clan in Bannerlord's native ordering.
        if (_pendingPhase == 1)
        {
            if (!TryCreateNewHouse(kingdom, founder, homeSettlement))
            {
                ClearPending();
                return;
            }

            var now = CampaignTime.Now.ToDays;
            _kingdomCooldownUntilDays[kingdom.StringId] = now + PerKingdomCooldownDays;
            _globalCooldownUntilDay = now + GlobalCooldownDays;
            ClearPending();
        }
    }

    private static bool IsSafeMutationWindow()
    {
        var mainParty = MobileParty.MainParty;
        if (mainParty == null)
            return false;
        if (mainParty.CurrentSettlement != null || mainParty.MapEvent != null || mainParty.BesiegedSettlement != null)
            return false;
        if (mainParty.IsMoving)
            return false;

        // Avoid TOR/Bannerlord's daily world-update burst around midnight.
        var hour = CampaignTime.Now.GetHourOfDay;
        return hour >= 6 && hour <= 20;
    }

    private static Hero FindBestFounder(Kingdom kingdom, Hero ruler)
    {
        return kingdom.Clans
            .Where(clan => IsEligibleSourceClan(clan, kingdom))
            .SelectMany(clan => clan.Heroes
                .Where(hero => hero != null)
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
        if (clan.Leader == null || !clan.Leader.IsAlive)
            return false;

        // Never hollow out a source house to make another one.
        return clan.Heroes.Count(hero => hero != null && hero.IsAlive && hero.IsActive) >= 3;
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

    private static bool PrepareFounder(Hero founder)
    {
        if (founder == null)
            return false;

        if (founder.CompanionOf != null)
            RemoveCompanionAction.ApplyByByTurningToLord(founder.CompanionOf, founder);

        if (!founder.IsLord)
            founder.SetNewOccupation(Occupation.Lord);

        return founder.IsLord && founder.CompanionOf == null;
    }

    private static bool TryCreateNewHouse(Kingdom kingdom, Hero founder, Settlement homeSettlement)
    {
        var ruler = kingdom?.Leader;
        var sourceClan = founder?.Clan;
        if (kingdom == null || ruler == null || sourceClan == null || sourceClan == Clan.PlayerClan)
            return false;
        if (sourceClan.Kingdom != kingdom || homeSettlement?.MapFaction != kingdom || !founder.IsLord)
            return false;

        var culture = founder.Culture ?? sourceClan.Culture ?? kingdom.Culture;
        var dayStamp = Math.Abs((int)CampaignTime.Now.ToDays);
        var clanId = $"kaitor_house_{kingdom.StringId}_{founder.StringId}_{dayStamp}";
        if (Clan.FindFirst(clan => string.Equals(clan.StringId, clanId, StringComparison.Ordinal)) != null)
            return false;

        var clanName = NameGenerator.Current.GenerateClanName(culture, homeSettlement) ?? founder.Name;
        var newClan = Clan.CreateClan(clanId);

        // Match Bannerlord's companion-to-lord initialization order as closely as possible.
        newClan.ChangeClanName(clanName, clanName);
        newClan.Culture = culture;
        newClan.Banner = Banner.CreateRandomClanBanner(-1);
        newClan.Kingdom = kingdom;
        newClan.SetInitialHomeSettlement(homeSettlement);
        founder.Clan = newClan;
        newClan.SetLeader(founder);
        newClan.IsNoble = true;

        var startingTier = Campaign.Current.Models.ClanTierModel.CompanionToLordClanStartingTier;
        var startingRenown = Campaign.Current.Models.ClanTierModel.GetRequiredRenownForTier(startingTier);
        newClan.AddRenown(startingRenown, false);

        if (ruler.Gold >= NewHouseSeedGold)
            GiveGoldAction.ApplyBetweenCharacters(ruler, founder, NewHouseSeedGold, false);

        CampaignEventDispatcher.Instance.OnClanCreated(newClan, true);

        return newClan.Kingdom == kingdom && founder.Clan == newClan && newClan.Leader == founder && founder.IsLord;
    }

    public IEnumerable<string> DescribeStatus()
    {
        var pending = HasPendingHouse()
            ? $"pending={_pendingKingdomId}/{_pendingFounderId}/{_pendingHomeSettlementId}, phase={_pendingPhase}, readyIn={Math.Max(0d, _pendingReadyAfterDay - CampaignTime.Now.ToDays):0.0}d"
            : "pending=none";
        yield return $"New-house staged queue: {pending}; globalCooldown={Math.Max(0d, _globalCooldownUntilDay - CampaignTime.Now.ToDays):0.0}d; perKingdomCooldown={PerKingdomCooldownDays}d; minimumDeficit={MinimumClanDeficitForNewHouse}; founders=lords+eligible companions; newHouses=landless.";
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
        _pendingPhase = 0;
        _pendingNextHour = 0d;
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
