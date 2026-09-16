using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace KaiTOR.Diplomacy.Runtime;

public static class KaiDynastyCommands
{
    [CommandLineFunctionality.CommandLineArgumentFunction("dynasty_status", "kaitor_diplomacy")]
    public static string DynastyStatus(List<string> arguments)
    {
        if (arguments.Count != 0)
            return "Usage: kaitor_diplomacy.dynasty_status";
        if (Campaign.Current == null)
            return "No campaign is active.";

        var behavior = Campaign.Current.GetCampaignBehavior<KaiDynastyAiBehavior>();
        if (behavior == null)
            return "Dynasty AI behavior is not loaded.";

        var lines = behavior.DescribeStatus().ToArray();
        return lines.Length == 0
            ? "No active kingdoms found."
            : "Kingdom clan-growth status:\n" + string.Join("\n", lines);
    }
}
