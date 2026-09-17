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
    // Existing TOR-save safety latch. Race-wide population, permission/death overrides
    // and other uncertified world mutations remain OFF during the first global test.
    private const bool LoadSafeDiagnostics = true;

    // Dawi are present for one-woman manual rig validation. Automatic Dawi population
    // remains disabled inside KaiDawiWomenBehavior until the real female rig passes.
    private const bool EnableDawiWomenLiveTest = true;

    protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
    {
        base.OnGameStart(game, gameStarterObject);

        if (gameStarterObject is not CampaignGameStarter campaignStarter)
            return;

        InstallMarriageWrapper(campaignStarter);

        if (EnableDawiWomenLiveTest)
        {
            InstallPregnancyWrapper(campaignStarter);
            campaignStarter.AddBehavior(new KaiDawiWomenBehavior());
        }

        // Safe player-facing systems used by the global test.
        campaignStarter.AddBehavior(new KaiDiplomacyBehavior());
        campaignStarter.AddBehavior(new KaiPoliticalMarriageBehavior());
        campaignStarter.AddBehavior(new KaiDiplomaticNegotiationBehavior());
        campaignStarter.AddBehavior(new KaiDiplomacyAiBehavior());
        campaignStarter.AddBehavior(new KaiCultureAssimilationBehavior());
        campaignStarter.AddBehavior(new KaiMarriageWarningBehavior());
        campaignStarter.AddBehavior(new KaiDynastyAiBehavior());
        campaignStarter.AddBehavior(new KaiMercyRelationBehavior());
        campaignStarter.AddBehavior(new KaiRealmHouseGrowthBehavior());

        // Intentionally NOT registered for this test:
        // - KaiDiplomacyOfficeBehavior: removed from town/castle UI by design.
        // - KaiFamilyAffairsBehavior: removed from town/castle UI until it has a native clan entry.
        // - KaiRacialPopulationBehavior and lifecycle/death overrides: still behind safety latch.
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
            campaignStarter.AddBehavior(new KaiRacialPopulationBehavior());
        }
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
