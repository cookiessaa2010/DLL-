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

        if (!HasTorMarriageOwnership(models.MarriageModel, out reason))
            return false;
        if (!HasTorPermissionOwnership(models.KingdomDecisionPermissionModel, out reason))
            return false;

        reason = "TOR diplomacy stack verified. LoadSafe accepts TOR-owned family/permission models; KaiTOR wrappers are also accepted when enabled.";
        return true;
    }

    private static bool HasTorMarriageOwnership(object model, out string reason)
    {
        var actualType = model?.GetType().FullName ?? "<null>";

        // LoadSafe diagnostics deliberately leave TOR's marriage model untouched.
        if (string.Equals(actualType, KaiPlayerMarriageModel.ExpectedTorBaseType, StringComparison.Ordinal))
        {
            reason = string.Empty;
            return true;
        }

        // Future full mode may install the additive KaiTOR wrapper over that exact TOR model.
        if (model is KaiPlayerMarriageModel marriage &&
            string.Equals(marriage.UnderlyingModelTypeName, KaiPlayerMarriageModel.ExpectedTorBaseType, StringComparison.Ordinal))
        {
            reason = string.Empty;
            return true;
        }

        reason = $"TOR compatibility gate failed: MarriageModel is '{actualType}', expected TOR model '{KaiPlayerMarriageModel.ExpectedTorBaseType}' or its KaiTOR wrapper.";
        return false;
    }

    private static bool HasTorPermissionOwnership(object model, out string reason)
    {
        var actualType = model?.GetType().FullName ?? "<null>";

        // LoadSafe diagnostics deliberately leave TOR's permission model untouched.
        if (string.Equals(actualType, KaiKingdomDecisionPermissionModel.ExpectedTorBaseType, StringComparison.Ordinal))
        {
            reason = string.Empty;
            return true;
        }

        // Future full mode may install the additive KaiTOR wrapper over that exact TOR model.
        if (model is KaiKingdomDecisionPermissionModel permissions &&
            string.Equals(permissions.UnderlyingModelTypeName, KaiKingdomDecisionPermissionModel.ExpectedTorBaseType, StringComparison.Ordinal))
        {
            reason = string.Empty;
            return true;
        }

        reason = $"TOR compatibility gate failed: KingdomDecisionPermissionModel is '{actualType}', expected TOR model '{KaiKingdomDecisionPermissionModel.ExpectedTorBaseType}' or its KaiTOR wrapper.";
        return false;
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
