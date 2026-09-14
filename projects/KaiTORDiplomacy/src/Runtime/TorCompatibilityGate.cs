using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace KaiTOR.Diplomacy.Runtime;

internal static class TorCompatibilityGate
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedModels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DiplomacyModel"] = "TOR_Core.Models.TORDiplomacyModel",
            ["AllianceModel"] = "TOR_Core.Models.TORAllianceModel",
            ["TradeAgreementModel"] = "TOR_Core.Models.TORTradeAgreementModel",
            ["MarriageModel"] = "TOR_Core.Models.TORMarriageModel",
            ["KingdomDecisionPermissionModel"] = "TOR_Core.CampaignMechanics.Diplomacy.TORKingdomDecisionPermissionModel",
        };

    public static bool TryValidate(out string reason)
    {
        var campaign = Campaign.Current;
        if (campaign?.Models == null)
        {
            reason = "Campaign models are not available yet.";
            return false;
        }

        var models = campaign.Models;
        var modelContainerType = models.GetType();

        foreach (var pair in ExpectedModels)
        {
            var property = modelContainerType.GetProperty(pair.Key);
            if (property == null)
            {
                reason = $"Bannerlord 1.3.15 model property '{pair.Key}' was not found.";
                return false;
            }

            var activeModel = property.GetValue(models);
            var actualType = activeModel?.GetType().FullName ?? "<null>";
            if (!string.Equals(actualType, pair.Value, StringComparison.Ordinal))
            {
                reason = $"TOR compatibility gate failed: {pair.Key} is '{actualType}', expected '{pair.Value}'.";
                return false;
            }
        }

        reason = "TOR diplomacy model stack verified.";
        return true;
    }
}
