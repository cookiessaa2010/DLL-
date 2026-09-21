using System;
using System.Collections.Generic;
using System.Linq;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Incoming AI marriage offers for the player's house. AI only proposes; the player
/// explicitly accepts or declines, and acceptance opens the same native marriage barter
/// used by the Family Affairs menu. AI same-sex proposals are intentionally disabled.
/// </summary>
public sealed class KaiIncomingMarriageProposalBehavior : CampaignBehaviorBase
{
    private const int ProposalCooldownDays = 45;
    private const string CooldownSaveKey = "kaitor_ai_marriage_offer_cooldown_v1";
    private const string PendingMemberSaveKey = "kaitor_ai_marriage_pending_member_v1";
    private const string PendingTargetSaveKey = "kaitor_ai_marriage_pending_target_v1";
    private const string PendingClanSaveKey = "kaitor_ai_marriage_pending_clan_v1";

    private Dictionary<string, double> _cooldownUntilDays = new();
    private string _pendingMemberId;
    private string _pendingTargetId;
    private string _pendingClanId;
    private bool _inquiryOpen;

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, TryPresentPendingOffer);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(CooldownSaveKey, ref _cooldownUntilDays);
        dataStore.SyncData(PendingMemberSaveKey, ref _pendingMemberId);
        dataStore.SyncData(PendingTargetSaveKey, ref _pendingTargetId);
        dataStore.SyncData(PendingClanSaveKey, ref _pendingClanId);
        _cooldownUntilDays ??= new Dictionary<string, double>();
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        starter.AddPlayerLine(
            "kaitor_ai_marriage_pending_discuss",
            "hero_main_options",
            "kaitor_ai_marriage_pending_reply",
            "Я получил брачное предложение вашего дома. Обсудим условия.",
            CanDiscussPendingOfferWithCurrentLord,
            null,
            127,
            null,
            null);

        starter.AddDialogLine(
            "kaitor_ai_marriage_pending_reply",
            "kaitor_ai_marriage_pending_reply",
            "kaitor_ai_marriage_pending_options",
            "Да. Предложение остаётся в силе. Перейдём к брачным условиям.",
            CanDiscussPendingOfferWithCurrentLord,
            null,
            120,
            null);

        starter.AddPlayerLine(
            "kaitor_ai_marriage_pending_accept",
            "kaitor_ai_marriage_pending_options",
            "close_window",
            "Продолжить брачные переговоры.",
            null,
            AcceptPendingOfferFromConversation,
            120,
            null,
            null);

        starter.AddPlayerLine(
            "kaitor_ai_marriage_pending_decline",
            "kaitor_ai_marriage_pending_options",
            "close_window",
            "Нет. Я отказываюсь от предложения.",
            null,
            DeclinePendingOfferFromConversation,
            100,
            null,
            null);
    }

    private void OnWeeklyTick()
    {
        if (Campaign.Current == null || Clan.PlayerClan == null || HasPendingOffer)
            return;

        CleanupCooldowns();
        var proposal = FindBestProposal();
        if (proposal == null)
            return;

        _pendingMemberId = proposal.Member.StringId;
        _pendingTargetId = proposal.Target.StringId;
        _pendingClanId = proposal.TargetClan.StringId;
        _cooldownUntilDays[proposal.TargetClan.StringId] = CampaignTime.Now.ToDays + ProposalCooldownDays;

        KaiRuntimeLog.Write(
            "AI_MARRIAGE_PROPOSAL_QUEUED",
            $"clan={proposal.TargetClan.StringId}; member={proposal.Member.StringId}; target={proposal.Target.StringId}; score={proposal.Score}; cooldown={ProposalCooldownDays}d");

        TryPresentPendingOffer();
    }

    private Proposal FindBestProposal()
    {
        var playerClan = Clan.PlayerClan;
        var model = Campaign.Current?.Models?.MarriageModel;
        if (playerClan == null || model == null)
            return null;

        var members = playerClan.Heroes
            .Where(h => h != null &&
                        h.IsAlive &&
                        h.IsActive &&
                        !h.IsTemplate &&
                        !h.IsMinorFactionHero &&
                        h.Spouse == null &&
                        (h == Hero.MainHero || h.IsLord) &&
                        h.CanMarry() &&
                        model.IsSuitableForMarriage(h))
            .OrderBy(h => h.StringId, StringComparer.Ordinal)
            .ToArray();

        Proposal best = null;
        foreach (var clan in Clan.All
                     .Where(IsEligibleOfferingClan)
                     .OrderBy(c => c.StringId, StringComparer.Ordinal))
        {
            if (GetCooldown(clan.StringId) > CampaignTime.Now.ToDays)
                continue;
            if (FactionManager.IsAtWarAgainstFaction(playerClan.MapFaction, clan.MapFaction))
                continue;

            foreach (var target in clan.AliveLords
                         .Where(h => h != null &&
                                     h.IsAlive &&
                                     h.IsActive &&
                                     !h.IsPrisoner &&
                                     h.Spouse == null &&
                                     h.CanMarry() &&
                                     model.IsSuitableForMarriage(h))
                         .OrderBy(h => h.StringId, StringComparer.Ordinal))
            {
                foreach (var member in members)
                {
                    // User requirement: AI female+female marriages stay off.
                    if (member.IsFemale == target.IsFemale)
                        continue;
                    if (!model.IsCoupleSuitableForMarriage(member, target))
                        continue;

                    var score = Score(member, target, clan, playerClan);
                    if (score < 0)
                        continue;

                    if (best == null || score > best.Score ||
                        (score == best.Score && string.CompareOrdinal(clan.StringId + target.StringId, best.TargetClan.StringId + best.Target.StringId) < 0))
                        best = new Proposal(member, target, clan, score);
                }
            }
        }

        return best;
    }

    private static int Score(Hero member, Hero target, Clan targetClan, Clan playerClan)
    {
        var score = targetClan.GetRelationWithClan(playerClan);
        score += Math.Min(30, targetClan.Tier * 5);
        score += Math.Min(20, target.Level / 3);
        if (targetClan.Kingdom != null && targetClan.Kingdom == playerClan.Kingdom)
            score += 15;

        // Long-lived races use the same normalized social age as the marriage
        // model, so a century-scale Dawi/elf calendar gap is not treated as a human gap.
        var memberAge = KaiRaceLifecycle.GetSocialMarriageAge(member);
        var targetAge = KaiRaceLifecycle.GetSocialMarriageAge(target);
        var ageGap = Math.Abs(memberAge - targetAge);
        score -= (int)Math.Min(25f, ageGap / 4f);
        return score;
    }

    private void TryPresentPendingOffer()
    {
        if (_inquiryOpen || !HasPendingOffer || !CanPresentNow())
            return;

        if (!TryResolvePending(out var member, out var target, out var targetClan) ||
            !IsPairStillValid(member, target, targetClan))
        {
            KaiRuntimeLog.Write("AI_MARRIAGE_PROPOSAL_DROPPED", $"member={_pendingMemberId}; target={_pendingTargetId}; clan={_pendingClanId}; reason=stale");
            ClearPending();
            return;
        }

        _inquiryOpen = true;
        var childlessWarning = TorFamilySafety.CanUseVanillaPregnancy(member, target)
            ? string.Empty
            : " У этой пары не будет биологических детей.";

        InformationManager.ShowInquiry(
            new InquiryData(
                "Брачное предложение",
                $"Дом {targetClan.Name} направил к вам предложение: заключить брак между {member.Name} и {target.Name}. " +
                "Принятие не заключит брак автоматически — после него откроются обычные брачные переговоры." +
                childlessWarning,
                true,
                true,
                "Рассмотреть предложение",
                "Отказать",
                () =>
                {
                    _inquiryOpen = false;
                    KaiRuntimeLog.Write(
                        "AI_MARRIAGE_PROPOSAL_CONTINUE",
                        $"clan={targetClan.StringId}; member={member.StringId}; target={target.StringId}");
                    OpenPendingOfferConversation(targetClan);
                },
                () =>
                {
                    _inquiryOpen = false;
                    KaiRuntimeLog.Write("AI_MARRIAGE_PROPOSAL_DECLINED", $"clan={targetClan.StringId}; member={member.StringId}; target={target.StringId}");
                    ClearPending();
                }),
            false,
            false);

        KaiRuntimeLog.Write("AI_MARRIAGE_PROPOSAL_OPEN", $"clan={targetClan.StringId}; member={member.StringId}; target={target.StringId}");
    }

    private void OpenPendingOfferConversation(Clan targetClan)
    {
        var leader = targetClan?.Leader;
        if (leader == null || !leader.IsAlive || leader.IsPrisoner)
        {
            KaiRuntimeLog.Write(
                "AI_MARRIAGE_PROPOSAL_FAILED",
                $"clan={targetClan?.StringId ?? "null"}; reason=leader_unavailable");
            return;
        }

        try
        {
            KaiMessengerService.StartRemoteConversation(leader);
            KaiRuntimeLog.Write(
                "AI_MARRIAGE_PROPOSAL_CONVERSATION",
                $"clan={targetClan.StringId}; leader={leader.StringId}");
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception(
                "AI_MARRIAGE_PROPOSAL_FAILED",
                ex,
                $"clan={targetClan.StringId}; stage=start_remote_conversation");
        }
    }

    private bool CanDiscussPendingOfferWithCurrentLord()
    {
        if (!TryResolvePending(out var member, out var target, out var targetClan) ||
            !IsPairStillValid(member, target, targetClan))
            return false;

        return Hero.OneToOneConversationHero == targetClan.Leader;
    }

    private void AcceptPendingOfferFromConversation()
    {
        if (!TryResolvePending(out var member, out var target, out var targetClan) ||
            !IsPairStillValid(member, target, targetClan))
        {
            KaiRuntimeLog.Write(
                "AI_MARRIAGE_PROPOSAL_FAILED",
                $"clan={_pendingClanId ?? "null"}; reason=stale_at_conversation");
            ClearPending();
            return;
        }

        ClearPending();
        if (!KaiMarriageBarterBridge.TryStart(member, target, targetClan, out var reason))
        {
            KaiRuntimeLog.Write(
                "AI_MARRIAGE_PROPOSAL_FAILED",
                $"clan={targetClan.StringId}; member={member.StringId}; target={target.StringId}; reason={reason}");
            MBInformationManager.AddQuickInformation(
                new TextObject(reason),
                3500,
                target.CharacterObject,
                null,
                string.Empty);
            return;
        }

        KaiRuntimeLog.Write(
            "AI_MARRIAGE_PROPOSAL_ACCEPTED",
            $"clan={targetClan.StringId}; member={member.StringId}; target={target.StringId}");
    }

    private void DeclinePendingOfferFromConversation()
    {
        if (TryResolvePending(out var member, out var target, out var targetClan))
        {
            KaiRuntimeLog.Write(
                "AI_MARRIAGE_PROPOSAL_DECLINED",
                $"clan={targetClan.StringId}; member={member.StringId}; target={target.StringId}; source=conversation");
        }

        ClearPending();
    }

    private static bool IsPairStillValid(Hero member, Hero target, Clan targetClan)
    {
        var model = Campaign.Current?.Models?.MarriageModel;
        if (model == null || member == null || target == null || targetClan == null)
            return false;
        if (member.Clan != Clan.PlayerClan || target.Clan != targetClan)
            return false;
        if (!member.IsAlive || !target.IsAlive || member.Spouse != null || target.Spouse != null || target.IsPrisoner)
            return false;
        if (member.IsFemale == target.IsFemale)
            return false;
        return member.CanMarry() && target.CanMarry() &&
               model.IsSuitableForMarriage(member) &&
               model.IsSuitableForMarriage(target) &&
               model.IsCoupleSuitableForMarriage(member, target);
    }

    private static bool IsEligibleOfferingClan(Clan clan)
        => clan != null &&
           clan != Clan.PlayerClan &&
           !clan.IsEliminated &&
           !clan.IsBanditFaction &&
           !clan.IsRebelClan &&
           !clan.IsMinorFaction &&
           !clan.IsClanTypeMercenary &&
           clan.Leader != null &&
           clan.Leader.IsAlive &&
           !clan.Leader.IsPrisoner;

    private static bool CanPresentNow()
    {
        var campaign = Campaign.Current;
        var mainParty = MobileParty.MainParty;
        if (campaign == null || mainParty == null)
            return false;
        if (campaign.ConversationManager?.IsConversationInProgress == true)
            return false;
        if (mainParty.MapEvent != null || mainParty.BesiegedSettlement != null)
            return false;
        if (PlayerEncounter.Current != null || Hero.MainHero?.IsPrisoner == true)
            return false;
        return true;
    }

    private bool TryResolvePending(out Hero member, out Hero target, out Clan clan)
    {
        member = Hero.AllAliveHeroes.FirstOrDefault(h => h != null && string.Equals(h.StringId, _pendingMemberId, StringComparison.Ordinal));
        target = Hero.AllAliveHeroes.FirstOrDefault(h => h != null && string.Equals(h.StringId, _pendingTargetId, StringComparison.Ordinal));
        clan = Clan.All.FirstOrDefault(c => c != null && string.Equals(c.StringId, _pendingClanId, StringComparison.Ordinal));
        return member != null && target != null && clan != null;
    }

    private bool HasPendingOffer
        => !string.IsNullOrWhiteSpace(_pendingMemberId) &&
           !string.IsNullOrWhiteSpace(_pendingTargetId) &&
           !string.IsNullOrWhiteSpace(_pendingClanId);

    private void ClearPending()
    {
        _pendingMemberId = null;
        _pendingTargetId = null;
        _pendingClanId = null;
    }

    private double GetCooldown(string clanId)
        => _cooldownUntilDays != null &&
           clanId != null &&
           _cooldownUntilDays.TryGetValue(clanId, out var value) &&
           !double.IsNaN(value) &&
           !double.IsInfinity(value)
            ? value
            : 0d;

    private void CleanupCooldowns()
    {
        var now = CampaignTime.Now.ToDays;
        foreach (var key in _cooldownUntilDays.Where(x => x.Value <= now || double.IsNaN(x.Value) || double.IsInfinity(x.Value)).Select(x => x.Key).ToArray())
            _cooldownUntilDays.Remove(key);
    }

    private sealed class Proposal
    {
        public Proposal(Hero member, Hero target, Clan targetClan, int score)
        {
            Member = member;
            Target = target;
            TargetClan = targetClan;
            Score = score;
        }

        public Hero Member { get; }
        public Hero Target { get; }
        public Clan TargetClan { get; }
        public int Score { get; }
    }
}
