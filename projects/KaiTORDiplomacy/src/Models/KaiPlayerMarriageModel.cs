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
        if (firstHero == null || secondHero == null)
            return false;

        if (!AreCulturesCompatible(firstHero.Culture?.StringId, secondHero.Culture?.StringId))
            return false;

        // TOR vampires may form social/dynastic marriages. Non-vampire undead do not
        // participate in the ordinary family system.
        if (TorFamilySafety.IsUndeadNonVampire(firstHero) || TorFamilySafety.IsUndeadNonVampire(secondHero))
            return false;

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

    /// <summary>
    /// Social marriage policy for TOR's playable family cultures.
    /// Empire, Bretonnia, Sylvania/Mousillon, Asrai, Eonir and Dawi can intermarry
    /// when Bannerlord's age/sex/kinship/current-marriage checks also pass.
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
