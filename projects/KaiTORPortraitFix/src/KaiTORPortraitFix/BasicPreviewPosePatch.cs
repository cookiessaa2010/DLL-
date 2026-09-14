using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Tableaus;

namespace KaiTORPortraitFix
{
    /// <summary>
    /// BasicCharacterTableau is the legacy Save/Load-only renderer. Unlike the normal
    /// CharacterTableau it creates a simplified skeleton and starts "act_inventory_idle".
    /// TOR characters are rendering as a fully textured but horizontal/prone model while
    /// the separately-rendered mount stays upright. The normal CharacterTableau uses
    /// "act_inventory_idle_start" as its default idle action.
    ///
    /// This experimental compatibility patch changes ONLY the Save/Load BasicCharacterTableau
    /// idle action from act_inventory_idle to act_inventory_idle_start. It does not modify
    /// saves, equipment, races, campaign visuals, or the normal CharacterTableau.
    /// </summary>
    [HarmonyPatch(typeof(BasicCharacterTableau), "RefreshCharacterTableau")]
    internal static class BasicPreviewPosePatch
    {
        private static int _samples;

        [HarmonyTranspiler]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            var replacements = 0;

            foreach (var instruction in list)
            {
                if (instruction.opcode == OpCodes.Ldstr &&
                    string.Equals(instruction.operand as string, "act_inventory_idle", StringComparison.Ordinal))
                {
                    instruction.operand = "act_inventory_idle_start";
                    replacements++;
                }
            }

            PortraitFixLog.Event(
                "POSE_PATCH",
                "applied=" + (replacements > 0) +
                "; replacements=" + replacements +
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
