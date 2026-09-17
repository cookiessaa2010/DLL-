using System;
using System.Linq;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

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
        // Warning acknowledgement is deliberately session-only.
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        // Bannerlord enters hero_courtship_final_barter immediately before opening the
        // final marriage barter. Priority 200 places this guard in front of vanilla's
        // priority-100 continuation.
        starter.AddDialogLine(
            "kaitor_childless_marriage_warning",
            "hero_courtship_final_barter",
            "kaitor_childless_marriage_warning_options",
            "Этот брак возможен, но у этой пары не будет биологических детей. Продолжить брачные договорённости?",
            ShouldWarnBeforeFinalMarriage,
            null,
            200,
            null);

        starter.AddPlayerLine(
            "kaitor_childless_marriage_continue",
            "kaitor_childless_marriage_warning_options",
            "hero_courtship_final_barter",
            "Я понимаю. Продолжить брачные договорённости.",
            null,
            AcknowledgeCurrentMarriage,
            200,
            null,
            null);

        starter.AddPlayerLine(
            "kaitor_childless_marriage_cancel",
            "kaitor_childless_marriage_warning_options",
            "close_window",
            "Не сейчас. Я хочу ещё подумать об этом браке.",
            null,
            ClearAcknowledgement,
            200,
            null,
            null);
    }

    private bool ShouldWarnBeforeFinalMarriage()
    {
        var partner = ResolveCurrentCourtshipPartner();
        var mainHero = Hero.MainHero;
        if (partner == null || mainHero == null)
            return false;

        if (!KaiPlayerMarriageModel.InvolvesPlayerClan(mainHero, partner))
            return false;

        var pairKey = MakePairKey(mainHero, partner);
        if (!string.IsNullOrEmpty(pairKey) && string.Equals(pairKey, _acknowledgedPairKey, StringComparison.Ordinal))
            return false;

        return !TorFamilySafety.CanUseVanillaPregnancy(mainHero, partner);
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

        // Some TOR/arranged-barter paths can bypass the final courtship state. By this
        // event MarriageAction has already assigned Spouse, so this is an informational
        // fallback rather than a fake cancel prompt.
        MBInformationManager.AddQuickInformation(
            new TextObject($"Брак заключён: {firstHero?.Name} и {secondHero?.Name}. У этой пары не будет биологических детей."),
            5000,
            secondHero?.CharacterObject,
            null,
            string.Empty);
    }

    private static Hero ResolveCurrentCourtshipPartner()
    {
        var mainHero = Hero.MainHero;
        if (mainHero == null)
            return null;

        var conversationHero = Hero.OneToOneConversationHero;
        if (conversationHero != null && IsPendingMarriage(mainHero, conversationHero))
            return conversationHero;

        var states = Romance.RomanticStateList;
        if (states == null)
            return null;

        var candidates = states
            .Where(state => state != null &&
                            state.Level >= Romance.RomanceLevelEnum.MatchMadeByFamily &&
                            state.Level < Romance.RomanceLevelEnum.Marriage &&
                            (state.Person1 == mainHero || state.Person2 == mainHero))
            .Select(state => new
            {
                Hero = state.Partner(mainHero),
                Level = state.Level
            })
            .Where(x => x.Hero != null && x.Hero.IsAlive && x.Hero.Spouse == null)
            .OrderByDescending(x => (int)x.Level)
            .ToArray();

        // When negotiating with the clan head, prefer the pending partner from that
        // clan. This matches Bannerlord's final-barter flow used in the live TOR test.
        if (conversationHero?.Clan != null)
        {
            var sameClan = candidates.FirstOrDefault(x => x.Hero.Clan == conversationHero.Clan);
            if (sameClan != null)
                return sameClan.Hero;
        }

        return candidates.FirstOrDefault()?.Hero;
    }

    private static bool IsPendingMarriage(Hero mainHero, Hero other)
    {
        if (mainHero == null || other == null || other.Spouse != null)
            return false;

        var level = Romance.GetRomanticLevel(mainHero, other);
        return level >= Romance.RomanceLevelEnum.MatchMadeByFamily &&
               level < Romance.RomanceLevelEnum.Marriage;
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
