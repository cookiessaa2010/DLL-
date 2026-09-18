using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;

namespace KaiTOR.Diplomacy.Models;

/// <summary>
/// World marriage compatibility layer for TOR.
/// Social marriage and biological reproduction are separate concerns: a valid marriage
/// no longer depends on CanUseVanillaPregnancy. Automatic NPC marriage remains
/// conservative (same culture/race, opposite sex through DefaultMarriageModel) while
/// long-lived races use normalized biological ages in the NPC pairing chance.
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

        if (!AreCulturesCompatible(firstHero.Culture?.StringId, secondHero.Culture?.StringId))
            return false;

        // Ordinary non-vampire undead do not participate in the social marriage system.
        // Actual TOR vampires are handled separately and may form social marriages even
        // though the pregnancy pipeline intentionally rejects them.
        if (TorFamilySafety.IsUndeadNonVampire(firstHero) || TorFamilySafety.IsUndeadNonVampire(secondHero))
            return false;

        var involvesPlayerHouse = InvolvesPlayerClan(firstHero, secondHero);

        // Random NPC marriage is deliberately narrower than player-arranged marriage:
        // same culture + same FaceGen race only. It no longer requires biological
        // pregnancy compatibility, which was incorrectly preventing vampire marriages.
        if (!involvesPlayerHouse && !IsSafeNpcSocialMarriagePair(firstHero, secondHero))
            return false;

        // Bannerlord's DefaultMarriageModel hard-requires opposite sexes. For the
        // player's house we additionally allow a woman to marry another woman while
        // preserving ordinary age, clan, engagement and close-kin restrictions.
        if (IsPlayerClanFemaleCouple(firstHero, secondHero))
            return IsFemaleCoupleSuitable(firstHero, secondHero);

        return base.IsCoupleSuitableForMarriage(firstHero, secondHero);
    }

    public override bool IsSuitableForMarriage(Hero maidenOrSuitor)
    {
        if (maidenOrSuitor == null)
            return false;
        if (!IsSupportedMarriageCulture(maidenOrSuitor.Culture?.StringId))
            return false;
        if (TorFamilySafety.IsUndeadNonVampire(maidenOrSuitor))
            return false;

        // Dawi adulthood for family simulation begins at 30. This check is local to the
        // marriage model and deliberately does not replace Bannerlord's global AgeModel.
        if (maidenOrSuitor.Age < KaiRaceLifecycle.GetMinimumMarriageAge(maidenOrSuitor))
            return false;

        return base.IsSuitableForMarriage(maidenOrSuitor);
    }

    public override bool IsClanSuitableForMarriage(Clan clan)
        => clan != null && IsSupportedMarriageCulture(clan.Culture?.StringId) && base.IsClanSuitableForMarriage(clan);

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

        // Humans retain Bannerlord's exact formula. Long-lived races and vampires use
        // the same formula evaluated against normalized biological/social age so a
        // 50+ year calendar-age gap cannot make the chance negative by construction.
        if (!UsesNormalizedMarriageAge(firstHero) && !UsesNormalizedMarriageAge(secondHero))
            return base.NpcCoupleMarriageChance(firstHero, secondHero);

        var firstAge = KaiRaceLifecycle.GetBiologicalAge(firstHero);
        var secondAge = KaiRaceLifecycle.GetBiologicalAge(secondHero);
        var adultAge = (float)Campaign.Current.Models.AgeModel.HeroComesOfAge;

        var chance = 0.002f;
        chance *= 1f + (firstAge - adultAge) / 50f;
        chance *= 1f + (secondAge - adultAge) / 50f;
        chance *= Math.Max(0f, 1f - Math.Abs(secondAge - firstAge) / 50f);

        if (firstHero.Clan.Kingdom != secondHero.Clan.Kingdom)
            chance *= 0.5f;

        var relationFactor = 0.5f + firstHero.Clan.GetRelationWithClan(secondHero.Clan) / 200f;
        return Math.Max(0f, chance * relationFactor);
    }

    public override bool ShouldNpcMarriageBetweenClansBeAllowed(Clan consideringClan, Clan targetClan)
    {
        if (consideringClan == null || targetClan == null)
            return false;

        if (!IsSupportedMarriageCulture(consideringClan.Culture?.StringId) ||
            !IsSupportedMarriageCulture(targetClan.Culture?.StringId))
            return false;

        // Automatic AI marriage remains same-culture to avoid random cross-faction race
        // mixing. Player-arranged cross-culture social marriages remain possible when
        // the couple-level rules accept them.
        if (!string.Equals(consideringClan.Culture?.StringId, targetClan.Culture?.StringId, StringComparison.Ordinal))
            return false;

        return base.ShouldNpcMarriageBetweenClansBeAllowed(consideringClan, targetClan);
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
        if (courtedBySecond != null && courtedBySecond != firstHero)
            return false;

        return firstHero.CanMarry() && secondHero.CanMarry();
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
        if (!IsSupportedMarriageCulture(firstHero.Culture?.StringId) ||
            !IsSupportedMarriageCulture(secondHero.Culture?.StringId))
            return false;
        if (!string.Equals(firstHero.Culture?.StringId, secondHero.Culture?.StringId, StringComparison.Ordinal))
            return false;
        if (firstHero.CharacterObject.Race != secondHero.CharacterObject.Race)
            return false;
        if (TorFamilySafety.IsUndeadNonVampire(firstHero) || TorFamilySafety.IsUndeadNonVampire(secondHero))
            return false;

        return true;
    }

    private static bool UsesNormalizedMarriageAge(Hero hero)
        => KaiRaceLifecycle.IsDawi(hero) ||
           KaiRaceLifecycle.IsLongLivedElf(hero) ||
           TorFamilySafety.IsVampire(hero);

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

    public static bool IsSupportedMarriageCulture(string cultureId)
    {
        if (string.IsNullOrWhiteSpace(cultureId)) return false;

        return string.Equals(cultureId, "empire", StringComparison.Ordinal) ||
               string.Equals(cultureId, "vlandia", StringComparison.Ordinal) ||
               string.Equals(cultureId, "khuzait", StringComparison.Ordinal) ||
               string.Equals(cultureId, "mousillon", StringComparison.Ordinal) ||
               string.Equals(cultureId, "battania", StringComparison.Ordinal) ||
               string.Equals(cultureId, "eonir", StringComparison.Ordinal) ||
               string.Equals(cultureId, "sturgia", StringComparison.Ordinal);
    }

    internal static bool InvolvesPlayerClan(Hero firstHero, Hero secondHero)
        => firstHero == Hero.MainHero || secondHero == Hero.MainHero ||
           firstHero?.Clan == Clan.PlayerClan || secondHero?.Clan == Clan.PlayerClan;
}
