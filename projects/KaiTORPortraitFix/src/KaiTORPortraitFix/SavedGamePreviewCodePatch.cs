using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SandBox.ViewModelCollection.SaveLoad;
using TaleWorlds.CampaignSystem.Extensions;

namespace KaiTORPortraitFix
{
    /// <summary>
    /// Bannerlord blanks MainHeroVisualCode when a save has a module discrepancy. TOR saves
    /// commonly hit that gate when the active module set changed even though metadata still
    /// contains a valid character visual code. Restore only the preview property; never write
    /// back to the save. We apply after construction and after RefreshValues so the preview stays
    /// recoverable if another UI refresh rebuilds/clears the VM state.
    /// </summary>
    [HarmonyPatch]
    internal static class SavedGamePreviewCodePatch
    {
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
            SavePreviewCodeRestore.TryRestore(__instance, "ctor");
        }
    }

    [HarmonyPatch(typeof(SavedGameVM), "RefreshValues")]
    internal static class SavedGamePreviewRefreshPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(SavedGameVM __instance)
        {
            SavePreviewCodeRestore.TryRestore(__instance, "refresh");
        }
    }

    internal static class SavePreviewCodeRestore
    {
        internal static void TryRestore(SavedGameVM instance, string source)
        {
            if (instance == null)
                return;

            try
            {
                var before = instance.MainHeroVisualCode;
                var metadataCode = instance.Save != null && instance.Save.MetaData != null
                    ? instance.Save.MetaData.GetCharacterVisualCode()
                    : string.Empty;

                PortraitFixLog.Event(
                    "SAVE_VM",
                    "source=" + source +
                    "; corrupted=" + instance.IsCorrupted +
                    "; discrepancy=" + instance.IsModuleDiscrepancyDetected +
                    "; vmCodeEmpty=" + string.IsNullOrEmpty(before) +
                    "; metadataCodeEmpty=" + string.IsNullOrEmpty(metadataCode) +
                    "; metadataCodeLen=" + (metadataCode == null ? 0 : metadataCode.Length));

                if (instance.IsCorrupted)
                {
                    PortraitFixLog.Event("SAVE_VM_FIX", "source=" + source + "; applied=false; reason=corrupted-save");
                    return;
                }

                if (!string.IsNullOrEmpty(before))
                {
                    PortraitFixLog.Event("SAVE_VM_FIX", "source=" + source + "; applied=false; reason=vm-code-already-present");
                    return;
                }

                if (string.IsNullOrEmpty(metadataCode))
                {
                    PortraitFixLog.Event("SAVE_VM_FIX", "source=" + source + "; applied=false; reason=metadata-code-empty");
                    return;
                }

                instance.MainHeroVisualCode = metadataCode;
                PortraitFixLog.Event(
                    "SAVE_VM_FIX",
                    "source=" + source +
                    "; applied=true; reason=restore-preview-code; discrepancy=" + instance.IsModuleDiscrepancyDetected +
                    "; codeLen=" + metadataCode.Length);
            }
            catch (Exception ex)
            {
                PortraitFixLog.Event("SAVE_VM_FIX", "source=" + source + "; applied=false; error=" + ex.GetType().FullName + ": " + ex.Message);
            }
        }
    }
}
