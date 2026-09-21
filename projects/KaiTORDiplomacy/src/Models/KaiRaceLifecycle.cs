using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace KaiTOR.Diplomacy.Models;

/// <summary>
/// Lore-aware family lifecycle for TOR.
/// Calendar age remains owned by Bannerlord/TOR. KaiTOR only decides whether a hero is
/// socially marriageable and, where biologically appropriate, how calendar age maps to
/// fertility/marriage chance. Marriage has no upper-age cap: only lore adulthood matters.
/// </summary>
internal static class KaiRaceLifecycle
{
    public const float HumanMarriageAge = 18f;
    public const float HumanFertilityStart = 18f;
    public const float HumanFertilityEnd = 45f;

    public const float DawiMarriageAge = 30f;
    public const float DawiFertilityStart = 30f;
    public const float DawiFertilityCurveSaturationAge = 180f;

    public const float ElfMarriageAge = 18f;
    public const float ElfFertilityStart = 18f;
    public const float ElfFertilityCurveSaturationAge = 300f;

    public const float VampireMarriageAge = 18f;

    public static bool IsDawi(Hero hero)
        => IsRace(hero, "dwarf");

    public static bool IsElf(Hero hero)
        => IsRace(hero, "elf");

    public static bool IsLongLivedElf(Hero hero)
    {
        if (IsElf(hero))
            return true;

        var culture = hero?.Culture?.StringId;
        return string.Equals(culture, "battania", StringComparison.Ordinal) ||
               string.Equals(culture, "eonir", StringComparison.Ordinal);
    }

    public static bool IsGreenskin(Hero hero)
        => TorFamilySafety.IsCulture(hero, "aserai") ||
           IsRace(hero, "orc") ||
           IsRace(hero, "goblin");

    /// <summary>
    /// Conventional marriage is available to living mortal peoples and vampires.
    /// Greenskins use their spore lifecycle; ordinary undead do not form normal
    /// biological/social dynasties in KaiTOR.
    /// </summary>
    public static bool CanUseSocialMarriage(Hero hero)
    {
        if (hero?.CharacterObject == null)
            return false;
        if (IsGreenskin(hero))
            return false;
        if (TorFamilySafety.IsUndeadNonVampire(hero))
            return false;

        return true;
    }

    /// <summary>
    /// Biological pregnancy is available to living, sexually reproducing, same-race
    /// peoples. Pair-level safety (sex/race/assets) is enforced in TorFamilySafety.
    /// </summary>
    public static bool CanUseBiologicalPregnancy(Hero hero)
    {
        if (hero?.CharacterObject == null)
            return false;
        if (IsGreenskin(hero))
            return false;
        if (IsDawi(hero))
            return false;
        if (TorFamilySafety.IsVampire(hero))
            return false;
        if (TorFamilySafety.IsUndead(hero))
            return false;

        return true;
    }

    /// <summary>
    /// Only races whose calendar lifespan differs materially from Bannerlord humans
    /// require a custom pregnancy curve. Ordinary mortal peoples continue through the
    /// active Bannerlord/TOR pregnancy model.
    /// </summary>
    public static bool UsesCustomFertility(Hero hero)
        => IsLongLivedElf(hero);

    public static float GetMinimumMarriageAge(Hero hero)
    {
        if (IsDawi(hero))
            return DawiMarriageAge;
        if (IsLongLivedElf(hero))
            return ElfMarriageAge;
        if (TorFamilySafety.IsVampire(hero))
            return VampireMarriageAge;
        return HumanMarriageAge;
    }

    /// <summary>
    /// No upper social-marriage age exists. Ancient Dawi, elves and vampires may still
    /// marry; ordinary mortals may marry after adulthood even after fertility ends.
    /// </summary>
    public static bool IsLoreMarriageAge(Hero hero)
        => hero != null && hero.Age >= GetMinimumMarriageAge(hero);

    public static bool IsWithinLoreFertilityWindow(Hero hero)
    {
        if (hero == null || !CanUseBiologicalPregnancy(hero))
            return false;

        if (IsDawi(hero))
            return false;

        if (IsLongLivedElf(hero))
            return hero.Age >= ElfFertilityStart;

        return hero.Age >= HumanFertilityStart && hero.Age <= HumanFertilityEnd;
    }

