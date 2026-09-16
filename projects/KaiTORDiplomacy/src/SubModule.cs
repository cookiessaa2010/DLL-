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
    // Diagnostic live-test latch for existing TOR saves.
    // The first v0.3.0 live test reached the end of Bannerlord save deserialization,
    // then crashed while the campaign map was being initialized. To isolate the
    // highest-risk startup group, this build temporarily leaves TOR's lifecycle and
    // family/death models untouched while keeping KaiTOR campaign behaviors loaded.
    private const bool LoadSafeDiagnostics = true;

    protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
    {
        base.OnGameStart(game, gameStarterObject);

        if (gameStarterObject is not CampaignGameStarter campaignStarter)
            return;

        if (!LoadSafeDiagnostics)
        {
            // TOR intentionally freezes Bannerlord's life/death cycle. The normal
            // KaiTOR path restores it for age, children, dynasties and succession.
            CampaignOptions.IsLifeDeathCycleDisabled = false;

            InstallPermissionWrapper(campaignStarter);
            InstallMarriageWrapper(campaignStarter);
            InstallPregnancyWrapper(campaignStarter);
            InstallHeroDeathWrapper(campaignStarter);
        }

        campaignStarter.AddBehavior(new KaiDiplomacyBehavior());
        campaignStarter.AddBehavior(new KaiDiplomacyOfficeBehavior());
        campaignStarter.AddBehavior(new KaiDiplomacyAiBehavior());
        campaignStarter.AddBehavior(new KaiCultureAssimilationBehavior());
        campaignStarter.AddBehavior(new KaiMarriageWarningBehavior());
        campaignStarter.AddBehavior(new KaiRacialPopulationBehavior());
        campaignStarter.AddBehavior(new KaiDawiWomenBehavior());
        campaignStarter.AddBehavior(new KaiDynastyAiBehavior());
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
