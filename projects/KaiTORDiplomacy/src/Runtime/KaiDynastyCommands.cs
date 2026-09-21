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

        var dawi = Campaign.Current.GetCampaignBehavior<KaiDawiDynastyBehavior>();
        if (dawi == null)
            return "Dawi abstract dynasty behavior is not loaded.";

        return string.Join("\n", dawi.DescribeStatus());
    }
}
