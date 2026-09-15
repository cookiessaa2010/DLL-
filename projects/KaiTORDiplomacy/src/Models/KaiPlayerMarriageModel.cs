using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;

namespace KaiTOR.Diplomacy.Models;

/// <summary>
/// Player-facing marriage compatibility layer for TOR.
/// TOR disables vanilla marriage globally. KaiTOR re-enables only marriages
/// involving the player clan, while NPC-to-NPC dynastic automation remains off.
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

        // Bannerlord offspring creation expects both parents to use the same race id.
        // This is more important in TOR than vanilla because cultures can represent
        // humans, vampires, elves, dwarfs and greenskins with different skeleton/body data.
        if (firstHero.CharacterObject == null || secondHero.CharacterObject == null ||
            firstHero.CharacterObject.Race != secondHero.CharacterObject.Race)
            return false;

        if (!AreCulturesCompatible(firstHero.Culture?.StringId, secondHero.Culture?.StringId))
            return false;

        return base.IsCoupleSuitableForMarriage(firstHero, secondHero);
    }

    public override bool IsSuitableForMarriage(Hero maidenOrSuitor)
    {
        if (maidenOrSuitor == null)
            return false;
        return base.IsSuitableForMarriage(maidenOrSuitor);
    }

    public override bool IsClanSuitableForMarriage(Clan clan)
        => base.IsClanSuitableForMarriage(clan);

    public override float NpcCoupleMarriageChance(Hero firstHero, Hero secondHero)
        => 0f;

    public override bool ShouldNpcMarriageBetweenClansBeAllowed(Clan consideringClan, Clan targetClan)
        => false;

    public static bool AreCulturesCompatible(string firstCulture, string secondCulture)
    {
        if (string.IsNullOrWhiteSpace(firstCulture) || string.IsNullOrWhiteSpace(secondCulture))
            return false;

        // Greenskins and unsupported/non-playable cultures remain excluded from vanilla
        // family mechanics until TOR supplies explicit reproduction/dynasty rules for them.
        if (IsForbiddenMarriageCulture(firstCulture) || IsForbiddenMarriageCulture(secondCulture))
            return false;

        if (string.Equals(firstCulture, secondCulture, StringComparison.Ordinal))
            return true;

        if (IsVampireCulture(firstCulture) && IsVampireCulture(secondCulture))
            return true;

        if (IsHumanCulture(firstCulture) && IsHumanCulture(secondCulture))
            return true;

        if (IsElfCulture(firstCulture) && IsElfCulture(secondCulture))
            return true;

        return false;
    }

    private static bool InvolvesPlayerClan(Hero firstHero, Hero secondHero)
        => firstHero == Hero.MainHero || secondHero == Hero.MainHero ||
           firstHero.Clan == Clan.PlayerClan || secondHero.Clan == Clan.PlayerClan;

    private static bool IsVampireCulture(string id)
        => string.Equals(id, "khuzait", StringComparison.Ordinal) ||
           string.Equals(id, "mousillon", StringComparison.Ordinal);

    private static bool IsHumanCulture(string id)
        => string.Equals(id, "empire", StringComparison.Ordinal) ||
           string.Equals(id, "vlandia", StringComparison.Ordinal);

    private static bool IsElfCulture(string id)
        => string.Equals(id, "battania", StringComparison.Ordinal) ||
           string.Equals(id, "eonir", StringComparison.Ordinal);

    private static bool IsForbiddenMarriageCulture(string id)
        => string.Equals(id, "aserai", StringComparison.Ordinal) ||
           string.Equals(id, "chaos_culture", StringComparison.Ordinal) ||
           string.Equals(id, "steppe_bandits", StringComparison.Ordinal) ||
           string.Equals(id, "druchii", StringComparison.Ordinal);
}
