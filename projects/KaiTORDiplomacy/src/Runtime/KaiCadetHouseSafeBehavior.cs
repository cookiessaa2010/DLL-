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
/// Conservative new-house path for existing TOR saves.
/// WeeklyTick only selects an existing adult hero and stores primitive IDs. The actual
/// clan-graph mutation is deferred until the player safely enters a fortification, so it
/// does not run inside TOR's daily/weekly hero-generation burst on the campaign map.
/// No new hero is generated. Existing lords and eligible clan companions can found a
/// new noble house; this lets Dawi and greenskins grow politically even while ordinary
/// family reproduction is unavailable.
/// </summary>
public sealed class KaiCadetHouseSafeBehavior : CampaignBehaviorBase
{
    private const int MaximumTargetNobleClans = 12;
    private const int MinimumClanDeficitForNewHouse = 1;
    private const int PerKingdomCooldownDays = 63;
    private const int GlobalCooldownDays = 21;
    private const int PendingDelayDays = 1;
    private const int NewHouseMinimumRulerGold = 30000;
    private const int NewHouseSeedGold = 10000;
    private const string TorSpecialSettlementId = "castle_BK1";

    // Keep the existing keys so old v0.4.x saves remain readable.
    private const string KingdomCooldownSaveKey = "kaitor_cadet_safe_kingdom_cooldown_v1";
    private const string GlobalCooldownSaveKey = "kaitor_cadet_safe_global_cooldown_v1";
    private const string PendingKingdomSaveKey = "kaitor_cadet_safe_pending_kingdom_v1";
    private const string PendingFounderSaveKey = "kaitor_cadet_safe_pending_founder_v1";
    private const string PendingHomeSaveKey = "kaitor_cadet_safe_pending_fief_v1";
    private const string PendingReadySaveKey = "kaitor_cadet_safe_pending_ready_v1";

    private Dictionary<string, double> _kingdomCooldownUntilDays = new();
    private double _globalCooldownUntilDay;
    private string _pendingKingdomId;
    private string _pendingFounderId;
    private string _pendingHomeSettlementId;
    private double _pendingReadyAfterDay;

    public override void RegisterEvents()
    {
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
        CampaignEvents.AfterSettlementEntered.AddNonSerializedListener(this, OnAfterSettlementEntered);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(KingdomCooldownSaveKey, ref _kingdomCooldownUntilDays);
        dataStore.SyncData(GlobalCooldownSaveKey, ref _globalCooldownUntilDay);
        dataStore.SyncData(PendingKingdomSaveKey, ref _pendingKingdomId);
        dataStore.SyncData(PendingFounderSaveKey, ref _pendingFounderId);
        dataStore.SyncData(PendingHomeSaveKey, ref _pendingHomeSettlementId);
        dataStore.SyncData(PendingReadySaveKey, ref _pendingReadyAfterDay);
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
        return true;
    }

    private void OnAfterSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
    {
        if (Campaign.Current == null || party != MobileParty.MainParty || settlement == null || !settlement.IsFortification)
            return;
        if (!HasPendingHouse() || CampaignTime.Now.ToDays < _pendingReadyAfterDay)
            return;

        var kingdom = Kingdom.All.FirstOrDefault(k => k != null && string.Equals(k.StringId, _pendingKingdomId, StringComparison.Ordinal));
        var founder = Hero.AllAliveHeroes.FirstOrDefault(h => h != null && string.Equals(h.StringId, _pendingFounderId, StringComparison.Ordinal));
        var homeSettlement = Settlement.All.FirstOrDefault(s => s != null && string.Equals(s.StringId, _pendingHomeSettlementId, StringComparison.Ordinal));

        ClearPending();

        if (!CanCommitPendingHouse(kingdom, founder, homeSettlement))
            return;

        if (!TryCreateNewHouse(kingdom, founder, homeSettlement))
            return;

        var now = CampaignTime.Now.ToDays;
        _kingdomCooldownUntilDays[kingdom.StringId] = now + PerKingdomCooldownDays;
        _globalCooldownUntilDay = now + GlobalCooldownDays;
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

        // The founder may already be a lord or may be an existing clan companion that
        // Bannerlord can elevate through its native companion-to-lord action.
        var canBeElevated = hero.IsLord || hero.CompanionOf == sourceClan || TorFamilySafety.IsAiCompanion(hero);
        if (!canBeElevated)
            return false;

        // Moving a spouse/parent requires moving an entire family graph at once. Keep
        // this LoadSafe path to unattached adults only.
        if (hero.Spouse != null || hero.Children.Count > 0)
            return false;
        if (hero.PartyBelongedToAsPrisoner != null || hero.IsPrisoner)
            return false;

        // Do not split an active party, army, battle participant or governorship.
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

    private static bool TryCreateNewHouse(Kingdom kingdom, Hero founder, Settlement homeSettlement)
    {
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

        // Elevate a companion before changing clan ownership. Bannerlord exposes a
        // dedicated ByTurningToLord path which clears companion state without firing
        // the normal dismissal/fugitive logic.
        if (founder.CompanionOf != null)
            RemoveCompanionAction.ApplyByByTurningToLord(founder.CompanionOf, founder);

        if (!founder.IsLord)
            founder.SetNewOccupation(Occupation.Lord);
        if (!founder.IsLord)
            return false;

        var newClan = Clan.CreateClan(clanId);
        var clanName = NameGenerator.Current.GenerateClanName(culture, homeSettlement) ?? founder.Name;
        newClan.ChangeClanName(clanName, clanName);
        newClan.Culture = culture;
        newClan.Banner = Banner.CreateRandomClanBanner(-1);
        newClan.SetInitialHomeSettlement(homeSettlement);
        newClan.IsNoble = true;

        founder.Clan = newClan;
        newClan.SetLeader(founder);

        var startingTier = Campaign.Current.Models.ClanTierModel.CompanionToLordClanStartingTier;
        var startingRenown = Campaign.Current.Models.ClanTierModel.GetRequiredRenownForTier(startingTier);
        newClan.AddRenown(startingRenown, false);

        ChangeKingdomAction.ApplyByJoinToKingdom(newClan, kingdom, CampaignTime.DaysFromNow(365f), true);

        // New houses start landless. This deliberately avoids transferring a settlement
        // during the same graph mutation. Bannerlord/TOR can grant fiefs later through
        // their ordinary kingdom decision system.
        if (ruler.Gold >= NewHouseSeedGold)
            GiveGoldAction.ApplyBetweenCharacters(ruler, founder, NewHouseSeedGold, false);

        CampaignEventDispatcher.Instance.OnClanCreated(newClan, true);

        return newClan.Kingdom == kingdom && founder.Clan == newClan && founder.IsLord;
    }

    public IEnumerable<string> DescribeStatus()
    {
        var pending = HasPendingHouse()
            ? $"pending={_pendingKingdomId}/{_pendingFounderId}/{_pendingHomeSettlementId}, readyIn={Math.Max(0d, _pendingReadyAfterDay - CampaignTime.Now.ToDays):0.0}d"
            : "pending=none";
        yield return $"New-house safe queue: {pending}; globalCooldown={Math.Max(0d, _globalCooldownUntilDay - CampaignTime.Now.ToDays):0.0}d; perKingdomCooldown={PerKingdomCooldownDays}d; minimumDeficit={MinimumClanDeficitForNewHouse}; founders=lords+eligible companions; newHouses=landless.";
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
