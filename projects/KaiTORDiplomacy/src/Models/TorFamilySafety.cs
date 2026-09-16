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
        => TryInvokeFlag(IsVampireMethod, hero, out var value) && value;

    public static bool IsUndead(Hero hero)
        => TryInvokeFlag(IsUndeadMethod, hero, out var value) && value;

    public static bool IsAiCompanion(Hero hero)
        => TryInvokeFlag(IsAiCompanionMethod, hero, out var value) && value;

    public static bool IsUndeadNonVampire(Hero hero)
    {
        if (hero == null) return false;

        var undeadKnown = TryInvokeFlag(IsUndeadMethod, hero, out var undead);
        if (!undeadKnown || !undead) return false;

        var vampireKnown = TryInvokeFlag(IsVampireMethod, hero, out var vampire);
        return !vampireKnown || !vampire;
    }

    public static bool CanUseVanillaPregnancy(Hero firstHero, Hero secondHero)
    {
        if (firstHero?.CharacterObject == null || secondHero?.CharacterObject == null)
            return false;

        // Social same-sex marriages are deliberately childless in the biological
        // pipeline. Family growth for them is handled through adoption instead.
        if (firstHero.IsFemale == secondHero.IsFemale)
            return false;

        // Bannerlord 1.3.x DeliverOffSpring asserts that both parents use the same
        // CharacterObject.Race. Never allow the vanilla pregnancy pipeline to reach
        // that method for a cross-race marriage.
        if (firstHero.CharacterObject.Race != secondHero.CharacterObject.Race)
            return false;

        // Greenskins reproduce through the off-screen spore lifecycle, never through
        // Bannerlord pregnancy.
        if (IsCulture(firstHero, "aserai") || IsCulture(secondHero, "aserai"))
            return false;

        // Dawi use normal same-race family mechanics only when the complete optional
        // female-Dawi asset chain is actually registered. Until then they remain
        // fail-closed rather than producing a child the installed race cannot render.
        if (IsCulture(firstHero, "sturgia") || IsCulture(secondHero, "sturgia"))
        {
            if (!DawiWomenAssetBridge.IsSupportedDawiPair(firstHero, secondHero))
                return false;
        }

        // Vampires/other undead do not use Bannerlord biological reproduction.
        // If TOR's vampire hook cannot be resolved, fail closed for the two cultures
        // that can contain vampires rather than risk creating a broken child.
        var firstVampireKnown = TryInvokeFlag(IsVampireMethod, firstHero, out var firstVampire);
        var secondVampireKnown = TryInvokeFlag(IsVampireMethod, secondHero, out var secondVampire);
        if ((firstVampireKnown && firstVampire) || (secondVampireKnown && secondVampire))
            return false;
        if ((!firstVampireKnown && IsVampireCulture(firstHero)) ||
            (!secondVampireKnown && IsVampireCulture(secondHero)))
            return false;

        if (TryInvokeFlag(IsUndeadMethod, firstHero, out var firstUndead) && firstUndead)
            return false;
        if (TryInvokeFlag(IsUndeadMethod, secondHero, out var secondUndead) && secondUndead)
            return false;

        return true;
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
