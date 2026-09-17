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
    // Existing TOR-save safety latch. Unsafe racial-population and permission overrides
    // remain OFF even while the ordinary Bannerlord lifecycle is restored.
    private const bool LoadSafeDiagnostics = true;

    // Restore Bannerlord/TOR's normal age progression, pregnancy/child growth and
    // natural-death lifecycle without enabling KaiTOR's uncertified population spawners.
    private const bool EnableLifecycleRestore = true;
    private const bool EnableLoreOldAgeMortality = true;
    private const bool EnableMarriageWarnings = true;

    // Dawi remain isolated from automatic population. The behavior is registered so
    // the console probe can create one test woman when the optional local assets are staged.
    private const bool EnableDawiWomenLiveTest = true;

    protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
    {
        base.OnGameStart(game, gameStarterObject);

        if (gameStarterObject is not CampaignGameStarter campaignStarter)
            return;

        // TOR disables ordinary marriage. Replace that model with our safe compatibility
        // layer, then guard every biological pregnancy path against unsupported races.
        InstallMarriageWrapper(campaignStarter);
        InstallPregnancyWrapper(campaignStarter);

        if (EnableLifecycleRestore)
            CampaignOptions.IsLifeDeathCycleDisabled = false;

        if (EnableLoreOldAgeMortality)
            InstallHeroDeathWrapper(campaignStarter);

        if (EnableMarriageWarnings)
            campaignStarter.AddBehavior(new KaiMarriageWarningBehavior());

        if (EnableDawiWomenLiveTest)
            campaignStarter.AddBehavior(new KaiDawiWomenBehavior());

        if (!LoadSafeDiagnostics)
        {
            // These paths are intentionally NOT enabled by v0.6.2. They remain behind
            // the safety latch until their world-mutation behavior is certified in game.
            InstallPermissionWrapper(campaignStarter);
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

        // New houses use the staged open-map native factory path from v0.6.1 CrashFix.
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
