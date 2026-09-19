using System.Collections.Generic;
using System.Linq;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace KaiTOR.Diplomacy.Runtime;

public static class KaiDawiWomenCommands
{
    [CommandLineFunctionality.CommandLineArgumentFunction("status", "kaitor_dawi_women")]
    public static string Status(List<string> arguments)
    {
        if (arguments.Count != 0)
            return "Usage: kaitor_dawi_women.status";
        if (Campaign.Current == null)
            return "No campaign is active.";

        var behavior = Campaign.Current.GetCampaignBehavior<KaiDawiWomenBehavior>();
        var forcedOff = DawiWomenAssetBridge.ForceSafeOffForLiveTest;
        var lines = new List<string>
        {
            $"KaiTOR Dawi Women mode: {(forcedOff ? "FORCED SAFE-OFF" : (DawiWomenAssetBridge.IsAvailable ? "READY" : "MISSING/SAFE-OFF"))}",
            $"Population behavior: {(behavior == null ? "NOT REGISTERED" : "REGISTERED")}",
            $"Automatic population: {(KaiDawiWomenBehavior.AutomaticPopulationEnabled ? "ON" : "OFF - MANUAL RIG TEST")}",
            "Pregnancy policy: " + (DawiWomenAssetBridge.IsAvailable
                ? "same-race Dawi pairs may use the guarded normal pregnancy pipeline"
                : "Dawi pregnancy is blocked")
        };

        if (behavior != null)
            lines.AddRange(behavior.DescribeStatus().Skip(2));

        return KaiConsoleText.Safe(string.Join("\n", lines));
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("spawn_test", "kaitor_dawi_women")]
    public static string SpawnTest(List<string> arguments)
    {
        if (arguments.Count != 0)
            return "Usage: kaitor_dawi_women.spawn_test";
        if (Campaign.Current == null)
            return "No campaign is active.";

        var behavior = Campaign.Current.GetCampaignBehavior<KaiDawiWomenBehavior>();
        if (behavior == null)
            return "KaiDawiWomenBehavior is not registered.";

        return KaiConsoleText.Safe(behavior.SpawnOneForLiveTest());
    }
}
