using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using TaleWorlds.MountAndBlade.View.Tableaus;

namespace KaiTORPortraitFix
{
    /// <summary>
    /// Bannerlord's Save/Load screen uses BasicCharacterTableau, a rendering path separate
    /// from the in-game CharacterTableau. In affected builds RefreshCharacterTableau feeds
    /// a temporary age-derived local boolean into the skin-generation call where the actual
    /// private _isFemale field is required. This can produce a broken save-preview while the
    /// same hero renders correctly in campaign/inventory.
    ///
    /// The transpiler is deliberately narrow: it only replaces the local loaded immediately
    /// after _faceDirtAmount with this._isFemale, matching the established upstream fix pattern.
    /// If the expected IL is not found, it changes nothing and logs a loud diagnostic.
    /// </summary>
    [HarmonyPatch(typeof(BasicCharacterTableau), "RefreshCharacterTableau")]
    internal static class SavePreviewGenderPatch
    {
        private static int _previewSamples;

        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            try
            {
                var faceDirt = AccessTools.Field(typeof(BasicCharacterTableau), "_faceDirtAmount");
                var isFemale = AccessTools.Field(typeof(BasicCharacterTableau), "_isFemale");
                if (faceDirt == null || isFemale == null)
                {
                    PortraitFixLog.Event("PATCH_PATTERN", "applied=false; reason=field-missing; faceDirt=" + (faceDirt != null) + "; isFemale=" + (isFemale != null));
                    return list;
                }

                var matcher = new CodeMatcher(list);
                matcher.MatchStartForward(
                    new CodeMatch(ci => ci.opcode == OpCodes.Ldarg_0),
                    new CodeMatch(ci => ci.opcode == OpCodes.Ldfld && Equals(ci.operand, faceDirt)),
                    new CodeMatch(ci => ci.IsLdloc()));

                if (!matcher.IsValid)
                {
                    // Compatibility fallback for IL where ldarg.0 is outside the matched pair.
                    matcher = new CodeMatcher(list);
                    matcher.MatchStartForward(
                        new CodeMatch(ci => ci.opcode == OpCodes.Ldfld && Equals(ci.operand, faceDirt)),
                        new CodeMatch(ci => ci.IsLdloc()));
                    if (!matcher.IsValid)
                    {
                        PortraitFixLog.Event("PATCH_PATTERN", "applied=false; reason=expected-il-not-found");
                        return list;
                    }
                    matcher.Advance(1);
                }
                else
                {
                    matcher.Advance(2);
                }

                matcher.RemoveInstruction();
                matcher.Insert(
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, isFemale));

                PortraitFixLog.Event("PATCH_PATTERN", "applied=true; fix=save-preview-gender");
                return matcher.InstructionEnumeration();
            }
            catch (Exception ex)
            {
                PortraitFixLog.Event("PATCH_ERROR", ex.GetType().FullName + ": " + ex.Message);
                return list;
            }
        }

        [HarmonyPrefix]
        internal static void Prefix(int ____race, bool ____isFemale, float ____faceDirtAmount)
        {
            // A few samples are enough to prove the Save/Load-only path is executing.
            var sample = Interlocked.Increment(ref _previewSamples);
            if (sample <= 24)
            {
                PortraitFixLog.Event(
                    "PREVIEW_REFRESH",
                    "sample=" + sample + "; race=" + ____race + "; isFemale=" + ____isFemale +
                    "; faceDirt=" + ____faceDirtAmount.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }
}
