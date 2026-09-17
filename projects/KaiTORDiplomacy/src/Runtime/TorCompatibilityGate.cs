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

        reason = "TOR diplomacy stack verified. Marriage may be TOR-owned directly or wrapped by the native-marriage compatibility layer; permission ownership remains TOR-safe.";
        return true;
    }

    private static bool HasTorMarriageOwnership(object model, out string reason)
    {
        var actualType = model?.GetType().FullName ?? "<null>";

        // Safe fallback: untouched TOR marriage ownership is accepted.
        if (string.Equals(actualType, KaiPlayerMarriageModel.ExpectedTorBaseType, StringComparison.Ordinal))
        {
            reason = string.Empty;
            return true;
        }

        // v0.4.5 LoadSafe selectively installs the additive marriage wrapper over the
        // exact TOR model while leaving pregnancy/death/lifecycle wrappers disabled.
        if (model is KaiPlayerMarriageModel marriage &&
            string.Equals(marriage.UnderlyingModelTypeName, KaiPlayerMarriageModel.ExpectedTorBaseType, StringComparison.Ordinal))
        {
            reason = string.Empty;
            return true;
        }

        reason = $"TOR compatibility gate failed: MarriageModel is '{actualType}', expected TOR model '{KaiPlayerMarriageModel.ExpectedTorBaseType}' or its native-marriage compatibility wrapper.";
        return false;
    }

    private static bool HasTorPermissionOwnership(object model, out string reason)
    {
        var actualType = model?.GetType().FullName ?? "<null>";

        if (string.Equals(actualType, KaiKingdomDecisionPermissionModel.ExpectedTorBaseType, StringComparison.Ordinal))
        {
            reason = string.Empty;
            return true;
        }

        if (model is KaiKingdomDecisionPermissionModel permissions &&
            string.Equals(permissions.UnderlyingModelTypeName, KaiKingdomDecisionPermissionModel.ExpectedTorBaseType, StringComparison.Ordinal))
        {
            reason = string.Empty;
            return true;
        }

        reason = $"TOR compatibility gate failed: KingdomDecisionPermissionModel is '{actualType}', expected TOR model '{KaiKingdomDecisionPermissionModel.ExpectedTorBaseType}' or its compatibility wrapper.";
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
