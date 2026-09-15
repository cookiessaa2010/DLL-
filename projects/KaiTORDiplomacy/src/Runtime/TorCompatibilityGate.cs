using System;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;

namespace KaiTOR.Diplomacy.Runtime;

internal static class TorCompatibilityGate
{
    private const string ExpectedDiplomacy = "TOR_Core.Models.TORDiplomacyModel";
    private const string ExpectedAlliance = "TOR_Core.Models.TORAllianceModel";
    private const string ExpectedTrade = "TOR_Core.Models.TORTradeAgreementModel";

    public static bool TryValidate(out string reason)
    {
        var models = Campaign.Current?.Models;
        if (models == null)
        {
            reason = "Campaign models are not available yet.";
            return false;
        }

        if (!HasExactType(models.DiplomacyModel, ExpectedDiplomacy, "DiplomacyModel", out reason))
            return false;
        if (!HasExactType(models.AllianceModel, ExpectedAlliance, "AllianceModel", out reason))
            return false;
        if (!HasExactType(models.TradeAgreementModel, ExpectedTrade, "TradeAgreementModel", out reason))
            return false;

        if (models.MarriageModel is not KaiPlayerMarriageModel marriage)
        {
            reason = $"TOR compatibility gate failed: MarriageModel is '{models.MarriageModel?.GetType().FullName ?? "<null>"}', expected KaiTOR player-only wrapper.";
            return false;
        }
        if (!string.Equals(marriage.UnderlyingModelTypeName, KaiPlayerMarriageModel.ExpectedTorBaseType, StringComparison.Ordinal))
        {
            reason = $"TOR compatibility gate failed: KaiTOR marriage wrapper base is '{marriage.UnderlyingModelTypeName}'.";
            return false;
        }

        if (models.KingdomDecisionPermissionModel is not KaiKingdomDecisionPermissionModel permissions)
        {
            reason = $"TOR compatibility gate failed: KingdomDecisionPermissionModel is '{models.KingdomDecisionPermissionModel?.GetType().FullName ?? "<null>"}', expected KaiTOR additive wrapper.";
            return false;
        }
        if (!string.Equals(permissions.UnderlyingModelTypeName, KaiKingdomDecisionPermissionModel.ExpectedTorBaseType, StringComparison.Ordinal))
        {
            reason = $"TOR compatibility gate failed: KaiTOR permission wrapper base is '{permissions.UnderlyingModelTypeName}'.";
            return false;
        }

        reason = "TOR diplomacy stack verified with KaiTOR additive wrappers.";
        return true;
    }

    private static bool HasExactType(object model, string expectedType, string slot, out string reason)
    {
        var actualType = model?.GetType().FullName ?? "<null>";
        if (!string.Equals(actualType, expectedType, StringComparison.Ordinal))
        {
            reason = $"TOR compatibility gate failed: {slot} is '{actualType}', expected '{expectedType}'.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
