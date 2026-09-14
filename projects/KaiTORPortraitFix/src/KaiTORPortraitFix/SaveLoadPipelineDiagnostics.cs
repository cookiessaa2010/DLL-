using System;
using System.Globalization;
using System.Threading;
using HarmonyLib;
using TaleWorlds.MountAndBlade.GauntletUI.TextureProviders;
using TaleWorlds.MountAndBlade.View.Tableaus;

namespace KaiTORPortraitFix
{
    /// <summary>
    /// Diagnostic-only instrumentation for the cold-menu Save/Load hero preview pipeline.
    /// It intentionally does not change renderer state. We need to determine whether TOR 1.3.15
    /// reaches SaveLoadHeroTableauTextureProvider, BasicCharacterTableau.DeserializeCharacterCode,
    /// FirstTimeInit / SetTargetSize / OnTick, and finally RefreshCharacterTableau.
    /// Logging is aggressively capped so the menu tick cannot spam the disk.
    /// </summary>
    internal static class SaveLoadPipelineDiagnostics
    {
        private static int _providerCtor;
        private static int _heroCode;
        private static int _providerTick;
        private static int _targetSize;
        private static int _deserialize;
        private static int _firstInit;
        private static int _basicTick;

        internal static string DescribeCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return "empty=true";
            try
            {
                var p = code.Split('|');
                var version = p.Length > 0 ? p[0] : "?";
                var skeleton = p.Length > 1 ? Short(p[1], 48) : "?";
                var female = p.Length > 3 ? p[3] : "?";
                var race = p.Length > 4 ? p[4] : "?";
                return "len=" + code.Length + "; parts=" + p.Length + "; version=" + version +
                       "; skeleton=" + skeleton + "; isFemale=" + female + "; race=" + race;
            }
            catch (Exception ex)
            {
                return "len=" + code.Length + "; parseError=" + ex.GetType().Name;
            }
        }

        private static string DescribeBasic(BasicCharacterTableau tableau)
        {
            if (tableau == null) return "tableau=null";
            try
            {
                var t = Traverse.Create(tableau);
                return "initialized=" + SafeField<bool>(t, "_initialized") +
                       "; firstFrame=" + SafeField<bool>(t, "_isFirstFrame") +
                       "; visualsDirty=" + SafeField<bool>(t, "_isVisualsDirty") +
                       "; versionCompatible=" + SafeField<bool>(t, "_isVersionCompatible") +
                       "; race=" + SafeField<int>(t, "_race") +
                       "; isFemale=" + SafeField<bool>(t, "_isFemale") +
                       "; skeleton=" + Short(SafeField<string>(t, "_skeletonName"), 48) +
                       "; texture=" + (tableau.Texture != null);
            }
            catch (Exception ex)
            {
                return "inspectError=" + ex.GetType().Name;
            }
        }

        private static T SafeField<T>(Traverse traverse, string name)
        {
            try { return traverse.Field(name).GetValue<T>(); }
            catch { return default(T); }
        }

        private static string Short(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            return value.Length <= max ? value : value.Substring(0, max);
        }

        [HarmonyPatch(typeof(SaveLoadHeroTableauTextureProvider), MethodType.Constructor)]
        private static class ProviderCtorPatch
        {
            [HarmonyPostfix]
            private static void Postfix(SaveLoadHeroTableauTextureProvider __instance)
            {
                var n = Interlocked.Increment(ref _providerCtor);
                if (n <= 8)
                    PortraitFixLog.Event("PIPE_PROVIDER_CTOR", "sample=" + n + "; instance=" + (__instance != null));
            }
        }

        [HarmonyPatch(typeof(SaveLoadHeroTableauTextureProvider), "set_HeroVisualCode")]
        private static class HeroVisualCodePatch
        {
            [HarmonyPrefix]
            private static void Prefix(string value)
            {
                var n = Interlocked.Increment(ref _heroCode);
                if (n <= 24)
                    PortraitFixLog.Event("PIPE_HEROCODE", "sample=" + n + "; " + DescribeCode(value));
            }
        }

        [HarmonyPatch(typeof(SaveLoadHeroTableauTextureProvider), "Tick")]
        private static class ProviderTickPatch
        {
            [HarmonyPrefix]
            private static void Prefix(SaveLoadHeroTableauTextureProvider __instance, float dt)
            {
                var n = Interlocked.Increment(ref _providerTick);
                if (n > 20) return;
                try
                {
                    var tableau = Traverse.Create(__instance).Field("_tableau").GetValue<BasicCharacterTableau>();
                    PortraitFixLog.Event("PIPE_PROVIDER_TICK",
                        "sample=" + n + "; dt=" + dt.ToString("0.###", CultureInfo.InvariantCulture) + "; " + DescribeBasic(tableau));
                }
                catch (Exception ex)
                {
                    PortraitFixLog.Event("PIPE_PROVIDER_TICK", "sample=" + n + "; inspectError=" + ex.GetType().Name);
                }
            }
        }

        [HarmonyPatch(typeof(BasicCharacterTableau), "SetTargetSize")]
        private static class TargetSizePatch
        {
            [HarmonyPrefix]
            private static void Prefix(BasicCharacterTableau __instance, int width, int height)
            {
                var n = Interlocked.Increment(ref _targetSize);
                if (n <= 16)
                    PortraitFixLog.Event("PIPE_TARGET_SIZE", "sample=" + n + "; width=" + width + "; height=" + height + "; " + DescribeBasic(__instance));
            }
        }

        [HarmonyPatch(typeof(BasicCharacterTableau), "DeserializeCharacterCode")]
        private static class DeserializePatch
        {
            [HarmonyPrefix]
            private static void Prefix(string code)
            {
                var n = Interlocked.Increment(ref _deserialize);
                if (n <= 24)
                    PortraitFixLog.Event("PIPE_DESERIALIZE_IN", "sample=" + n + "; " + DescribeCode(code));
            }

            [HarmonyPostfix]
            private static void Postfix(BasicCharacterTableau __instance)
            {
                var n = Volatile.Read(ref _deserialize);
                if (n <= 24)
                    PortraitFixLog.Event("PIPE_DESERIALIZE_OUT", "sample=" + n + "; " + DescribeBasic(__instance));
            }
        }

        [HarmonyPatch(typeof(BasicCharacterTableau), "FirstTimeInit")]
        private static class FirstTimeInitPatch
        {
            [HarmonyPrefix]
            private static void Prefix(BasicCharacterTableau __instance)
            {
                var n = Interlocked.Increment(ref _firstInit);
                if (n <= 12)
                    PortraitFixLog.Event("PIPE_FIRST_INIT", "sample=" + n + "; before; " + DescribeBasic(__instance));
            }

            [HarmonyPostfix]
            private static void Postfix(BasicCharacterTableau __instance)
            {
                var n = Volatile.Read(ref _firstInit);
                if (n <= 12)
                    PortraitFixLog.Event("PIPE_FIRST_INIT", "sample=" + n + "; after; " + DescribeBasic(__instance));
            }
        }

        [HarmonyPatch(typeof(BasicCharacterTableau), "OnTick")]
        private static class BasicTickPatch
        {
            [HarmonyPrefix]
            private static void Prefix(BasicCharacterTableau __instance, float dt)
            {
                var n = Interlocked.Increment(ref _basicTick);
                if (n <= 24)
                    PortraitFixLog.Event("PIPE_BASIC_TICK",
                        "sample=" + n + "; dt=" + dt.ToString("0.###", CultureInfo.InvariantCulture) + "; " + DescribeBasic(__instance));
            }
        }
    }
}
