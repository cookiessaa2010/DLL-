using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;

namespace KaiTOR.Diplomacy.Models;

/// <summary>
/// World marriage compatibility layer for TOR.
/// TOR disables vanilla marriage globally. KaiTOR restores Bannerlord's normal
/// player and NPC dynastic marriage flow for supported TOR cultures while keeping
/// biological reproduction behind a separate race/undead safety gate.
/// </summary>
public sealed class KaiPlayerMarriageModel : DefaultMarriageModel
{
    public const string ExpectedTorBaseType = "TOR_Core.Models.TORMarriageModel";
    private const float ChildlessNpcMarriageChanceMultiplier = 0.25f;

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

        // TOR vampires may form social/dynastic marriages. Non-vampire undead do not
        // participate in the ordinary family system.
        if (TorFamilySafety.IsUndeadNonVampire(firstHero) || TorFamilySafety.IsUndeadNonVampire(secondHero))
            return false;

        // Bannerlord's DefaultMarriageModel hard-requires opposite sexes. For the
        // player's house we additionally allow a woman to marry another woman while
        // preserving all ordinary age, clan, engagement and close-kin restrictions.
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
        return base.IsSuitableForMarriage(maidenOrSuitor);
    }

    public override bool IsClanSuitableForMarriage(Clan clan)
        => clan != null && IsSupportedMarriageCulture(clan.Culture?.StringId) && base.IsClanSuitableForMarriage(clan);

    public override Clan GetClanAfterMarriage(Hero firstHero, Hero secondHero)
    {
        // A female/female marriage involving the player's house keeps the player-clan
        // member in Clan.PlayerClan and brings the spouse into the player's house. This
        // avoids relying on DefaultMarriageModel's male/female clan-selection rule.
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
        var chance = base.NpcCoupleMarriageChance(firstHero, secondHero);
        if (chance <= 0f)
            return 0f;

        // Same biologically safe pairings keep Bannerlord's native dynastic rate.
        // Social marriages that KaiTOR deliberately makes childless are rarer for AI,
        // so mixed-species unions exist in the world without overwhelming dynasties.
        return TorFamilySafety.CanUseVanillaPregnancy(firstHero, secondHero)
            ? chance
            : chance * ChildlessNpcMarriageChanceMultiplier;
    }

    public override bool ShouldNpcMarriageBetweenClansBeAllowed(Clan consideringClan, Clan targetClan)
    {
        if (consideringClan == null || targetClan == null)
            return false;
        if (!AreCulturesCompatible(consideringClan.Culture?.StringId, targetClan.Culture?.StringId))
            return false;
        return base.ShouldNpcMarriageBetweenClansBeAllowed(consideringClan, targetClan);
    }

    private bool IsFemaleCoupleSuitable(Hero firstHero, Hero secondHero)
    {
        if (!IsClanSuitableForMarriage(firstHero.Clan) || !IsClanSuitableForMarriage(secondHero.Clan))
            return false;

        // Match Bannerlord's rule that two ruling clan leaders cannot marry each other.
        if (firstHero.Clan?.Leader == firstHero && secondHero.Clan?.Leader == secondHero)
            return false;

        if (AreHeroesRelated(firstHero, secondHero, 3))
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

    // Equivalent in intent to DefaultMarriageModel's private three-generation kinship
    // check. Keeping it local lets the female/female path obey the same safety rule
    // without reflection into Bannerlord internals.
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

    /// <summary>
    /// Social marriage policy for TOR's playable family cultures.
    /// Empire, Bretonnia, Sylvania/Mousillon, Asrai, Eonir and Dawi can intermarry
    /// when Bannerlord's age/kinship/current-marriage checks also pass.
    /// Greenskins remain outside ordinary marriage/family mechanics.
    /// </summary>
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
