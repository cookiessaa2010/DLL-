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
    // LoadSafe must not install lifecycle/family model wrappers, conversation hooks,
    // or autonomous hero/racial-population mutation systems.
    private const bool LoadSafeDiagnostics = true;

    protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
    {
        base.OnGameStart(game, gameStarterObject);

        if (gameStarterObject is not CampaignGameStarter campaignStarter)
            return;

        if (!LoadSafeDiagnostics)
        {
            // TOR intentionally freezes Bannerlord's life/death cycle. The normal
            // full-family path restores it for age, children, dynasties and succession.
            CampaignOptions.IsLifeDeathCycleDisabled = false;

            InstallPermissionWrapper(campaignStarter);
            InstallMarriageWrapper(campaignStarter);
            InstallPregnancyWrapper(campaignStarter);
            InstallHeroDeathWrapper(campaignStarter);

            // These behaviors mutate the family/racial hero graph and therefore stay
            // completely outside the live LoadSafe branch until separately certified.
            campaignStarter.AddBehavior(new KaiMarriageWarningBehavior());
            campaignStarter.AddBehavior(new KaiRacialPopulationBehavior());
            campaignStarter.AddBehavior(new KaiDawiWomenBehavior());
        }

        // LoadSafe runtime: treaties/UI/culture conversion plus conservative ruler AI.
        // KaiDynastyAiBehavior remains loaded, but in this branch it is recruitment-only
        // and cannot create cadet clans or new heroes on weekly ticks.
        campaignStarter.AddBehavior(new KaiDiplomacyBehavior());
        campaignStarter.AddBehavior(new KaiDiplomacyOfficeBehavior());
        campaignStarter.AddBehavior(new KaiDiplomacyAiBehavior());
        campaignStarter.AddBehavior(new KaiCultureAssimilationBehavior());
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
