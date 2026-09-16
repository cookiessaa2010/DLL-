using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;

namespace KaiTOR.Diplomacy.Models;

/// <summary>
/// World marriage compatibility layer for TOR.
/// Player-house marriage uses Bannerlord's normal flow. Automatic NPC/NPC marriages
/// are intentionally narrower in LoadSafe: only certified same-culture, same-race,
/// biologically safe pairs are allowed to reach MarriageAction during daily ticks.
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

        if (TorFamilySafety.IsUndeadNonVampire(firstHero) || TorFamilySafety.IsUndeadNonVampire(secondHero))
            return false;

        var involvesPlayerHouse = InvolvesPlayerClan(firstHero, secondHero);

        // The random NPC marriage loop runs on daily clan ticks. Keep that path to
        // pairings already proven compatible with Bannerlord's ordinary family graph.
        // Dawi, greenskins, vampires and cross-culture NPC couples remain outside the
        // automatic path until their lifecycle is certified separately.
        if (!involvesPlayerHouse && !IsCertifiedNpcMarriagePair(firstHero, secondHero))
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
        if (!IsCertifiedNpcMarriagePair(firstHero, secondHero))
            return 0f;

        return base.NpcCoupleMarriageChance(firstHero, secondHero);
    }

    public override bool ShouldNpcMarriageBetweenClansBeAllowed(Clan consideringClan, Clan targetClan)
    {
        if (consideringClan == null || targetClan == null)
            return false;

        if (!IsCertifiedNpcDynastyCulture(consideringClan.Culture?.StringId) ||
            !IsCertifiedNpcDynastyCulture(targetClan.Culture?.StringId))
            return false;

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

    private static bool IsCertifiedNpcMarriagePair(Hero firstHero, Hero secondHero)
    {
        if (firstHero?.CharacterObject == null || secondHero?.CharacterObject == null)
            return false;
        if (InvolvesPlayerClan(firstHero, secondHero))
            return false;
        if (!IsCertifiedNpcDynastyCulture(firstHero.Culture?.StringId) ||
            !IsCertifiedNpcDynastyCulture(secondHero.Culture?.StringId))
            return false;
        if (!string.Equals(firstHero.Culture?.StringId, secondHero.Culture?.StringId, StringComparison.Ordinal))
            return false;
        if (firstHero.CharacterObject.Race != secondHero.CharacterObject.Race)
            return false;
        if (!TorFamilySafety.CanUseVanillaPregnancy(firstHero, secondHero))
            return false;

        return true;
    }

    // These are the ordinary living family cultures we currently allow the random
    // NPC daily-marriage loop to process. Dawi and vampire cultures can still marry
    // through explicit player-house negotiations, but are excluded from random NPC
    // marriages until their complete family lifecycle is certified in TOR.
    private static bool IsCertifiedNpcDynastyCulture(string cultureId)
        => string.Equals(cultureId, "empire", StringComparison.Ordinal) ||
           string.Equals(cultureId, "vlandia", StringComparison.Ordinal) ||
           string.Equals(cultureId, "battania", StringComparison.Ordinal) ||
           string.Equals(cultureId, "eonir", StringComparison.Ordinal);

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
