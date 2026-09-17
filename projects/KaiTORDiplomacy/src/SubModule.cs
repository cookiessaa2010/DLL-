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
    // Existing TOR-save safety latch. Racial population, permission/death overrides
    // and other uncertified world mutations remain OFF.
    private const bool LoadSafeDiagnostics = true;

    // Dawi v0.5.2+ remains isolated from automatic population. The behavior is
    // registered so the console probe can create one test woman after local assets are
    // staged, while KaiDawiWomenBehavior itself keeps automatic population disabled.
    private const bool EnableDawiWomenLiveTest = true;

    protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
    {
        base.OnGameStart(game, gameStarterObject);

        if (gameStarterObject is not CampaignGameStarter campaignStarter)
            return;

        InstallMarriageWrapper(campaignStarter);

        if (EnableDawiWomenLiveTest)
        {
            // The wrapper delegates normal valid pairs to TOR/Bannerlord and only
            // fail-closes races that are unsafe for native DeliverOffSpring.
            InstallPregnancyWrapper(campaignStarter);
            campaignStarter.AddBehavior(new KaiDawiWomenBehavior());
        }

        if (!LoadSafeDiagnostics)
        {
            CampaignOptions.IsLifeDeathCycleDisabled = false;

            InstallPermissionWrapper(campaignStarter);
            if (!EnableDawiWomenLiveTest)
            {
                InstallPregnancyWrapper(campaignStarter);
                campaignStarter.AddBehavior(new KaiDawiWomenBehavior());
            }
            InstallHeroDeathWrapper(campaignStarter);

            campaignStarter.AddBehavior(new KaiMarriageWarningBehavior());
            campaignStarter.AddBehavior(new KaiRacialPopulationBehavior());
        }

        // LoadSafe runtime systems.
        campaignStarter.AddBehavior(new KaiDiplomacyBehavior());
        campaignStarter.AddBehavior(new KaiDiplomacyOfficeBehavior());
        campaignStarter.AddBehavior(new KaiDiplomacyAiBehavior());
        campaignStarter.AddBehavior(new KaiCultureAssimilationBehavior());
        campaignStarter.AddBehavior(new KaiFamilyAffairsBehavior());
        campaignStarter.AddBehavior(new KaiDynastyAiBehavior());
        campaignStarter.AddBehavior(new KaiMercyRelationBehavior());

        // v0.5.3: new houses are restored through a staged open-map path. WeeklyTick
        // only selects a candidate; the clan graph changes later on HourlyTick while
        // the player is on the open campaign map. No settlement-entry mutation.
        campaignStarter.AddBehavior(new KaiRealmHouseGrowthBehavior());
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
        if (torModel is KaiPlayerMarriageModel)
            return;
        if (!string.Equals(torModel?.GetType().FullName, KaiPlayerMarriageModel.ExpectedTorBaseType, StringComparison.Ordinal))
            return;

        starter.AddModel<MarriageModel>(new KaiPlayerMarriageModel(torModel));
    }

    private static void InstallPregnancyWrapper(CampaignGameStarter starter)
    {
        var activeModel = starter.GetModel<PregnancyModel>();
        if (activeModel == null || activeModel is KaiPregnancyModel)
            return;

        starter.AddModel<PregnancyModel>(new KaiPregnancyModel(activeModel));
    }

    private static void InstallHeroDeathWrapper(CampaignGameStarter starter)
    {
        var activeModel = starter.GetModel<HeroDeathProbabilityCalculationModel>();
        if (activeModel == null || activeModel is KaiHeroDeathProbabilityModel)
            return;

        starter.AddModel<HeroDeathProbabilityCalculationModel>(new KaiHeroDeathProbabilityModel(activeModel));
    }
}
