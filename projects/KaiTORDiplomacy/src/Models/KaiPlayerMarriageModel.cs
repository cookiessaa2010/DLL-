using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;

namespace KaiTOR.Diplomacy.Models;

/// <summary>
/// KaiTOR owns TOR family eligibility because TOR's TORMarriageModel intentionally
/// returns false for every marriage check. Conventional marriage is restored for
/// lore-capable peoples with race-aware adulthood and no upper marriage-age cap.
/// Biological reproduction remains a separate decision owned by TorFamilySafety and
/// KaiPregnancyModel.
/// </summary>
public sealed class KaiPlayerMarriageModel : DefaultMarriageModel
{
    public const string ExpectedTorBaseType = "TOR_Core.Models.TORMarriageModel";

    private readonly MarriageModel _torBase;

    public KaiPlayerMarriageModel(MarriageModel torBase)
    {
        _torBase = torBase;
    }

    public string UnderlyingModelTypeName => _torBase?.GetType().FullName ?? "<null>";

    public override bool IsCoupleSuitableForMarriage(Hero firstHero, Hero secondHero)
    {
        if (firstHero == null || secondHero == null || firstHero == secondHero)
            return false;

        // Dawi marriage is an abstract house union. No female spouse Hero is created.
        if (KaiRaceLifecycle.IsDawi(firstHero) || KaiRaceLifecycle.IsDawi(secondHero))
            return false;

        if (!KaiRaceLifecycle.CanUseSocialMarriage(firstHero) ||
            !KaiRaceLifecycle.CanUseSocialMarriage(secondHero))
            return false;

        if (!AreCulturesCompatible(firstHero.Culture?.StringId, secondHero.Culture?.StringId))
            return false;

        if (!IsClanSuitableForMarriage(firstHero.Clan) ||
            !IsClanSuitableForMarriage(secondHero.Clan))
            return false;

        // Preserve Bannerlord's safeguard against marrying two ruling clan leaders.
        if (firstHero.Clan?.Leader == firstHero && secondHero.Clan?.Leader == secondHero)
            return false;

        var involvesPlayerHouse = InvolvesPlayerClan(firstHero, secondHero);

        // Random AI marriages stay same-culture + same FaceGen race. Player-arranged
        // social marriages can be broader, but pregnancy still has its own strict
        // same-race biological gate.
        if (!involvesPlayerHouse && !IsSafeNpcSocialMarriagePair(firstHero, secondHero))
            return false;

        if (firstHero.IsFemale == secondHero.IsFemale)
        {
            if (!IsPlayerClanFemaleCouple(firstHero, secondHero))
                return false;

            return IsFemaleCoupleSuitable(firstHero, secondHero);
        }

        if (AreHeroesRelated(firstHero, secondHero, 3))
            return false;

        var courtedByFirst = Romance.GetCourtedHeroInOtherClan(firstHero, secondHero);
        if (courtedByFirst != null && courtedByFirst != secondHero)
            return false;

        var courtedBySecond = Romance.GetCourtedHeroInOtherClan(secondHero, firstHero);
        if (courtedBySecond != null && courtedBySecond != firstHero)
            return false;

        // Do not call DefaultMarriageModel/CanMarry here: KaiTOR must own the lore-age
        // decision instead of reintroducing TOR's global false gate.
        return IsSuitableForMarriage(firstHero) && IsSuitableForMarriage(secondHero);
    }

