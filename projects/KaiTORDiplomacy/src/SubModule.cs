using System;
using KaiTOR.Diplomacy.Models;
using KaiTOR.Diplomacy.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace KaiTOR.Diplomacy;

public sealed class SubModule : MBSubModuleBase
{
    protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
    {
        base.OnGameStart(game, gameStarterObject);

        if (gameStarterObject is not CampaignGameStarter campaignStarter)
            return;

        InstallPermissionWrapper(campaignStarter);
        InstallMarriageWrapper(campaignStarter);

        campaignStarter.AddBehavior(new KaiDiplomacyBehavior());
        campaignStarter.AddBehavior(new KaiCultureAssimilationBehavior());
    }

    private static void InstallPermissionWrapper(CampaignGameStarter starter)
    {
        var torModel = starter.GetModel<KingdomDecisionPermissionModel>();
        if (!string.Equals(torModel?.GetType().FullName, KaiKingdomDecisionPermissionModel.ExpectedTorBaseType, StringComparison.Ordinal))
            return;

        starter.AddModel<KingdomDecisionPermissionModel>(new KaiKingdomDecisionPermissionModel(torModel));
    }

    private static void InstallMarriageWrapper(CampaignGameStarter starter)
    {
        var torModel = starter.GetModel<MarriageModel>();
        if (!string.Equals(torModel?.GetType().FullName, KaiPlayerMarriageModel.ExpectedTorBaseType, StringComparison.Ordinal))
            return;

        starter.AddModel<MarriageModel>(new KaiPlayerMarriageModel(torModel));
    }
}
