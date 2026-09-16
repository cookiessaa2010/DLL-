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
    // LoadSafe blocks pregnancy/death/racial hero spawning, custom courtship hooks and
    // automatic runtime Clan.CreateClan mutations until each path is certified in TOR.
    private const bool LoadSafeDiagnostics = true;

    protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
    {
        base.OnGameStart(game, gameStarterObject);

        if (gameStarterObject is not CampaignGameStarter campaignStarter)
            return;

        InstallMarriageWrapper(campaignStarter);

        if (!LoadSafeDiagnostics)
        {
            CampaignOptions.IsLifeDeathCycleDisabled = false;

            InstallPermissionWrapper(campaignStarter);
            InstallPregnancyWrapper(campaignStarter);
            InstallHeroDeathWrapper(campaignStarter);

            campaignStarter.AddBehavior(new KaiMarriageWarningBehavior());
            campaignStarter.AddBehavior(new KaiRacialPopulationBehavior());
            campaignStarter.AddBehavior(new KaiDawiWomenBehavior());

            // Retained for future controlled certification, but deliberately not
            // registered in LoadSafe after a native access violation appeared during
            // live world progression with runtime-created houses enabled.
            campaignStarter.AddBehavior(new KaiCadetHouseSafeBehavior());
        }

        // LoadSafe runtime systems. AI kingdoms still grow by recruiting existing
        // noble clans through Bannerlord's native barter path. Family adoption acts
        // only on heroes already attached to the player's own house.
        campaignStarter.AddBehavior(new KaiDiplomacyBehavior());
        campaignStarter.AddBehavior(new KaiDiplomacyOfficeBehavior());
        campaignStarter.AddBehavior(new KaiDiplomacyAiBehavior());
        campaignStarter.AddBehavior(new KaiCultureAssimilationBehavior());
        campaignStarter.AddBehavior(new KaiFamilyAffairsBehavior());
        campaignStarter.AddBehavior(new KaiDynastyAiBehavior());
        campaignStarter.AddBehavior(new KaiMercyRelationBehavior());
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
