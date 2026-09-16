using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Tableaus;

namespace KaiTORPortraitFix
{
    /// <summary>
    /// Controlled Save/Load pose experiment for Bannerlord 1.3.15.
    ///
    /// Normal in-game CharacterTableau resolves its inventory idle at render time and falls back
    /// to act_inventory_idle_start. BasicCharacterTableau (Save/Load preview) instead reads the
    /// cached ActionIndexCache.act_inventory_idle static field directly. If that cached field was
    /// initialized too early or points at an action that is not usable for the preview skeleton,
    /// the body can remain in its bind/prone pose even though the visual code itself is valid.
    ///
    /// This test changes only that one action source: the ldsfld of act_inventory_idle is replaced
    /// by a call that dynamically resolves act_inventory_idle_start at the moment the preview is
    /// refreshed. If idle_start cannot be resolved, it falls back to a dynamic act_inventory_idle.
    /// Race, skeleton name, equipment, body properties, spawn frames and save files are untouched.
    /// </summary>
    [HarmonyPatch(typeof(BasicCharacterTableau), "RefreshCharacterTableau")]
    internal static class BasicPreviewPosePatch
    {
        private static int _resolveSamples;
        private static readonly MethodInfo ResolveMethod = AccessTools.Method(
            typeof(BasicPreviewPosePatch),
            nameof(ResolveInventoryIdleLikeCharacterTableau));

        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            var idleField = AccessTools.Field(typeof(ActionIndexCache), "act_inventory_idle");
            var replacements = 0;

            if (idleField != null && ResolveMethod != null)
            {
                foreach (var instruction in list)
                {
                    if (instruction.opcode == OpCodes.Ldsfld &&
                        Equals(instruction.operand as FieldInfo, idleField))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = ResolveMethod;
                        replacements++;
                    }
                }
            }

            PortraitFixLog.Event(
                "VANILLA_IDLE_PATCH",
                "applied=" + (replacements > 0) +
                "; replacements=" + replacements +
                "; idleFieldFound=" + (idleField != null) +
                "; resolveMethodFound=" + (ResolveMethod != null) +
                "; from=ActionIndexCache.act_inventory_idle" +
                "; to=dynamic:act_inventory_idle_start" +
                "; fallback=dynamic:act_inventory_idle");

            return list;
        }

        internal static ActionIndexCache ResolveInventoryIdleLikeCharacterTableau()
        {
            var idleStart = ActionIndexCache.Create("act_inventory_idle_start");
            var idle = ActionIndexCache.Create("act_inventory_idle");
            var resolved = idleStart.Index >= 0 ? idleStart : idle;

            var sample = Interlocked.Increment(ref _resolveSamples);
            if (sample <= 24)
            {
                var cachedIdleIndex = ReadCachedIndex("act_inventory_idle");
                var cachedIdleStartIndex = ReadCachedIndex("act_inventory_idle_start");

                PortraitFixLog.Event(
                    "VANILLA_IDLE_RESOLVE",
                    "sample=" + sample +
                    "; cachedIdleIndex=" + cachedIdleIndex +
                    "; cachedIdleStartIndex=" + cachedIdleStartIndex +
                    "; dynamicIdleIndex=" + idle.Index +
                    "; dynamicIdleStartIndex=" + idleStart.Index +
                    "; selected=" + (idleStart.Index >= 0 ? "act_inventory_idle_start" : "act_inventory_idle") +
                    "; selectedIndex=" + resolved.Index);
            }

            return resolved;
        }

        private static int ReadCachedIndex(string fieldName)
        {
            try
            {
                var field = AccessTools.Field(typeof(ActionIndexCache), fieldName);
                if (field == null)
                    return int.MinValue;

                var value = field.GetValue(null);
                return value is ActionIndexCache action ? action.Index : int.MinValue;
            }
            catch
            {
                return int.MinValue;
            }
        }
    }
}