    // Compatibility alias used by the pregnancy decorator.
    public static bool IsWithinCustomFertilityWindow(Hero hero)
        => IsWithinLoreFertilityWindow(hero);

    /// <summary>
    /// Projects long-lived calendar ages onto Bannerlord's stable 18..45 family curve.
    /// For ordinary mortals it returns calendar age; vampires use a clamped social age
    /// because they marry socially but never enter the pregnancy pipeline.
    /// </summary>
    public static float GetBiologicalAge(Hero hero)
    {
        if (hero == null)
            return HumanFertilityStart;

        if (IsDawi(hero))
            return Project(Math.Min(hero.Age, DawiFertilityCurveSaturationAge), DawiFertilityStart, DawiFertilityCurveSaturationAge);

        if (IsLongLivedElf(hero))
            return Project(Math.Min(hero.Age, ElfFertilityCurveSaturationAge), ElfFertilityStart, ElfFertilityCurveSaturationAge);

        if (TorFamilySafety.IsVampire(hero))
            return Clamp(hero.Age, HumanFertilityStart, HumanFertilityEnd);

        return hero.Age;
    }

    /// <summary>
    /// Marriage chance must never become negative or explode simply because a
    /// long-lived/old hero has a large calendar age. This age is only for the AI chance
    /// curve; it never prevents marriage.
    /// </summary>
    public static float GetSocialMarriageAge(Hero hero)
    {
        if (hero == null)
            return HumanMarriageAge;

        if (IsDawi(hero))
            return ProjectFromAdulthood(hero.Age, DawiMarriageAge, DawiFertilityCurveSaturationAge);

        if (IsLongLivedElf(hero))
            return ProjectFromAdulthood(hero.Age, ElfMarriageAge, ElfFertilityCurveSaturationAge);

        return Clamp(hero.Age, HumanMarriageAge, HumanFertilityEnd);
    }

    public static float GetCustomPregnancyAgeFactor(Hero hero)
    {
        if (!IsWithinLoreFertilityWindow(hero))
            return 0f;

        var biologicalAge = GetBiologicalAge(hero);
        return Math.Max(0f, 1.2f - (biologicalAge - HumanFertilityStart) * 0.04f);
    }

    public static IEnumerable<string> DescribeRules()
    {
        yield return "Human/mortal: marriage 18+ (no upper cap); pregnancy 18-45.";
        yield return "Dawi: ordinary Hero-to-Hero marriage/pregnancy OFF; player marriage is an abstract house union; no female Hero/assets; heirs materialize only as adult male Dawi.";
        yield return "Elf: marriage 18+ (no upper cap); pregnancy 18+ (no vanilla upper cap); same race.";
        yield return "Vampire: social marriage 18+ (no upper cap); biological pregnancy OFF; reproduction via Blood Kiss.";
        yield return "Greenskin: conventional marriage OFF; biological pregnancy OFF; reproduction via spores.";
        yield return "Ordinary undead: conventional marriage OFF; biological pregnancy OFF.";
        yield return "Unknown living same-race peoples: marriage 18+; pregnancy delegates to Bannerlord/TOR human-like biology unless a custom race rule exists.";
    }

    private static bool IsRace(Hero hero, string raceId)
    {
        if (hero?.CharacterObject == null || string.IsNullOrWhiteSpace(raceId))
            return false;

        var requested = FaceGen.GetRaceOrDefault(raceId);
        var human = FaceGen.GetRaceOrDefault("human");
        return requested != human && hero.CharacterObject.Race == requested;
    }

    private static float ProjectFromAdulthood(float age, float sourceStart, float sourceEnd)
    {
        if (age <= sourceStart)
            return HumanMarriageAge;
        if (sourceEnd <= sourceStart)
            return HumanMarriageAge;

        return Project(Math.Min(age, sourceEnd), sourceStart, sourceEnd);
    }

    private static float Project(float age, float sourceStart, float sourceEnd)
    {
        if (sourceEnd <= sourceStart)
            return HumanFertilityStart;

        var t = (age - sourceStart) / (sourceEnd - sourceStart);
        t = Clamp(t, 0f, 1f);
        return HumanFertilityStart + t * (HumanFertilityEnd - HumanFertilityStart);
    }

    private static float Clamp(float value, float min, float max)
        => value < min ? min : value > max ? max : value;
}
