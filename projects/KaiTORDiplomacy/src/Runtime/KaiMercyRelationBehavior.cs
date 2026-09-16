using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Rewards the player's deliberate post-battle decision to release a defeated lord.
/// Only the native ReleasedAfterBattle path from the player's own party is handled;
/// ransom, peace, escape and compensation releases are intentionally ignored.
/// </summary>
public sealed class KaiMercyRelationBehavior : CampaignBehaviorBase
{
    public const int PostBattleMercyRelationBonus = 50;

    public override void RegisterEvents()
    {
        CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, OnHeroPrisonerReleased);
    }

    public override void SyncData(IDataStore dataStore)
    {
    }

    private static void OnHeroPrisonerReleased(
        Hero prisoner,
        PartyBase formerCaptorParty,
        IFaction capturerFaction,
        EndCaptivityDetail detail,
        bool showNotification)
    {
        if (detail != EndCaptivityDetail.ReleasedAfterBattle)
            return;

        if (prisoner == null || prisoner == Hero.MainHero || !prisoner.IsLord)
            return;

        var mainParty = MobileParty.MainParty;
        if (mainParty == null || formerCaptorParty != mainParty.Party)
            return;

        ChangeRelationAction.ApplyPlayerRelation(
            prisoner,
            PostBattleMercyRelationBonus,
            affectRelatives: true,
            showQuickNotification: true);

        MBInformationManager.AddQuickInformation(
            new TextObject($"{prisoner.Name} запомнит проявленное вами милосердие."),
            0,
            prisoner.CharacterObject,
            null,
            string.Empty);
    }
}
