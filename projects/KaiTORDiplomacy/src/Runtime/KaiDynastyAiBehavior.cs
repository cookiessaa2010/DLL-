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
/// Conservative AI dynasty growth for TOR kingdoms.
/// Under-populated AI kingdoms first try to recruit an existing noble clan through
/// Bannerlord's native AI barter. If no acceptable clan exists, a ruler with spare
/// land may elevate one unmarried adult relative/clan member/TOR AI companion into a
/// new cadet vassal house. At most one world-changing action is performed per week.
/// </summary>
public sealed class KaiDynastyAiBehavior : CampaignBehaviorBase
{
    private const int MaximumTargetNobleClans = 10;
    private const int NewHouseCooldownDays = 180;
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
        var campaign = Campaign.Current;
        if (campaign == null)
            return;

        var playerKingdom = Clan.PlayerClan?.Kingdom;
        var candidates = Kingdom.All
            .Where(k => k != null && !k.IsEliminated && k.Leader != null && k != playerKingdom)
            .Select(k => new KingdomNeed(k, GetTargetNobleClanCount(k) - GetCurrentNobleClanCount(k)))
            .Where(x => x.Deficit > 0)
            .OrderByDescending(x => x.Deficit)
            .ThenBy(x => x.Kingdom.StringId, StringComparer.Ordinal)
            .ToArray();

        foreach (var need in candidates)
        {
            // Prefer political recruitment. It preserves existing houses and lets the
            // native barter/value system decide whether both sides actually benefit.
            if (TryRecruitExistingClan(need.Kingdom))
                return;

            // Found a new house only when recruitment produced no acceptable result.
            if (TryFoundCadetHouse(need.Kingdom))
                return;
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

    private static bool TryRecruitExistingClan(Kingdom targetKingdom)
    {
        if (targetKingdom?.Leader == null)
            return false;

        var playerKingdom = Clan.PlayerClan?.Kingdom;
        var possibleClans = Clan.All
            .Where(clan => IsRecruitableClan(clan, targetKingdom, playerKingdom))
            .Select(clan => new ClanCandidate(clan, ScoreRecruitmentCandidate(clan, targetKingdom)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Clan.StringId, StringComparer.Ordinal)
            .Take(6)
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

            // Match Bannerlord's own defection safety concept: a king should not spend
            // most of the treasury on a single recruit.
            var neededPayment = clanValue < 0 ? -clanValue : 0;
            if (neededPayment > targetKingdom.Leader.Gold * 0.35f)
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
        if (clan.Kingdom == targetKingdom || clan.Kingdom == playerKingdom)
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

        // A struggling house is more willing to seek a stronger patron, but the native
        // barter remains the final authority on whether the move actually happens.
        if (clan.CurrentTotalStrength < targetKingdom.CurrentTotalStrength * 0.10f)
            score += 15;

        return score;
    }

    private bool TryFoundCadetHouse(Kingdom kingdom)
    {
        var rulingClan = kingdom?.RulingClan;
        var ruler = kingdom?.Leader;
        if (rulingClan == null || ruler == null || rulingClan.Leader != ruler)
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

        // Never strip the ruling house of its final fortification.
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
        newClan.Tier = Campaign.Current.Models.ClanTierModel.CompanionToLordClanStartingTier;
        newClan.SetInitialHomeSettlement(fief);
        newClan.IsNoble = true;

        if (founder.CompanionOf != null)
            RemoveCompanionAction.ApplyByByTurningToLord(founder.CompanionOf, founder);

        if (!founder.IsLord)
            founder.SetNewOccupation(Occupation.Lord);

        founder.Clan = newClan;
        newClan.SetLeader(founder);
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
