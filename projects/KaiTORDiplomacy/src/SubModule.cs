using System;
using HarmonyLib;
using KaiTOR.Diplomacy.Models;
using KaiTOR.Diplomacy.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace KaiTOR.Diplomacy;

public sealed class SubModule : MBSubModuleBase
{
    private const bool LoadSafeDiagnostics = true;
    private const bool EnableLifecycleRestore = true;
    private const bool EnableLoreOldAgeMortality = true;
    private const bool EnableMarriageWarnings = true;
    private const bool EnableDawiWomenLiveTest = true;

    private Harmony _harmony;

    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        try
        {
            _harmony = new Harmony("kaitor.diplomacy.kingdom-ui");
            _harmony.PatchAll(typeof(SubModule).Assembly);
        }
        catch
        {
            // Never prevent the campaign from loading if an optional UI patch cannot be installed.
        }
    }

    protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
    {
        base.OnGameStart(game, gameStarterObject);
        if (gameStarterObject is not CampaignGameStarter campaignStarter) return;

        // Family lifecycle: native Bannerlord maturation stays intact; TOR remains the base model stack.
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

        // Safe additive diplomacy permission layer. It delegates every TOR rule and only vetoes
        // ordinary war proposals while an active non-aggression pact exists.
        InstallPermissionWrapper(campaignStarter);

        // Dangerous automatic population generation remains isolated behind the safety latch.
        if (!LoadSafeDiagnostics)
            campaignStarter.AddBehavior(new KaiRacialPopulationBehavior());

        campaignStarter.AddBehavior(new KaiDiplomacyBehavior());
        campaignStarter.AddBehavior(new KaiDiplomacyAiBehavior());
        campaignStarter.AddBehavior(new KaiCultureAssimilationBehavior());
        campaignStarter.AddBehavior(new KaiFamilyAffairsBehavior());
        campaignStarter.AddBehavior(new KaiLoreEducationBehavior());
        campaignStarter.AddBehavior(new KaiDynastyAiBehavior());
        campaignStarter.AddBehavior(new KaiMercyRelationBehavior());
        campaignStarter.AddBehavior(new KaiRealmHouseGrowthBehavior());
    }

    private static void InstallPermissionWrapper(CampaignGameStarter starter)
    {
        var torModel = starter.GetModel<KingdomDecisionPermissionModel>();
        if (torModel is KaiKingdomDecisionPermissionModel) return;
        if (!string.Equals(torModel?.GetType().FullName, KaiKingdomDecisionPermissionModel.ExpectedTorBaseType, StringComparison.Ordinal)) return;
        starter.AddModel<KingdomDecisionPermissionModel>(new KaiKingdomDecisionPermissionModel(torModel));
    }

    private static void InstallMarriageWrapper(CampaignGameStarter starter)
    {
        var torModel = starter.GetModel<MarriageModel>();
        if (torModel is KaiPlayerMarriageModel) return;
        if (!string.Equals(torModel?.GetType().FullName, KaiPlayerMarriageModel.ExpectedTorBaseType, StringComparison.Ordinal)) return;
        starter.AddModel<MarriageModel>(new KaiPlayerMarriageModel(torModel));
    }

    private static void InstallPregnancyWrapper(CampaignGameStarter starter)
    {
        var activeModel = starter.GetModel<PregnancyModel>();
        if (activeModel == null || activeModel is KaiPregnancyModel) return;
        starter.AddModel<PregnancyModel>(new KaiPregnancyModel(activeModel));
    }

    private static void InstallHeroDeathWrapper(CampaignGameStarter starter)
    {
        var activeModel = starter.GetModel<HeroDeathProbabilityCalculationModel>();
        if (activeModel == null || activeModel is KaiHeroDeathProbabilityModel) return;
        starter.AddModel<HeroDeathProbabilityCalculationModel>(new KaiHeroDeathProbabilityModel(activeModel));
    }
}
