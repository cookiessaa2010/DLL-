using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Raises Bannerlord's native relation reward for deliberately releasing a defeated lord.
/// No extra relation event is created: the original ApplyPlayerRelation call remains the
/// only mechanic, with its native clan/relative propagation and notification behavior.
/// </summary>
internal static class KaiMercyRelationPatch
{
    public const int NativeReleaseRelation = 4;
    public const int ReleaseRelation = 50;

    [HarmonyPatch(typeof(LordConversationsCampaignBehavior), "conversation_talk_lord_defeat_to_lord_release_on_consequence")]
    private static class DefeatedLordReleasePatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => ReplaceNativeReleaseRelation(instructions, "post_battle");
    }

    [HarmonyPatch(typeof(LordConversationsCampaignBehavior), "conversation_talk_lord_freed_to_lord_release_on_consequence")]
    private static class FreedLordReleasePatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => ReplaceNativeReleaseRelation(instructions, "release_by_choice");
    }

    private static IEnumerable<CodeInstruction> ReplaceNativeReleaseRelation(
        IEnumerable<CodeInstruction> instructions,
        string route)
    {
        var replaced = false;

        foreach (var instruction in instructions)
        {
            if (!replaced && LoadsInt32(instruction, NativeReleaseRelation))
            {
                instruction.opcode = OpCodes.Ldc_I4;
                instruction.operand = ReleaseRelation;
                replaced = true;
            }

            yield return instruction;
        }

        if (replaced)
            KaiRuntimeLog.Write("MERCY_RELATION_PATCH", $"route={route}; native={NativeReleaseRelation}; replacement={ReleaseRelation}");
        else
            KaiRuntimeLog.Write("MERCY_RELATION_PATCH_FAILED", $"route={route}; native_constant_not_found={NativeReleaseRelation}");
    }

    private static bool LoadsInt32(CodeInstruction instruction, int value)
    {
        if (instruction == null)
            return false;

        if (value == -1 && instruction.opcode == OpCodes.Ldc_I4_M1) return true;
        if (value == 0 && instruction.opcode == OpCodes.Ldc_I4_0) return true;
        if (value == 1 && instruction.opcode == OpCodes.Ldc_I4_1) return true;
        if (value == 2 && instruction.opcode == OpCodes.Ldc_I4_2) return true;
        if (value == 3 && instruction.opcode == OpCodes.Ldc_I4_3) return true;
        if (value == 4 && instruction.opcode == OpCodes.Ldc_I4_4) return true;
        if (value == 5 && instruction.opcode == OpCodes.Ldc_I4_5) return true;
        if (value == 6 && instruction.opcode == OpCodes.Ldc_I4_6) return true;
        if (value == 7 && instruction.opcode == OpCodes.Ldc_I4_7) return true;
        if (value == 8 && instruction.opcode == OpCodes.Ldc_I4_8) return true;

        if (instruction.opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte shortValue)
            return shortValue == value;

        if (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int intValue)
            return intValue == value;

        return false;
    }
}
