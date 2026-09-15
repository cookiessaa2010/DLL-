using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Tableaus;

namespace KaiTORPortraitFix
{
    /// <summary>
    /// Save/Load uses BasicCharacterTableau. Bannerlord 1.3.x loads the cached
    /// ActionIndexCache.act_inventory_idle field directly, so replacing a string literal
    /// does not affect the real method body. This patch replaces that field load with
    /// ActionIndexCache.act_inventory_idle_start. A legacy string replacement is retained
    /// as a fallback for nearby game builds.
    /// </summary>
    [HarmonyPatch(typeof(BasicCharacterTableau), "RefreshCharacterTableau")]
    internal static class BasicPreviewPosePatch
    {
        private static int _samples;

        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            var idleField = AccessTools.Field(typeof(ActionIndexCache), "act_inventory_idle");
            var idleStartField = AccessTools.Field(typeof(ActionIndexCache), "act_inventory_idle_start");
            var fieldReplacements = 0;
            var stringReplacements = 0;

            foreach (var instruction in list)
            {
                if (idleField != null && idleStartField != null &&
                    instruction.opcode == OpCodes.Ldsfld &&
                    Equals(instruction.operand as FieldInfo, idleField))
                {
                    instruction.operand = idleStartField;
                    fieldReplacements++;
                    continue;
                }

                if (instruction.opcode == OpCodes.Ldstr &&
                    string.Equals(instruction.operand as string, "act_inventory_idle", StringComparison.Ordinal))
                {
                    instruction.operand = "act_inventory_idle_start";
                    stringReplacements++;
                }
            }

            var replacements = fieldReplacements + stringReplacements;
            PortraitFixLog.Event(
                "POSE_PATCH",
                "applied=" + (replacements > 0) +
                "; replacements=" + replacements +
                "; fieldReplacements=" + fieldReplacements +
                "; stringFallbackReplacements=" + stringReplacements +
                "; idleFieldFound=" + (idleField != null) +
                "; idleStartFieldFound=" + (idleStartField != null) +
                "; from=act_inventory_idle; to=act_inventory_idle_start");

            return list;
        }

        [HarmonyPrefix]
        internal static void Prefix(BasicCharacterTableau __instance, int ____race, string ____skeletonName)
        {
            var sample = Interlocked.Increment(ref _samples);
            if (sample > 24)
                return;

            try
            {
                var idle = ActionIndexCache.Create("act_inventory_idle").Index;
                var idleStart = ActionIndexCache.Create("act_inventory_idle_start").Index;
                var t = Traverse.Create(__instance);
                var frame = t.Field("_initialSpawnFrame").GetValue<MatrixFrame>();
                var mountFrame = t.Field("_mountSpawnPoint").GetValue<MatrixFrame>();

                PortraitFixLog.Event(
                    "POSE_REFRESH",
                    "sample=" + sample +
                    "; race=" + ____race +
                    "; skeleton=" + (____skeletonName ?? string.Empty) +
                    "; idleIndex=" + idle +
                    "; idleStartIndex=" + idleStart +
                    "; agentOrigin=" + Vec(frame.origin) +
                    "; agentUp=" + Vec(frame.rotation.u) +
                    "; agentForward=" + Vec(frame.rotation.f) +
                    "; mountOrigin=" + Vec(mountFrame.origin) +
                    "; mountUp=" + Vec(mountFrame.rotation.u));
            }
            catch (Exception ex)
            {
                PortraitFixLog.Event("POSE_REFRESH", "sample=" + sample + "; inspectError=" + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static string Vec(Vec3 value)
        {
            return value.x.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                   value.y.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                   value.z.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
