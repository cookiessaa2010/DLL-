using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace KaiTOR.Diplomacy.Models;

/// <summary>
/// Race-aware biological age projection used only by KaiTOR family simulation.
/// It deliberately does not replace Bannerlord's AgeModel: calendar age, childhood,
/// coming-of-age events and visual aging remain engine/TOR-owned.
/// </summary>
internal static class KaiRaceLifecycle
{
    public const float HumanFertilityStart = 18f;
    public const float HumanFertilityEnd = 45f;

    // Dawi become family-eligible later than humans and remain biologically capable
    // until the beginning of KaiTOR's existing Dawi old-age mortality window.
    public const float DawiMarriageAge = 30f;
    public const float DawiFertilityStart = 30f;
    public const float DawiFertilityEnd = 180f;

    // TOR's Asrai/Eonir have no ordinary human old-age mortality in KaiTOR. Use a
    // deliberately broad family-simulation span instead of feeding century-scale ages
    // into Bannerlord's 18..45 formula. This is a simulation scale, not a lifespan cap.
    public const float ElfMarriageAge = 18f;
    public const float ElfFertilityStart = 18f;
    public const float ElfFertilityEnd = 300f;

    public static bool IsDawi(Hero hero)
    {
        if (hero?.CharacterObject == null)
            return false;

        var dwarf = FaceGen.GetRaceOrDefault("dwarf");
        var human = FaceGen.GetRaceOrDefault("human");
        return dwarf != human && hero.CharacterObject.Race == dwarf;
    }

    public static bool IsLongLivedElf(Hero hero)
    {
        var culture = hero?.Culture?.StringId;
        return string.Equals(culture, "battania", StringComparison.Ordinal) ||
               string.Equals(culture, "eonir", StringComparison.Ordinal);
    }

    public static bool UsesCustomFertility(Hero hero)
        => IsDawi(hero) || IsLongLivedElf(hero);

    public static float GetMinimumMarriageAge(Hero hero)
    {
        if (IsDawi(hero))
            return DawiMarriageAge;
        if (IsLongLivedElf(hero))
            return ElfMarriageAge;
        return HumanFertilityStart;
    }

    public static bool IsWithinCustomFertilityWindow(Hero hero)
    {
        if (hero == null)
            return false;

        if (IsDawi(hero))
            return hero.Age >= DawiFertilityStart && hero.Age <= DawiFertilityEnd;

        if (IsLongLivedElf(hero))
            return hero.Age >= ElfFertilityStart && hero.Age <= ElfFertilityEnd;

        return false;
    }

    /// <summary>
    /// Projects a long-lived race's calendar age into the 18..45 biological range used
    /// by Bannerlord 1.3.15's fertility and NPC-marriage curves.
    /// </summary>
    public static float GetBiologicalAge(Hero hero)
    {
        if (hero == null)
            return HumanFertilityStart;

        if (IsDawi(hero))
            return Project(hero.Age, DawiFertilityStart, DawiFertilityEnd);

        if (IsLongLivedElf(hero))
            return Project(hero.Age, ElfFertilityStart, ElfFertilityEnd);

        // Vampires are socially marriageable but biologically excluded. Clamp their
        // social-age contribution so ancient vampires cannot make the vanilla NPC
        // marriage formula negative solely because of calendar age.
        if (TorFamilySafety.IsVampire(hero))
            return Clamp(hero.Age, HumanFertilityStart, HumanFertilityEnd);

        return hero.Age;
    }

    public static float GetCustomPregnancyAgeFactor(Hero hero)
    {
        if (!IsWithinCustomFertilityWindow(hero))
            return 0f;

        var biologicalAge = GetBiologicalAge(hero);
        // Exact age component from DefaultPregnancyModel 1.3.15, evaluated against
        // projected biological age instead of calendar age.
        return Math.Max(0f, 1.2f - (biologicalAge - HumanFertilityStart) * 0.04f);
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
