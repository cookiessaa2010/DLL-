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
/// Conservative cadet-house path for existing TOR saves.
/// WeeklyTick only selects a candidate and stores primitive IDs. The actual clan graph
/// mutation is deferred until the player has safely entered a fortification, so it does
/// not run inside TOR's daily/weekly hero-generation burst on the campaign map.
/// No new hero is generated: the founder must already be an adult lord or an existing
/// clan companion who can be elevated through Bannerlord's native companion-to-lord path.
/// This deliberately lets cultures without an active family lifecycle (for example Dawi
/// during SAFE-OFF and greenskins) still grow politically through new noble houses.
/// </summary>
public sealed class KaiCadetHouseSafeBehavior : CampaignBehaviorBase
{
    private const int MaximumTargetNobleClans = 12;
    private const int MinimumClanDeficitForCadetHouse = 2;
    private const int PerKingdomCooldownDays = 84;
    private const int GlobalCooldownDays = 42;
    private const int PendingDelayDays = 1;
    private const int NewHouseMinimumRulerGold = 50000;
    private const int NewHouseSeedGold = 15000;
    private const string TorSpecialSettlementId = "castle_BK1";

    private const string KingdomCooldownSaveKey = "kaitor_cadet_safe_kingdom_cooldown_v1";
    private const string GlobalCooldownSaveKey = "kaitor_cadet_safe_global_cooldown_v1";
    private const string PendingKingdomSaveKey = "kaitor_cadet_safe_pending_kingdom_v1";
    private const string PendingFounderSaveKey = "kaitor_cadet_safe_pending_founder_v1";
    private const string PendingFiefSaveKey = "kaitor_cadet_safe_pending_fief_v1";
    private const string PendingReadySaveKey = "kaitor_cadet_safe_pending_ready_v1";

    private Dictionary<string, double> _kingdomCooldownUntilDays = new();
    private double _globalCooldownUntilDay;
    private string _pendingKingdomId;
    private string _pendingFounderId;
    private string _pendingFiefId;
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
        dataStore.SyncData(PendingFiefSaveKey, ref _pendingFiefId);
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
            .Where(x => x.Deficit >= MinimumClanDeficitForCadetHouse)
            .OrderByDescending(x => x.Deficit)
            .ThenByDescending(x => x.Kingdom.Settlements.Count(s => s != null && s.IsFortification))
            .ThenBy(x => x.Kingdom.StringId, StringComparer.Ordinal)
            .ToArray();

