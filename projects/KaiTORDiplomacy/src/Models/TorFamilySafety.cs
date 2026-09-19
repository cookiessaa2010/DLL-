using System;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace KaiTOR.Diplomacy.Models;

/// <summary>
/// Small reflection bridge for TOR hero biology/role flags. KaiTOR deliberately does
/// not reference TOR_Core at compile time so the module can fail closed when TOR internals move.
/// </summary>
internal static class TorFamilySafety
{
    private const string HeroExtensionsTypeName = "TOR_Core.Extensions.HeroExtensions, TOR_Core";

    private static readonly MethodInfo IsVampireMethod = FindHeroExtension("IsVampire");
    private static readonly MethodInfo IsUndeadMethod = FindHeroExtension("IsUndead");
    private static readonly MethodInfo IsAiCompanionMethod = FindHeroExtension("IsAICompanion");
    private static readonly MethodInfo AddAttributeMethod = FindHeroMutation("AddAttribute");

    public static bool CanAddAttribute => AddAttributeMethod != null;

    public static bool IsVampire(Hero hero)
    {
        if (hero?.CharacterObject == null)
            return false;

        // TOR defines vampire heroes by FaceGen race. Check the actual race first so
        // family safety does not depend on ExtendedInfo/reflection initialization order.
        var race = hero.CharacterObject.Race;
        if (race == FaceGen.GetRaceOrDefault("vampire") || race == FaceGen.GetRaceOrDefault("necrarch"))
            return true;

        return TryInvokeFlag(IsVampireMethod, hero, out var value) && value;
    }

    public static bool IsUndead(Hero hero)
        => TryInvokeFlag(IsUndeadMethod, hero, out var value) && value;

    public static bool IsAiCompanion(Hero hero)
        => TryInvokeFlag(IsAiCompanionMethod, hero, out var value) && value;

    public static bool IsUndeadNonVampire(Hero hero)
    {
        if (hero == null) return false;
        if (IsVampire(hero)) return false;

        var undeadKnown = TryInvokeFlag(IsUndeadMethod, hero, out var undead);
        return undeadKnown && undead;
    }

    public static bool CanUseVanillaPregnancy(Hero firstHero, Hero secondHero)
        => GetVanillaPregnancyBlockReason(firstHero, secondHero) == null;

    public static string GetVanillaPregnancyBlockReason(Hero firstHero, Hero secondHero)
    {
        if (firstHero?.CharacterObject == null || secondHero?.CharacterObject == null)
            return "missing_character";

        // Bannerlord's offspring generator requires one female and one male parent.
        if (firstHero.IsFemale == secondHero.IsFemale)
            return "same_sex";

        // Bannerlord 1.3.x DeliverOffSpring asserts that both parents use the same race.
        if (firstHero.CharacterObject.Race != secondHero.CharacterObject.Race)
            return "different_facegen_race";

        if (IsVampire(firstHero) || IsVampire(secondHero))
            return "vampire";

        if (IsUndeadNonVampire(firstHero) || IsUndeadNonVampire(secondHero))
            return "undead";

        if (KaiRaceLifecycle.IsGreenskin(firstHero) || KaiRaceLifecycle.IsGreenskin(secondHero))
            return "greenskin";

        if (!KaiRaceLifecycle.CanUseBiologicalPregnancy(firstHero) ||
            !KaiRaceLifecycle.CanUseBiologicalPregnancy(secondHero))
            return "lore_biology_gate";

        if (KaiRaceLifecycle.IsDawi(firstHero) || KaiRaceLifecycle.IsDawi(secondHero))
        {
            if (!DawiWomenAssetBridge.IsSupportedDawiPair(firstHero, secondHero))
                return "dawi_assets_or_pair";
        }

        return null;
    }

    /// <summary>
    /// Applies the same public race mutation TOR uses for its player Blood Kiss flows.
    /// No career/religion is fabricated here; KaiTOR only changes biological race and
    /// lets TOR systems continue to own careers, spells, religion and resources.
    /// </summary>
    public static bool TryApplyBloodKiss(Hero hero)
    {
        if (hero?.CharacterObject == null || IsVampire(hero))
            return false;

        var vampireRace = FaceGen.GetRaceOrDefault("vampire");
        var humanRace = FaceGen.GetRaceOrDefault("human");
        if (vampireRace == humanRace)
            return false;

        hero.CharacterObject.Race = vampireRace;
        return IsVampire(hero);
    }

    public static bool TryAddAttribute(Hero hero, string attribute)
    {
        if (AddAttributeMethod == null || hero == null || string.IsNullOrWhiteSpace(attribute))
            return false;

        try
        {
            AddAttributeMethod.Invoke(null, new object[] { hero, attribute });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsCulture(Hero hero, string id)
        => string.Equals(hero?.Culture?.StringId, id, StringComparison.Ordinal);

    private static bool IsVampireCulture(Hero hero)
        => IsCulture(hero, "khuzait") || IsCulture(hero, "mousillon");

    private static MethodInfo FindHeroExtension(string methodName)
    {
        var type = Type.GetType(HeroExtensionsTypeName, false);
        return type?.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal)) return false;
                var parameters = method.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == typeof(Hero) && method.ReturnType == typeof(bool);
            });
    }

    private static MethodInfo FindHeroMutation(string methodName)
    {
        var type = Type.GetType(HeroExtensionsTypeName, false);
        return type?.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal)) return false;
                var parameters = method.GetParameters();
                return parameters.Length == 2 &&
                       parameters[0].ParameterType == typeof(Hero) &&
                       parameters[1].ParameterType == typeof(string) &&
                       method.ReturnType == typeof(void);
            });
    }

    private static bool TryInvokeFlag(MethodInfo method, Hero hero, out bool value)
    {
        value = false;
        if (method == null || hero == null) return false;

        try
        {
            if (method.Invoke(null, new object[] { hero }) is bool result)
            {
                value = result;
                return true;
            }
        }
        catch
        {
            // Family/lifecycle safety is conservative: callers avoid unsafe assumptions
            // when TOR reflection hooks cannot be resolved.
        }

        return false;
    }
}
