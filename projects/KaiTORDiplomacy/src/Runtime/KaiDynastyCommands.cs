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

        var recruitment = Campaign.Current.GetCampaignBehavior<KaiDynastyAiBehavior>();
        var realmGrowth = Campaign.Current.GetCampaignBehavior<KaiRealmHouseGrowthBehavior>();
        if (recruitment == null)
            return "Dynasty AI behavior is not loaded.";

        var lines = recruitment.DescribeStatus().ToList();
        if (realmGrowth != null)
            lines.AddRange(realmGrowth.DescribeStatus());

        return lines.Count == 0
            ? "No active kingdoms found."
            : "Kingdom clan-growth status:\n" + string.Join("\n", lines);
    }
}
