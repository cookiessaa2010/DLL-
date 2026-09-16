using System;
using System.Collections.Generic;
using System.Linq;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Conservative AI ruler clan-growth logic for TOR kingdoms.
/// LoadSafe is recruitment-only: under-populated kingdoms may recruit an existing
/// noble clan through Bannerlord's native AI barter, but runtime clan creation is
/// hard-disabled until TOR cadet-house creation is separately certified in game.
/// Player-led kingdoms are never changed automatically, while a kingdom the player
/// merely serves is managed normally by its AI ruler.
/// </summary>
public sealed class KaiDynastyAiBehavior : CampaignBehaviorBase
{
    private const int MaximumTargetNobleClans = 12;
    private const int MaximumKingdomGrowthActionsPerWeek = 1;
    private const bool EnableCadetHouseCreation = false;
    private const int NewHouseCooldownDays = 42;
    private const int NewHouseMinimumRulerGold = 30000;
    private const int NewHouseSeedGold = 15000;
    private const string NewHouseCooldownSaveKey = "kaitor_dynasty_house_cooldown_v1";
    private const string TorSpecialSettlementId = "castle_BK1";

    private Dictionary<string, double> _newHouseCooldownUntilDays = new();

    public override void RegisterEvents()
    {
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(NewHouseCooldownSaveKey, ref _newHouseCooldownUntilDays);
        _newHouseCooldownUntilDays ??= new Dictionary<string, double>();
    }