        foreach (var need in candidates)
        {
            if (TryQueueCadetHouse(need.Kingdom, now))
                break;
        }
    }

    private bool TryQueueCadetHouse(Kingdom kingdom, double now)
    {
        var rulingClan = kingdom?.RulingClan;
        var ruler = kingdom?.Leader;
        if (rulingClan == null || ruler == null || rulingClan.Leader != ruler || ruler == Hero.MainHero)
            return false;
        if (ruler.Gold < NewHouseMinimumRulerGold)
            return false;
        if (_kingdomCooldownUntilDays.TryGetValue(kingdom.StringId, out var cooldownUntil) && now < cooldownUntil)
            return false;

        var spareFiefs = rulingClan.Settlements
            .Where(settlement =>
                settlement != null &&
                settlement.IsFortification &&
                !settlement.IsUnderSiege &&
                !string.Equals(settlement.StringId, TorSpecialSettlementId, StringComparison.Ordinal))
            .OrderBy(settlement => settlement.IsCastle ? 0 : 1)
            .ThenBy(settlement => settlement.StringId, StringComparer.Ordinal)
            .ToArray();

        // The ruling house always keeps at least one fortification after the split.
        if (spareFiefs.Length < 2)
            return false;

        var founder = rulingClan.Heroes
            .Where(hero => IsEligibleFounder(hero, rulingClan, ruler))
            .Select(hero => new HeroCandidate(hero, ScoreFounder(hero, ruler)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Hero.StringId, StringComparer.Ordinal)
            .Select(x => x.Hero)
            .FirstOrDefault();
        if (founder == null)
            return false;

        _pendingKingdomId = kingdom.StringId;
        _pendingFounderId = founder.StringId;
        _pendingFiefId = spareFiefs[0].StringId;
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
        var founder = Clan.All
            .Where(c => c != null)
            .SelectMany(c => c.Heroes)
            .FirstOrDefault(h => h != null && string.Equals(h.StringId, _pendingFounderId, StringComparison.Ordinal));
        var fief = Settlement.All.FirstOrDefault(s => s != null && string.Equals(s.StringId, _pendingFiefId, StringComparison.Ordinal));

        ClearPending();

        if (!CanCommitPendingHouse(kingdom, founder, fief))
            return;

        if (!TryCreateCadetClan(kingdom, founder, fief))
            return;

        var now = CampaignTime.Now.ToDays;
        _kingdomCooldownUntilDays[kingdom.StringId] = now + PerKingdomCooldownDays;
        _globalCooldownUntilDay = now + GlobalCooldownDays;
    }

    private static bool CanCommitPendingHouse(Kingdom kingdom, Hero founder, Settlement fief)
    {
        if (kingdom == null || kingdom.IsEliminated || kingdom.Leader == null || kingdom.Leader == Hero.MainHero)
            return false;
        if (GetTargetNobleClanCount(kingdom) - GetCurrentNobleClanCount(kingdom) < MinimumClanDeficitForCadetHouse)
            return false;

        var rulingClan = kingdom.RulingClan;
        var ruler = kingdom.Leader;
        if (rulingClan == null || rulingClan.Leader != ruler || ruler.Gold < NewHouseMinimumRulerGold)
            return false;
        if (founder == null || founder.Clan != rulingClan || !IsEligibleFounder(founder, rulingClan, ruler))
            return false;
        if (fief == null || !fief.IsFortification || fief.IsUnderSiege || fief.OwnerClan != rulingClan)
            return false;
        if (string.Equals(fief.StringId, TorSpecialSettlementId, StringComparison.Ordinal))
            return false;

        var remainingFortifications = rulingClan.Settlements.Count(s => s != null && s.IsFortification && s != fief);
        return remainingFortifications >= 1;
    }

    private static bool IsEligibleFounder(Hero hero, Clan rulingClan, Hero ruler)
    {
        if (hero == null || hero == ruler || hero == ruler.Spouse)
            return false;
        if (!hero.IsAlive || !hero.IsActive || hero.IsTemplate || hero.IsMinorFactionHero)
            return false;
        if (hero.Clan != rulingClan)
            return false;
        if (hero.Age < Campaign.Current.Models.AgeModel.HeroComesOfAge)
            return false;

        // A founder may already be a lord, or may be an existing clan companion that
        // Bannerlord can elevate to lord status. This is important for TOR cultures
        // whose family lifecycle is intentionally disabled during LoadSafe testing.
        var canBeElevated = hero.IsLord || hero.CompanionOf == rulingClan || TorFamilySafety.IsAiCompanion(hero);
        if (!canBeElevated)
            return false;

        // Splitting a married parent would require moving an entire family graph at once.
        // LoadSafe deliberately restricts founders to unattached adult members.
        if (hero.Spouse != null || hero.Children.Count > 0)
            return false;
        if (hero.PartyBelongedToAsPrisoner != null)
            return false;

        // Do not move a party leader, army member, governor or active combat participant.
        // Requiring no owned party makes the clan reassignment much less invasive.
        if (hero.PartyBelongedTo != null)
            return false;
        if (hero.GovernorOf != null)
            return false;

        return true;
    }

    private static int ScoreFounder(Hero hero, Hero ruler)
    {
        var score = 0;
        if (hero.Father == ruler || hero.Mother == ruler)
            score += 120;
        else if ((ruler.Father != null && hero.Father == ruler.Father) ||
                 (ruler.Mother != null && hero.Mother == ruler.Mother))
            score += 90;
        else
            score += 40;

        if (!hero.IsLord && (hero.CompanionOf != null || TorFamilySafety.IsAiCompanion(hero)))
            score += 35;

        score += ruler.GetRelation(hero);
        score += Math.Min(30, hero.Level / 2);
        return score;
    }

    private static bool TryCreateCadetClan(Kingdom kingdom, Hero founder, Settlement fief)
    {
        var rulingClan = kingdom.RulingClan;
        var ruler = kingdom.Leader;
        if (rulingClan == null || ruler == null || founder?.Clan != rulingClan || fief?.OwnerClan != rulingClan)
            return false;

        var culture = founder.Culture ?? kingdom.Culture;
        var dayStamp = Math.Abs((int)CampaignTime.Now.ToDays);
        var clanId = $"kaitor_cadet_{kingdom.StringId}_{founder.StringId}_{dayStamp}";
        if (Clan.FindFirst(clan => string.Equals(clan.StringId, clanId, StringComparison.Ordinal)) != null)
            return false;

        // Follow Bannerlord's own clan-creation ordering: create and initialize the clan,
        // elevate an existing companion through the native companion-to-lord path when
        // needed, attach the founder, join the kingdom, transfer the fief, then dispatch
        // OnClanCreated only after the object graph has reached a coherent state.
        var newClan = Clan.CreateClan(clanId);
        var clanName = NameGenerator.Current.GenerateClanName(culture, fief) ?? founder.Name;
        newClan.ChangeClanName(clanName, clanName);
        newClan.Culture = culture;
        newClan.Banner = Banner.CreateRandomClanBanner(-1);
        newClan.SetInitialHomeSettlement(fief);
        newClan.IsNoble = true;

        if (founder.CompanionOf != null)
            RemoveCompanionAction.ApplyByByTurningToLord(founder.CompanionOf, founder);

        if (!founder.IsLord)
            founder.SetNewOccupation(Occupation.Lord);

        founder.Clan = newClan;
        newClan.SetLeader(founder);

        var startingTier = Campaign.Current.Models.ClanTierModel.CompanionToLordClanStartingTier;
        var startingRenown = Campaign.Current.Models.ClanTierModel.GetRequiredRenownForTier(startingTier);
        newClan.AddRenown(startingRenown, false);

        ChangeKingdomAction.ApplyByJoinToKingdom(newClan, kingdom, CampaignTime.DaysFromNow(365f), true);
        ChangeOwnerOfSettlementAction.ApplyByGift(fief, founder);

        if (ruler.Gold >= NewHouseSeedGold)
            GiveGoldAction.ApplyBetweenCharacters(ruler, founder, NewHouseSeedGold, false);

        CampaignEventDispatcher.Instance.OnClanCreated(newClan, true);

        return newClan.Kingdom == kingdom && founder.Clan == newClan && founder.IsLord && fief.OwnerClan == newClan;
    }

    public IEnumerable<string> DescribeStatus()
    {
        var pending = HasPendingHouse()
            ? $"pending={_pendingKingdomId}/{_pendingFounderId}/{_pendingFiefId}, readyIn={Math.Max(0d, _pendingReadyAfterDay - CampaignTime.Now.ToDays):0.0}d"
            : "pending=none";
        yield return $"Cadet-house safe queue: {pending}; globalCooldown={Math.Max(0d, _globalCooldownUntilDay - CampaignTime.Now.ToDays):0.0}d; perKingdomCooldown={PerKingdomCooldownDays}d; minimumDeficit={MinimumClanDeficitForCadetHouse}; founders=lords+eligible companions.";
    }

    private bool HasPendingHouse()
        => !string.IsNullOrWhiteSpace(_pendingKingdomId) &&
           !string.IsNullOrWhiteSpace(_pendingFounderId) &&
           !string.IsNullOrWhiteSpace(_pendingFiefId);

    private void ClearPending()
    {
        _pendingKingdomId = null;
        _pendingFounderId = null;
        _pendingFiefId = null;
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
