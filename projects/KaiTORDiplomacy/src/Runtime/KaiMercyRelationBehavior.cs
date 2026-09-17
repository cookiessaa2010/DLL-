using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Rewards the player's deliberate decision to release a defeated lord. Bannerlord's
/// post-battle prisoner screen commonly reports that choice as ReleasedByChoice, while
/// direct battle cleanup uses ReleasedAfterBattle; both paths are handled conservatively.
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
        if (prisoner == null || prisoner == Hero.MainHero || !prisoner.IsLord)
            return;

        if (!IsPlayerMercyRelease(formerCaptorParty, detail))
            return;

        ChangeRelationAction.ApplyPlayerRelation(
            prisoner,
            PostBattleMercyRelationBonus,
            affectRelatives: true,
            showQuickNotification: true);

        MBInformationManager.AddQuickInformation(
            new TextObject($"{prisoner.Name}: милосердие +{PostBattleMercyRelationBonus} к отношениям."),
            2500,
            prisoner.CharacterObject,
            null,
            string.Empty);
    }

    private static bool IsPlayerMercyRelease(PartyBase formerCaptorParty, EndCaptivityDetail detail)
    {
        var mainParty = PartyBase.MainParty;
        if (mainParty == null)
            return false;

        // Releasing a prisoner from the player's own roster through the party/post-battle
        // screen is reported as ReleasedByChoice in Bannerlord 1.3.15.
        if (detail == EndCaptivityDetail.ReleasedByChoice)
            return formerCaptorParty == mainParty;

        if (detail != EndCaptivityDetail.ReleasedAfterBattle)
            return false;

        if (formerCaptorParty == mainParty)
            return true;

        // Immediate post-battle release can occur before the hero is assigned to the
        // player's prisoner roster, so formerCaptorParty is null. Only accept that path
        // while the active player map event is a player victory.
        if (formerCaptorParty != null)
            return false;

        var mapEvent = MapEvent.PlayerMapEvent;
        return mapEvent != null &&
               mapEvent.IsPlayerMapEvent &&
               mapEvent.WinningSide != BattleSideEnum.None &&
               mapEvent.WinningSide == mapEvent.PlayerSide;
    }
}