    public override bool IsSuitableForMarriage(Hero hero)
    {
        if (hero == null || !hero.IsAlive || !hero.IsActive)
            return false;
        if (KaiRaceLifecycle.IsDawi(hero))
            return false;
        if (!KaiRaceLifecycle.CanUseSocialMarriage(hero))
            return false;
        if (!IsSupportedMarriageCulture(hero.Culture?.StringId))
            return false;
        if (hero.Spouse != null || !hero.IsLord || hero.IsMinorFactionHero || hero.IsNotable || hero.IsTemplate)
            return false;
        if (hero.PartyBelongedTo?.MapEvent != null || hero.PartyBelongedTo?.Army != null)
            return false;

        var marriageOffers = Campaign.Current?.GetCampaignBehavior<IMarriageOfferCampaignBehavior>();
        if (marriageOffers != null && marriageOffers.IsHeroEngaged(hero))
            return false;

        // Only the lore adulthood floor matters. There is deliberately no upper age cap.
        return KaiRaceLifecycle.IsLoreMarriageAge(hero);
    }

    public override bool IsClanSuitableForMarriage(Clan clan)
    {
        if (clan == null || clan.IsBanditFaction || clan.IsRebelClan || clan.IsEliminated)
            return false;

        return IsSupportedMarriageCulture(clan.Culture?.StringId);
    }

    public override Clan GetClanAfterMarriage(Hero firstHero, Hero secondHero)
    {
        if (IsPlayerClanFemaleCouple(firstHero, secondHero))
        {
            if (firstHero?.Clan == Clan.PlayerClan)
                return Clan.PlayerClan;
            if (secondHero?.Clan == Clan.PlayerClan)
                return Clan.PlayerClan;
        }

        return base.GetClanAfterMarriage(firstHero, secondHero);
    }

    public override float NpcCoupleMarriageChance(Hero firstHero, Hero secondHero)
    {
        if (!IsSafeNpcSocialMarriagePair(firstHero, secondHero))
            return 0f;
        if (!IsCoupleSuitableForMarriage(firstHero, secondHero))
            return 0f;

        // Use a normalized social age for every people. Old heroes remain marriageable,
        // but huge calendar ages can never make the vanilla formula negative or explode.
        var firstAge = KaiRaceLifecycle.GetSocialMarriageAge(firstHero);
        var secondAge = KaiRaceLifecycle.GetSocialMarriageAge(secondHero);
        var adultAge = (float)Campaign.Current.Models.AgeModel.HeroComesOfAge;

        var chance = 0.002f;
        chance *= 1f + (firstAge - adultAge) / 50f;
        chance *= 1f + (secondAge - adultAge) / 50f;
        chance *= Math.Max(0f, 1f - Math.Abs(secondAge - firstAge) / 50f);

        if (firstHero.Clan?.Kingdom != secondHero.Clan?.Kingdom)
            chance *= 0.5f;

        var relationFactor = 0.5f + firstHero.Clan.GetRelationWithClan(secondHero.Clan) / 200f;
        return Math.Max(0f, chance * Math.Max(0f, relationFactor));
    }

    public override bool ShouldNpcMarriageBetweenClansBeAllowed(Clan consideringClan, Clan targetClan)
    {
        if (!IsClanSuitableForMarriage(consideringClan) ||
            !IsClanSuitableForMarriage(targetClan) ||
            consideringClan == targetClan)
            return false;

        // Automatic AI pairing remains same-culture to prevent accidental cross-race
        // dynasties. Player-arranged marriages may still use the broader pair rules.
        if (!string.Equals(
                consideringClan.Culture?.StringId,
                targetClan.Culture?.StringId,
                StringComparison.Ordinal))
            return false;

        if (consideringClan.IsAtWarWith(targetClan))
            return false;

        return consideringClan.GetRelationWithClan(targetClan) >= -50;
    }

    private bool IsFemaleCoupleSuitable(Hero firstHero, Hero secondHero)
    {
        if (!IsClanSuitableForMarriage(firstHero.Clan) || !IsClanSuitableForMarriage(secondHero.Clan))
            return false;
        if (firstHero.Clan?.Leader == firstHero && secondHero.Clan?.Leader == secondHero)
            return false;
        if (AreHeroesRelated(firstHero, secondHero, 3))
            return false;
        if (!IsSuitableForMarriage(firstHero) || !IsSuitableForMarriage(secondHero))
            return false;

        var courtedByFirst = Romance.GetCourtedHeroInOtherClan(firstHero, secondHero);
        if (courtedByFirst != null && courtedByFirst != secondHero)
            return false;

        var courtedBySecond = Romance.GetCourtedHeroInOtherClan(secondHero, firstHero);
        return courtedBySecond == null || courtedBySecond == firstHero;
    }

