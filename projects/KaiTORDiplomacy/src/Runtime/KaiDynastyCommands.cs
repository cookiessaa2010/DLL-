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

        var realmGrowth = Campaign.Current.GetCampaignBehavior<KaiRealmHouseGrowthBehavior>();
        if (realmGrowth == null)
            return "Realm-house growth behavior is not loaded.";

        var lines = realmGrowth.DescribeStatus().ToList();
        return lines.Count == 0
            ? "No active realm-house state found."
            : "Realm-house growth status:\n" + string.Join("\n", lines);
    }
}
