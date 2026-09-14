using System;
using HarmonyLib;
using SandBox.ViewModelCollection.SaveLoad;
using TaleWorlds.CampaignSystem.Extensions;

namespace KaiTORPortraitFix
{
    /// <summary>
    /// Bannerlord intentionally clears SavedGameVM.MainHeroVisualCode when it detects a module
    /// discrepancy. That leaves SaveLoadHeroTableauTextureProvider with an empty visual code and
    /// the Save/Load screen shows only the black placeholder/silhouette even though the save and
    /// in-game hero are healthy.
    ///
    /// For non-corrupted saves only, restore the character visual code from save metadata. This
    /// changes only the preview VM; it does not modify the save file or relax the actual load-time
    /// discrepancy checks/warnings.
    /// </summary>
    [HarmonyPatch(typeof(SavedGameVM), MethodType.Constructor)]
    internal static class SavedGamePreviewCodePatch
    {
        [HarmonyPostfix]
        internal static void Postfix(SavedGameVM __instance)
        {
            if (__instance == null)
                return;

            try
            {
                var before = __instance.MainHeroVisualCode;
                var metadataCode = __instance.Save != null && __instance.Save.MetaData != null
                    ? __instance.Save.MetaData.GetCharacterVisualCode()
                    : string.Empty;

                PortraitFixLog.Event(
                    "SAVE_VM",
                    "corrupted=" + __instance.IsCorrupted +
                    "; discrepancy=" + __instance.IsModuleDiscrepancyDetected +
                    "; vmCodeEmpty=" + string.IsNullOrEmpty(before) +
                    "; metadataCodeEmpty=" + string.IsNullOrEmpty(metadataCode) +
                    "; metadataCodeLen=" + (metadataCode == null ? 0 : metadataCode.Length));

                if (__instance.IsCorrupted)
                {
                    PortraitFixLog.Event("SAVE_VM_FIX", "applied=false; reason=corrupted-save");
                    return;
                }

                if (!string.IsNullOrEmpty(before))
                {
                    PortraitFixLog.Event("SAVE_VM_FIX", "applied=false; reason=vm-code-already-present");
                    return;
                }

                if (string.IsNullOrEmpty(metadataCode))
                {
                    PortraitFixLog.Event("SAVE_VM_FIX", "applied=false; reason=metadata-code-empty");
                    return;
                }

                __instance.MainHeroVisualCode = metadataCode;
                PortraitFixLog.Event(
                    "SAVE_VM_FIX",
                    "applied=true; reason=restore-preview-code; discrepancy=" + __instance.IsModuleDiscrepancyDetected +
                    "; codeLen=" + metadataCode.Length);
            }
            catch (Exception ex)
            {
                PortraitFixLog.Event("SAVE_VM_FIX", "applied=false; error=" + ex.GetType().FullName + ": " + ex.Message);
            }
        }
    }
}