    private static bool IsPlayerClanFemaleCouple(Hero firstHero, Hero secondHero)
        => firstHero?.IsFemale == true &&
           secondHero?.IsFemale == true &&
           (firstHero.Clan == Clan.PlayerClan || secondHero.Clan == Clan.PlayerClan);

    private static bool IsSafeNpcSocialMarriagePair(Hero firstHero, Hero secondHero)
    {
        if (firstHero?.CharacterObject == null || secondHero?.CharacterObject == null)
            return false;
        if (InvolvesPlayerClan(firstHero, secondHero))
            return false;
        if (!KaiRaceLifecycle.CanUseSocialMarriage(firstHero) ||
            !KaiRaceLifecycle.CanUseSocialMarriage(secondHero))
            return false;
        if (!IsSupportedMarriageCulture(firstHero.Culture?.StringId) ||
            !IsSupportedMarriageCulture(secondHero.Culture?.StringId))
            return false;
        if (!string.Equals(firstHero.Culture?.StringId, secondHero.Culture?.StringId, StringComparison.Ordinal))
            return false;
        if (firstHero.CharacterObject.Race != secondHero.CharacterObject.Race)
            return false;

        return true;
    }

    private static bool AreHeroesRelated(Hero firstHero, Hero secondHero, int ancestorDepth)
        => AreHeroesRelatedAux(firstHero, secondHero, ancestorDepth, ancestorDepth);

    private static bool AreHeroesRelatedAux(Hero firstHero, Hero secondHero, int firstDepth, int secondDepth)
    {
        if (IsAncestorOrSelf(firstHero, secondHero, secondDepth))
            return true;

        if (firstDepth <= 0 || firstHero == null)
            return false;

        return (firstHero.Mother != null && AreHeroesRelatedAux(firstHero.Mother, secondHero, firstDepth - 1, secondDepth)) ||
               (firstHero.Father != null && AreHeroesRelatedAux(firstHero.Father, secondHero, firstDepth - 1, secondDepth));
    }

    private static bool IsAncestorOrSelf(Hero ancestor, Hero hero, int depth)
    {
        if (ancestor == null || hero == null)
            return false;
        if (ancestor == hero)
            return true;
        if (depth <= 0)
            return false;

        return (hero.Mother != null && IsAncestorOrSelf(ancestor, hero.Mother, depth - 1)) ||
               (hero.Father != null && IsAncestorOrSelf(ancestor, hero.Father, depth - 1));
    }

    public static bool AreCulturesCompatible(string firstCulture, string secondCulture)
        => IsSupportedMarriageCulture(firstCulture) && IsSupportedMarriageCulture(secondCulture);

    /// <summary>
    /// TOR currently uses these living cultures plus Greenskins. Greenskins are the one
    /// explicit conventional-marriage exclusion because they reproduce through spores.
    /// Unknown future living cultures are not blocked here; hero-level lore/race gates
    /// still reject Greenskins and ordinary undead.
    /// </summary>
    public static bool IsSupportedMarriageCulture(string cultureId)
        => !string.IsNullOrWhiteSpace(cultureId) &&
           !string.Equals(cultureId, "aserai", StringComparison.Ordinal);

    internal static bool InvolvesPlayerClan(Hero firstHero, Hero secondHero)
        => firstHero == Hero.MainHero || secondHero == Hero.MainHero ||
           firstHero?.Clan == Clan.PlayerClan || secondHero?.Clan == Clan.PlayerClan;
}