    private void OnWeeklyTick()
    {
        if (Campaign.Current == null)
            return;

        var candidates = Kingdom.All
            .Where(k => k != null && !k.IsEliminated && k.Leader != null && k.Leader != Hero.MainHero)
            .Select(k => new KingdomNeed(k, GetTargetNobleClanCount(k) - GetCurrentNobleClanCount(k)))
            .Where(x => x.Deficit > 0)
            .OrderByDescending(x => x.Deficit)
            .ThenByDescending(x => x.Kingdom.Settlements.Count(s => s != null && s.IsFortification))
            .ThenBy(x => x.Kingdom.StringId, StringComparer.Ordinal)
            .ToArray();

        var actions = 0;
        foreach (var need in candidates)
        {
            if (actions >= MaximumKingdomGrowthActionsPerWeek)
                break;

            // Safe live path: use Bannerlord's own barter/recruitment mechanism only.
            if (TryRecruitExistingClan(need.Kingdom))
            {
                actions++;
                continue;
            }

            // Runtime Clan.CreateClan / hero reassignment is deliberately disabled in
            // LoadSafe after native campaign-map access violations during weekly ticks.
            if (EnableCadetHouseCreation && TryFoundCadetHouse(need.Kingdom))
                actions++;
        }
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

    public IEnumerable<string> DescribeStatus()
    {
        foreach (var kingdom in Kingdom.All
                     .Where(k => k != null && !k.IsEliminated)
                     .OrderByDescending(k => k.Settlements.Count(s => s != null && s.IsFortification))
                     .ThenBy(k => k.StringId, StringComparer.Ordinal))
        {
            var fortifications = kingdom.Settlements.Count(s => s != null && s.IsFortification);
            var current = GetCurrentNobleClanCount(kingdom);
            var target = GetTargetNobleClanCount(kingdom);
            var deficit = Math.Max(0, target - current);
            var mode = kingdom.Leader == Hero.MainHero ? "PLAYER-RULED" : "AI-RULER";
            var cooldown = GetHouseCooldownRemainingDays(kingdom);
            var growthMode = EnableCadetHouseCreation ? "RECRUIT+CADET" : "RECRUIT-ONLY";
            yield return $"{kingdom.StringId} = {kingdom.Name}: settlements={fortifications}, clans={current}, target={target}, deficit={deficit}, ruler={kingdom.Leader?.Name}, mode={mode}, growth={growthMode}, cadetCooldown={cooldown}d";
        }
    }

    private int GetHouseCooldownRemainingDays(Kingdom kingdom)
    {
        if (kingdom == null || !_newHouseCooldownUntilDays.TryGetValue(kingdom.StringId, out var until))
            return 0;
        var remaining = until - CampaignTime.Now.ToDays;
        return remaining <= 0d ? 0 : Math.Max(1, (int)Math.Ceiling(remaining));
    }

    private static bool TryRecruitExistingClan(Kingdom targetKingdom)
    {
        if (targetKingdom?.Leader == null || targetKingdom.Leader == Hero.MainHero)
            return false;

        var playerKingdom = Clan.PlayerClan?.Kingdom;
        var possibleClans = Clan.All
            .Where(clan => IsRecruitableClan(clan, targetKingdom, playerKingdom))
            .Select(clan => new ClanCandidate(clan, ScoreRecruitmentCandidate(clan, targetKingdom)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Clan.StringId, StringComparer.Ordinal)
            .Take(8)
            .ToArray();

        foreach (var candidate in possibleClans)
        {
            var clan = candidate.Clan;
            var isDefection = clan.Kingdom != null;
            var barterable = new JoinKingdomAsClanBarterable(clan.Leader, targetKingdom, isDefection);
            var clanValue = barterable.GetValueForFaction(clan);
            var kingdomValue = barterable.GetValueForFaction(targetKingdom);
            var combinedValue = clanValue + kingdomValue;
            if (combinedValue <= 0)
                continue;

            var neededPayment = clanValue < 0 ? -clanValue : 0;
            if (neededPayment > targetKingdom.Leader.Gold * 0.45f)
                continue;

            Campaign.Current.BarterManager.ExecuteAiBarter(
                clan,
                targetKingdom,
                clan.Leader,
                targetKingdom.Leader,
                barterable);

            if (clan.Kingdom == targetKingdom)
                return true;
        }

        return false;
    }

    private static bool IsRecruitableClan(Clan clan, Kingdom targetKingdom, Kingdom playerKingdom)
    {
        if (clan == null || clan.Leader == null || clan == Clan.PlayerClan)
            return false;
        if (clan.IsEliminated || clan.IsBanditFaction || clan.IsRebelClan || clan.IsMinorFaction || clan.IsClanTypeMercenary)
            return false;
        if (clan.Kingdom == targetKingdom)
            return false;

        if (playerKingdom != null && targetKingdom != playerKingdom && clan.Kingdom == playerKingdom)
            return false;

        if (clan.Kingdom != null && clan.Kingdom.RulingClan == clan)
            return false;
        if (!clan.ShouldStayInKingdomUntil.IsPast)
            return false;
        if (clan.IsAtWarWith(targetKingdom))
            return false;
        if (Campaign.Current.Models.DiplomacyModel.IsAtConstantWar(clan, targetKingdom))
            return false;
        if (clan.WarPartyComponents.Any(component => component.MobileParty?.MapEvent != null))
            return false;
        return true;
    }

    private static int ScoreRecruitmentCandidate(Clan clan, Kingdom targetKingdom)
    {
        var score = 0;

        if (clan.Kingdom == null)
            score += 35;
        if (clan.Culture == targetKingdom.Culture)
            score += 60;
        if (clan.Settlements.Count == 0)
            score += 20;

        score += clan.Leader.GetRelation(targetKingdom.Leader);

        var currentRuler = clan.Kingdom?.Leader;
        if (currentRuler != null)
            score -= clan.Leader.GetRelation(currentRuler) / 2;

        if (clan.CurrentTotalStrength < targetKingdom.CurrentTotalStrength * 0.10f)
            score += 15;

        return score;
    }

    private bool TryFoundCadetHouse(Kingdom kingdom)
    {
        var rulingClan = kingdom?.RulingClan;
        var ruler = kingdom?.Leader;
        if (rulingClan == null || ruler == null || rulingClan.Leader != ruler || ruler == Hero.MainHero)
            return false;
        if (ruler.Gold < NewHouseMinimumRulerGold)
            return false;

        var now = CampaignTime.Now.ToDays;
        if (_newHouseCooldownUntilDays.TryGetValue(kingdom.StringId, out var cooldownUntil) && now < cooldownUntil)
            return false;

        var spareFiefs = rulingClan.Settlements
            .Where(settlement =>
                settlement != null &&
                settlement.IsFortification &&
                !string.Equals(settlement.StringId, TorSpecialSettlementId, StringComparison.Ordinal))
            .OrderBy(settlement => settlement.IsCastle ? 0 : 1)
            .ThenBy(settlement => settlement.StringId, StringComparer.Ordinal)
            .ToArray();

        if (spareFiefs.Length < 2)
            return false;

        var candidate = rulingClan.Heroes
            .Where(hero => IsEligibleHouseFounder(hero, rulingClan, ruler))
            .Select(hero => new HeroCandidate(hero, ScoreHouseFounder(hero, ruler)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Hero.StringId, StringComparer.Ordinal)
            .Select(x => x.Hero)
            .FirstOrDefault();

        if (candidate == null)
            return false;

        var fief = spareFiefs[0];
        if (!TryCreateCadetClan(kingdom, rulingClan, ruler, candidate, fief))
            return false;

        _newHouseCooldownUntilDays[kingdom.StringId] = now + NewHouseCooldownDays;
        return true;
    }

    private static bool IsEligibleHouseFounder(Hero hero, Clan rulingClan, Hero ruler)
    {
        if (hero == null || hero == ruler || hero == ruler.Spouse)
            return false;
        if (!hero.IsAlive || !hero.IsActive || hero.IsTemplate || hero.IsMinorFactionHero)
            return false;
        if (hero.Clan != rulingClan)
            return false;
        if (hero.Age < Campaign.Current.Models.AgeModel.HeroComesOfAge)
            return false;
        if (hero.Spouse != null || hero.Children.Count > 0)
            return false;
        if (hero.PartyBelongedToAsPrisoner != null)
            return false;
        if (hero.PartyBelongedTo?.MapEvent != null || hero.PartyBelongedTo?.Army != null)
            return false;

        return hero.IsLord || TorFamilySafety.IsAiCompanion(hero);
    }

    private static int ScoreHouseFounder(Hero hero, Hero ruler)
    {
        var score = 0;

        if (hero.Father == ruler || hero.Mother == ruler)
            score += 120;
        else if ((ruler.Father != null && hero.Father == ruler.Father) ||
                 (ruler.Mother != null && hero.Mother == ruler.Mother))
            score += 90;
        else if (hero.IsLord)
            score += 60;

        if (TorFamilySafety.IsAiCompanion(hero))
            score += 50;

        score += ruler.GetRelation(hero);
        score += Math.Min(30, hero.Level / 2);
        return score;
    }

    private static bool TryCreateCadetClan(
        Kingdom kingdom,
        Clan rulingClan,
        Hero ruler,
        Hero founder,
        Settlement fief)
    {
        if (kingdom == null || rulingClan == null || ruler == null || founder == null || fief == null)
            return false;

        var culture = founder.Culture ?? kingdom.Culture;
        var dayStamp = (int)CampaignTime.Now.ToDays;
        var clanId = $"kaitor_house_{kingdom.StringId}_{founder.StringId}_{dayStamp}";
        if (Clan.FindFirst(clan => string.Equals(clan.StringId, clanId, StringComparison.Ordinal)) != null)
            return false;

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

        CampaignEventDispatcher.Instance.OnClanCreated(newClan, true);

        ChangeKingdomAction.ApplyByJoinToKingdom(
            newClan,
            kingdom,
            CampaignTime.DaysFromNow(365f),
            true);

        ChangeOwnerOfSettlementAction.ApplyByGift(fief, founder);

        if (ruler.Gold >= NewHouseSeedGold)
            GiveGoldAction.ApplyBetweenCharacters(ruler, founder, NewHouseSeedGold, false);

        return newClan.Kingdom == kingdom && founder.Clan == newClan && fief.OwnerClan == newClan;
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

    private sealed class ClanCandidate
    {
        public ClanCandidate(Clan clan, int score)
        {
            Clan = clan;
            Score = score;
        }

        public Clan Clan { get; }
        public int Score { get; }
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
