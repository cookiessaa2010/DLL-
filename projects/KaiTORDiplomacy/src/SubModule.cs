using System;
using Bannerlord.UIExtenderEx;
using HarmonyLib;
using KaiTOR.Diplomacy.Models;
using KaiTOR.Diplomacy.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.MountAndBlade;

namespace KaiTOR.Diplomacy;

public sealed class SubModule : MBSubModuleBase
{
    private const bool EnableMarriageWarnings = true;

    private Harmony _harmony;
    private UIExtender _uiExtender;

    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        try
        {
            UIConfig.DoNotUseGeneratedPrefabs = true;
            _uiExtender = UIExtender.Create("KaiTOR_Diplomacy");
            _uiExtender.Register(typeof(SubModule).Assembly);
            _uiExtender.Enable();
            KaiRuntimeLog.Write("MESSENGER_ENCYCLOPEDIA_UI_READY", "embedded UIExtenderEx registered; external module not required.");
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("MESSENGER_ENCYCLOPEDIA_UI_FAILED", ex, "stage=EmbeddedUIExtender");
        }

        try
        {
            _harmony = new Harmony("kaitor.diplomacy.kingdom-ui");
            _harmony.PatchAll(typeof(SubModule).Assembly);
            KaiRuntimeLog.Write("DIPLOMACY_UI_READY", "Harmony PatchAll completed independently.");
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("DIPLOMACY_UI_FAILED", ex, "stage=HarmonyPatchAll");
        }
    }

    protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
    {
        base.OnGameStart(game, gameStarterObject);
        if (gameStarterObject is not CampaignGameStarter campaignStarter) return;

        KaiRuntimeLog.Write("STARTUP", "KaiTOR Diplomacy v0.6.7.0 child-education campaign start.");

        // Family and diplomacy only. TOR/Bannerlord retain ownership of world population,
        // lord/clan generation, clan transitions, visual aging and natural mortality.
        InstallMarriageWrapper(campaignStarter);
        InstallPregnancyWrapper(campaignStarter);
        campaignStarter.AddBehavior(new KaiPregnancyLifecycleBehavior());
        if (EnableMarriageWarnings)
            campaignStarter.AddBehavior(new KaiMarriageWarningBehavior());

        // Additive NAP layer; ordinary TOR war/peace/alliance rules remain authoritative.
        InstallPermissionWrapper(campaignStarter);

        campaignStarter.AddBehavior(new KaiDiplomacyBehavior());
        campaignStarter.AddBehavior(new KaiDiplomacyAiBehavior());
        campaignStarter.AddBehavior(new KaiDiplomacyConversationBehavior());
        campaignStarter.AddBehavior(new KaiCultureAssimilationBehavior());
        campaignStarter.AddBehavior(new KaiDynasticMarriageBehavior());
        campaignStarter.AddBehavior(new KaiDawiDynastyBehavior());
        campaignStarter.AddBehavior(new KaiFamilyAffairsBehavior());
        campaignStarter.AddBehavior(new KaiIncomingMarriageProposalBehavior());
        campaignStarter.AddBehavior(new KaiLoreEducationBehavior());
        campaignStarter.AddBehavior(new KaiBloodKissBehavior());
        campaignStarter.AddBehavior(new KaiMercyRelationBehavior());
        campaignStarter.AddBehavior(new KaiMessengerBehavior());
        campaignStarter.AddBehavior(new KaiLiveTestBehavior());
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
}
