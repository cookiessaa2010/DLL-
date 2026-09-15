using KaiTOR.Diplomacy.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Models;

/// <summary>
/// Additive wrapper over TOR's kingdom decision permission model.
/// TOR remains authoritative for every vanilla/TOR permission. KaiTOR only adds
/// a non-aggression-pact veto to ordinary kingdom war decisions.
/// Scripted wars that bypass kingdom decisions (Chaos/lore/event logic) remain untouched.
/// </summary>
public sealed class KaiKingdomDecisionPermissionModel : KingdomDecisionPermissionModel
{
    public const string ExpectedTorBaseType = "TOR_Core.CampaignMechanics.Diplomacy.TORKingdomDecisionPermissionModel";

    private readonly KingdomDecisionPermissionModel _torBase;

    public KaiKingdomDecisionPermissionModel(KingdomDecisionPermissionModel torBase)
    {
        _torBase = torBase;
    }

    public string UnderlyingModelTypeName => _torBase?.GetType().FullName ?? "<null>";

    public override bool IsPolicyDecisionAllowed(PolicyObject policy)
        => _torBase != null && _torBase.IsPolicyDecisionAllowed(policy);

    public override bool IsWarDecisionAllowedBetweenKingdoms(Kingdom kingdom1, Kingdom kingdom2, out TextObject reason)
    {
        if (_torBase == null)
        {
            reason = new TextObject("KaiTOR: TOR kingdom permission model is unavailable.");
            return false;
        }

        if (!_torBase.IsWarDecisionAllowedBetweenKingdoms(kingdom1, kingdom2, out reason))
            return false;

        var behavior = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
        if (behavior != null && behavior.RuntimeEnabled && behavior.IsNonAggressionPactActive(kingdom1, kingdom2))
        {
            var days = behavior.GetRemainingDays(kingdom1, kingdom2);
            reason = new TextObject($"KaiTOR: a non-aggression pact is active for another {days} day(s).");
            return false;
        }

        return true;
    }

    public override bool IsPeaceDecisionAllowedBetweenKingdoms(Kingdom kingdom1, Kingdom kingdom2, out TextObject reason)
    {
        if (_torBase == null)
        {
            reason = new TextObject("KaiTOR: TOR kingdom permission model is unavailable.");
            return false;
        }
        return _torBase.IsPeaceDecisionAllowedBetweenKingdoms(kingdom1, kingdom2, out reason);
    }

    public override bool IsStartAllianceDecisionAllowedBetweenKingdoms(Kingdom kingdom1, Kingdom kingdom2, out TextObject reason)
    {
        if (_torBase == null)
        {
            reason = new TextObject("KaiTOR: TOR kingdom permission model is unavailable.");
            return false;
        }
        return _torBase.IsStartAllianceDecisionAllowedBetweenKingdoms(kingdom1, kingdom2, out reason);
    }

    public override bool IsAnnexationDecisionAllowed(Settlement annexedSettlement)
        => _torBase != null && _torBase.IsAnnexationDecisionAllowed(annexedSettlement);

    public override bool IsExpulsionDecisionAllowed(Clan expelledClan)
        => _torBase != null && _torBase.IsExpulsionDecisionAllowed(expelledClan);

    public override bool IsKingSelectionDecisionAllowed(Kingdom kingdom)
        => _torBase != null && _torBase.IsKingSelectionDecisionAllowed(kingdom);
}
