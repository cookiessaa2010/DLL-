using System;
using System.Collections.Generic;
using HarmonyLib;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Keeps chronological Hero.Age/BirthDay intact while constraining only the age value
/// passed through CharacterObject body properties to FaceGen for long-lived TOR races.
/// </summary>
[HarmonyPatch(typeof(CharacterObject), nameof(CharacterObject.GetBodyProperties), typeof(Equipment), typeof(int))]
internal static class KaiVisualAgePatch
{
    private static readonly HashSet<string> LoggedHeroes = new(StringComparer.Ordinal);

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    private static void ApplyVisualAge(CharacterObject __instance, ref BodyProperties __result)
    {
        try
        {
            if (__instance == null || !__instance.IsHero)
                return;

            var hero = __instance.HeroObject;
            if (hero == null)
                return;

            var dynamicProperties = __result.DynamicProperties;
            var originalVisualAge = dynamicProperties.Age;
            var visualAge = KaiRaceLifecycle.GetVisualAge(hero, originalVisualAge);

            if (float.IsNaN(visualAge) || float.IsInfinity(visualAge) || visualAge < 0f)
                visualAge = 35f;

            if (Math.Abs(visualAge - originalVisualAge) > 0.01f)
            {
                __result = new BodyProperties(
                    new DynamicBodyProperties(visualAge, dynamicProperties.Weight, dynamicProperties.Build),
                    __result.StaticProperties);
            }

            if (ShouldLog(hero))
            {
                var biologicalAge = KaiRaceLifecycle.GetBiologicalAge(hero);
                KaiRuntimeLog.Write(
                    "FACE_AGE",
                    $"hero={hero.StringId}; race={hero.CharacterObject?.Race.ToString() ?? "null"}; culture={hero.Culture?.StringId ?? "null"}; calendar={hero.Age:0.0}; biological={biologicalAge:0.0}; sourceVisual={originalVisualAge:0.0}; visual={visualAge:0.0}; clamped={(Math.Abs(visualAge - originalVisualAge) > 0.01f)}");
            }
        }
        catch (Exception ex)
        {
            // Face rendering must never become a campaign-load blocker.
            KaiRuntimeLog.Exception("FACE_AGE_FAILED", ex, $"hero={__instance?.HeroObject?.StringId ?? "null"}");
        }
    }

    private static bool ShouldLog(Hero hero)
    {
        if (hero == null)
            return false;
        if (!KaiRaceLifecycle.IsDawi(hero) &&
            !KaiRaceLifecycle.IsLongLivedElf(hero) &&
            !KaiRaceLifecycle.IsGreenskin(hero) &&
            !TorFamilySafety.IsVampire(hero) &&
            !TorFamilySafety.IsUndead(hero))
            return false;

        lock (LoggedHeroes)
            return LoggedHeroes.Add(hero.StringId ?? hero.Name?.ToString() ?? "unknown");
    }
}
