using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Observes TOR's own private leave-service path. The prefix does not alter arguments or
/// return flow; it only records whether TOR itself will treat the departure as desertion.
/// </summary>
[HarmonyPatch]
internal static class KaiTorHirelingLeaveObserverPatch
{
    private static MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName(
            "TOR_Core.CampaignMechanics.ServeAsAHireling.ServeAsAHirelingCampaignBehavior");

        return type == null
            ? null
            : AccessTools.Method(
                type,
                "LeaveEnlistingParty",
                new[] { typeof(string), typeof(bool) });
    }

    private static void Prefix(bool desertion)
    {
        var duration = TorHirelingBridge.GetDurationDays();
        var torTreatsAsDesertion = desertion || duration < 25f;

        Campaign.Current?
            .GetCampaignBehavior<KaiServiceRecordBehavior>()?
            .MarkLeavingService(torTreatsAsDesertion);
    }
}
