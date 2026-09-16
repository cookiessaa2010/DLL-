using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using SandBox.ViewModelCollection.SaveLoad;
using TaleWorlds.CampaignSystem.Extensions;

namespace KaiTORPortraitFix
{
    /// <summary>
    /// Restores only the Save/Load preview visual code when Bannerlord clears MainHeroVisualCode
    /// because the selected save has a module discrepancy.
    ///
    /// This patch does not write to the save file, does not suppress discrepancy warnings and does
    /// not bypass the game's normal load checks. It only supplies the already-stored metadata visual
    /// code back to the SavedGameVM preview.
    /// </summary>
    [HarmonyPatch]
    internal static class SavedGamePreviewCodePatch
    {
        private static int _appliedLogs;
        private static int _skipLogs;
        private const int MaxAppliedLogs = 8;
        private const int MaxSkipLogs = 3;

        [HarmonyTargetMethods]
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            var constructors = typeof(SavedGameVM).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            PortraitFixLog.Event("SAVE_VM_TARGETS", "constructors=" + constructors.Length);
            foreach (var constructor in constructors)
                yield return constructor;
        }

        [HarmonyPostfix]
        internal static void Postfix(SavedGameVM __instance)
        {
            if (__instance == null)
                return;

            try
            {
                if (__instance.IsCorrupted)
                {
                    LogSkip("reason=corrupted-save");
                    return;
                }

                if (!string.IsNullOrEmpty(__instance.MainHeroVisualCode))
                    return;

                var metadataCode = __instance.Save != null && __instance.Save.MetaData != null
                    ? __instance.Save.MetaData.GetCharacterVisualCode()
                    : string.Empty;

                if (string.IsNullOrEmpty(metadataCode))
                {
                    LogSkip("reason=metadata-code-empty");
                    return;
                }

                __instance.MainHeroVisualCode = metadataCode;
                LogApplied(
                    "reason=restore-preview-code; discrepancy=" + __instance.IsModuleDiscrepancyDetected +
                    "; codeLen=" + metadataCode.Length);
            }
            catch (Exception ex)
            {
                PortraitFixLog.Event(
                    "SAVE_VM_FIX",
                    "applied=false; error=" + ex.GetType().FullName + ": " + ex.Message);
            }
        }

        private static void LogApplied(string message)
        {
            var n = Interlocked.Increment(ref _appliedLogs);
            if (n <= MaxAppliedLogs)
            {
                PortraitFixLog.Event("SAVE_VM_FIX", "applied=true; " + message);
            }
            else if (n == MaxAppliedLogs + 1)
            {
                PortraitFixLog.Event(
                    "SAVE_VM_FIX",
                    "applied=true; further-success-events-suppressed=true");
            }
        }

        private static void LogSkip(string message)
        {
            var n = Interlocked.Increment(ref _skipLogs);
            if (n <= MaxSkipLogs)
                PortraitFixLog.Event("SAVE_VM_FIX", "applied=false; " + message);
            else if (n == MaxSkipLogs + 1)
                PortraitFixLog.Event("SAVE_VM_FIX", "further-skip-events-suppressed=true");
        }
    }
}
