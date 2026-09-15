using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;

namespace KaiTOR.Diplomacy.Models;

/// <summary>
/// Player-facing marriage compatibility layer for TOR.
/// TOR disables vanilla marriage globally. KaiTOR re-enables only marriages
/// involving the player clan while NPC-to-NPC dynastic automation remains off.
///
/// Marriage compatibility is intentionally broader than biological reproduction:
/// Dawi, humans and elves may marry across TOR race ids, while KaiPregnancyModel
/// prevents Bannerlord from attempting unsafe cross-race offspring generation.
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
        if (firstHero == null || secondHero == null)
            return false;

        if (!InvolvesPlayerClan(firstHero, secondHero))
            return false;

        if (!AreCulturesCompatible(firstHero.Culture?.StringId, secondHero.Culture?.StringId))
            return false;

        // TOR vampires can participate in social/dynastic marriage, but non-vampire
        // undead (skeleton/wight-style heroes) are not a valid family partner.
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
        => 0f;

    public override bool ShouldNpcMarriageBetweenClansBeAllowed(Clan consideringClan, Clan targetClan)
        => false;

    /// <summary>
    /// Social marriage policy for TOR's eight playable cultures.
    /// Empire, Bretonnia, Sylvania/Mousillon, Asrai, Eonir and Dawi can intermarry
    /// when the vanilla age/sex/family checks also pass. Greenskins are excluded:
    /// TOR has no female Orc family templates and Warhammer Greenskins do not use
    /// normal sexual reproduction/family mechanics.
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
