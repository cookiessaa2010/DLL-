using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace KaiTOR.Diplomacy.Runtime;

internal static class KaiTorDiplomacyModifiers
{
    public const string TorDiplomacyModelType =
        "TOR_Core.Models.TORDiplomacyModel";

    public static void ApplyPeaceExhaustionModifier(
        IFaction source,
        IFaction target,
        ref float result,
        string path)
    {
        if (result <= -50000f ||
            float.IsNaN(result) ||
            float.IsInfinity(result) ||
            source is not Kingdom side ||
            target is not Kingdom enemy ||
            !side.IsAtWarWith(enemy))
            return;

        var behavior = Campaign.Current?.GetCampaignBehavior<KaiWarExhaustionBehavior>();
        if (behavior == null)
            return;

        var modifier = behavior.GetPeacePressureModifier(side, enemy);
        if (modifier <= 0.001f)
            return;

        result += modifier;
        KaiRuntimeLog.Write(
            "WAR_EXHAUSTION_PEACE_MODIFIER",
            $"side={side.StringId}; enemy={enemy.StringId}; path={path}; exhaustion={behavior.GetExhaustion(side, enemy):0.0}; modifier={modifier:0.0}; score={result:0.0}");
    }
}

[HarmonyPatch]
internal static class KaiTorPeaceScorePatch
{
    private static MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName(KaiTorDiplomacyModifiers.TorDiplomacyModelType);
        return type == null
            ? null
            : AccessTools.Method(
                type,
                "GetScoreOfDeclaringPeace",
                new[] { typeof(IFaction), typeof(IFaction) });
    }

    private static void Postfix(
        IFaction factionDeclaresPeace,
        IFaction factionDeclaredPeace,
        ref float __result)
    {
        KaiTorDiplomacyModifiers.ApplyPeaceExhaustionModifier(
            factionDeclaresPeace,
            factionDeclaredPeace,
            ref __result,
            "TOR.GetScoreOfDeclaringPeace");
    }
}

[HarmonyPatch]
internal static class KaiTorPeaceForClanScorePatch
{
    private static MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName(KaiTorDiplomacyModifiers.TorDiplomacyModelType);
        if (type == null)
            return null;

        return AccessTools.Method(
            type,
            "GetScoreOfDeclaringPeaceForClan",
            new[]
            {
                typeof(IFaction),
                typeof(IFaction),
                typeof(Clan),
                typeof(TaleWorlds.Localization.TextObject).MakeByRefType(),
                typeof(bool)
            });
    }

    private static void Postfix(
        IFaction factionDeclaresPeace,
        IFaction factionDeclaredPeace,
        ref float __result)
    {
        KaiTorDiplomacyModifiers.ApplyPeaceExhaustionModifier(
            factionDeclaresPeace,
            factionDeclaredPeace,
            ref __result,
            "TOR.GetScoreOfDeclaringPeaceForClan");
    }
}
