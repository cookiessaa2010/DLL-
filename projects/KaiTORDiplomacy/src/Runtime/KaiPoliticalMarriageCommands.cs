using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace KaiTOR.Diplomacy.Runtime;

public static class KaiPoliticalMarriageCommands
{
    [CommandLineFunctionality.CommandLineArgumentFunction("dynastic_bonds", "kaitor_diplomacy")]
    public static string DynasticBonds(List<string> arguments)
    {
        if (arguments.Count != 0)
            return "Usage: kaitor_diplomacy.dynastic_bonds";
        if (Campaign.Current == null)
            return "No campaign is active.";

        var behavior = Campaign.Current.GetCampaignBehavior<KaiPoliticalMarriageBehavior>();
        if (behavior == null)
            return "Political marriage behavior is not loaded.";

        var bonds = behavior.DescribeActiveBonds().ToArray();
        return bonds.Length == 0
            ? "Dynastic bonds: none."
            : "Dynastic bonds:\n" + string.Join("\n", bonds);
    }
}
