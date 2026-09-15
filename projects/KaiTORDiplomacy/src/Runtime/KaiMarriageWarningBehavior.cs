using System;
using System.Linq;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Player-facing warning layer for marriages that KaiTOR allows socially but blocks
/// from the vanilla offspring pipeline. No persistent state is stored here.
/// </summary>
public sealed class KaiMarriageWarningBehavior : CampaignBehaviorBase
{
    private string _acknowledgedPairKey;

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        CampaignEvents.BeforeHeroesMarried.AddNonSerializedListener(this, OnBeforeHeroesMarried);
    }

    public override void SyncData(IDataStore dataStore)
    {
        // Intentionally empty: warning/acknowledgement state must never become part of a save.
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        // Bannerlord 1.3.15 enters this state immediately before the final marriage barter.
        // Priority 200 lets KaiTOR show the warning before the vanilla priority-100 line.
        starter.AddDialogLine(
            "kaitor_childless_marriage_warning",
            "hero_courtship_final_barter",
            "kaitor_childless_marriage_warning_options",
            "{=kaitor_childless_marriage_warning}KaiTOR warning: this marriage is allowed, but this couple will not be able to have biological children. KaiTOR will not generate offspring for this pairing. Do you still want to continue with the marriage arrangements?",
            ShouldWarnBeforeFinalMarriage,
            null,
            200,
            null);

        starter.AddPlayerLine(
            "kaitor_childless_marriage_continue",
            "kaitor_childless_marriage_warning_options",
            "hero_courtship_final_barter",
            "{=kaitor_childless_marriage_continue}I understand. Continue with the marriage arrangements.",
            null,
            AcknowledgeCurrentMarriage,
            200,
            null,
            null);

        starter.AddPlayerLine(
            "kaitor_childless_marriage_cancel",
            "kaitor_childless_marriage_warning_options",
            "close_window",
            "{=kaitor_childless_marriage_cancel}Not now. I want to reconsider this marriage.",
            null,
            ClearAcknowledgement,
            200,
            null,
            null);
    }

    private bool ShouldWarnBeforeFinalMarriage()
    {
        var partner = ResolveCurrentCourtshipPartner();
        if (partner == null || Hero.MainHero == null)
            return false;

        if (!KaiPlayerMarriageModel.InvolvesPlayerClan(Hero.MainHero, partner))
            return false;

        var pairKey = MakePairKey(Hero.MainHero, partner);
        if (!string.IsNullOrEmpty(pairKey) && string.Equals(pairKey, _acknowledgedPairKey, StringComparison.Ordinal))
            return false;

        return !TorFamilySafety.CanUseVanillaPregnancy(Hero.MainHero, partner);
    }

    private void AcknowledgeCurrentMarriage()
    {
        var partner = ResolveCurrentCourtshipPartner();
        _acknowledgedPairKey = MakePairKey(Hero.MainHero, partner);
    }

    private void ClearAcknowledgement()
    {
        _acknowledgedPairKey = null;
    }

    private void OnBeforeHeroesMarried(Hero firstHero, Hero secondHero, bool showNotification)
    {
        if (!KaiPlayerMarriageModel.InvolvesPlayerClan(firstHero, secondHero))
            return;
        if (TorFamilySafety.CanUseVanillaPregnancy(firstHero, secondHero))
            return;

        var pairKey = MakePairKey(firstHero, secondHero);
        if (!string.IsNullOrEmpty(pairKey) && string.Equals(pairKey, _acknowledgedPairKey, StringComparison.Ordinal))
        {
            _acknowledgedPairKey = null;
            return;
        }

        // Backup for marriage paths that bypass the normal courtship final-barter node
        // (for example arranged/barter-driven paths). At this point MarriageAction has
        // already accepted the couple, so this is informational only; fertility remains
        // hard-blocked by KaiPregnancyModel.
        InformationManager.DisplayMessage(new InformationMessage(
            $"KaiTOR family warning: {firstHero?.Name} and {secondHero?.Name} can marry, but this marriage will not produce biological children."));
    }

    private static Hero ResolveCurrentCourtshipPartner()
    {
        var mainHero = Hero.MainHero;
        var conversationHero = Hero.OneToOneConversationHero;
        if (mainHero == null || conversationHero == null)
            return null;

        if (Romance.GetRomanticLevel(mainHero, conversationHero) == Romance.RomanceLevelEnum.CoupleAgreedOnMarriage)
            return conversationHero;

        var clan = conversationHero.Clan;
        if (clan == null)
            return null;

        // Mirrors Bannerlord 1.3.15 RomanceCampaignBehavior's final-courtship lookup
        // when the player speaks to the partner's clan leader instead of the partner.
        return clan.AliveLords.FirstOrDefault(hero =>
            hero != null &&
            hero != conversationHero &&
            Romance.GetRomanticLevel(mainHero, hero) == Romance.RomanceLevelEnum.CoupleAgreedOnMarriage);
    }

    private static string MakePairKey(Hero firstHero, Hero secondHero)
    {
        var first = firstHero?.StringId;
        var second = secondHero?.StringId;
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            return null;

        return string.CompareOrdinal(first, second) <= 0
            ? first + "|" + second
            : second + "|" + first;
    }
}
