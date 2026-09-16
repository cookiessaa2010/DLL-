using System.Collections.Generic;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.Library;

namespace KaiTOR.Diplomacy.Runtime;

public static class KaiMarriageCommands
{
    [CommandLineFunctionality.CommandLineArgumentFunction("marriage_status", "kaitor_diplomacy")]
    public static string MarriageStatus(List<string> arguments)
    {
        if (arguments.Count != 0)
            return "Usage: kaitor_diplomacy.marriage_status";
        if (Campaign.Current == null)
            return "No campaign is active.";

        var model = Campaign.Current.Models.MarriageModel;
        var wrapper = model as KaiPlayerMarriageModel;
        var romance = Campaign.Current.GetCampaignBehavior<RomanceCampaignBehavior>() != null;
        var offers = Campaign.Current.GetCampaignBehavior<MarriageOfferCampaignBehavior>() != null;

        return "Marriage runtime status:\n" +
               $"model={model?.GetType().FullName ?? "<null>"}\n" +
               $"TOR wrapper={(wrapper != null ? "ACTIVE" : "NO")}" +
               (wrapper != null ? $" (base={wrapper.UnderlyingModelTypeName})" : string.Empty) + "\n" +
               $"RomanceCampaignBehavior={(romance ? "LOADED" : "MISSING")}\n" +
               $"MarriageOfferCampaignBehavior={(offers ? "LOADED" : "MISSING")}\n" +
               $"lifeDeathCycleDisabled={CampaignOptions.IsLifeDeathCycleDisabled}\n" +
               $"Dawi women assets={(DawiWomenAssetBridge.IsAvailable ? "READY" : "SAFE-OFF")}.";
    }
}
