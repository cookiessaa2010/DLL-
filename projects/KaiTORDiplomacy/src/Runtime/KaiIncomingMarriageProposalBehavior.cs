using System;
using System.Linq;
using KaiTOR.Diplomacy.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Special KaiTOR marriage-offer transport only.
/// Ordinary AI -> player-clan offers are owned by Bannerlord's native
/// MarriageOfferCampaignBehavior, which now works through KaiPlayerMarriageModel.
/// This behavior deliberately performs no periodic matchmaking, preventing duplicate offers.
/// </summary>
public sealed class KaiIncomingMarriageProposalBehavior : CampaignBehaviorBase
{
    private const string PendingMemberSaveKey = "kaitor_special_marriage_pending_member_v2";
    private const string PendingTargetSaveKey = "kaitor_special_marriage_pending_target_v2";
    private const string PendingClanSaveKey = "kaitor_special_marriage_pending_clan_v2";
    private const string PendingKindSaveKey = "kaitor_special_marriage_pending_kind_v2";

    private string _pendingMemberId;
    private string _pendingTargetId;
    private string _pendingClanId;
    private string _pendingKind;
    private bool _inquiryOpen;

    public override void RegisterEvents()
    {
        // No weekly/daily matcher exists here. Native MarriageOfferCampaignBehavior owns
        // ordinary marriage proposals. We only present an explicitly queued special offer.
        CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, TryPresentPendingOffer);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(PendingMemberSaveKey, ref _pendingMemberId);
        dataStore.SyncData(PendingTargetSaveKey, ref _pendingTargetId);
        dataStore.SyncData(PendingClanSaveKey, ref _pendingClanId);
        dataStore.SyncData(PendingKindSaveKey, ref _pendingKind);
    }

    public bool HasPendingSpecialOffer =>
        !string.IsNullOrWhiteSpace(_pendingMemberId) &&
        !string.IsNullOrWhiteSpace(_pendingTargetId) &&
        !string.IsNullOrWhiteSpace(_pendingClanId);

    /// <summary>
    /// Queue only a KaiTOR-specific offer (dynastic/treaty package/etc.).
    /// Ordinary marriage proposals must never call this method.
    /// </summary>
    public bool TryQueueSpecialOffer(Hero playerClanMember, Hero target, Clan targetClan, string kind, out string reason)
    {
        reason = string.Empty;
        if (Campaign.Current == null || playerClanMember == null || target == null || targetClan == null)
        {
            reason = Ui("kaitor_diplomacy_special_marriage_invalid", "Invalid marriage-offer data.");
            return false;
        }

        if (HasPendingSpecialOffer)
        {
            reason = Ui("kaitor_diplomacy_special_marriage_pending", "Another special marriage offer is already pending.");
            return false;
        }

        var model = Campaign.Current.Models.MarriageModel;
        if (playerClanMember.Clan != Clan.PlayerClan ||
            target.Clan != targetClan ||
            !playerClanMember.IsAlive ||
            !target.IsAlive ||
            playerClanMember.Spouse != null ||
            target.Spouse != null ||
            target.IsPrisoner ||
            model == null ||
            !model.IsCoupleSuitableForMarriage(playerClanMember, target))
        {
            reason = Ui("kaitor_diplomacy_special_marriage_ineligible", "The proposed pair is no longer eligible.");
            return false;
        }

        _pendingMemberId = playerClanMember.StringId;
        _pendingTargetId = target.StringId;
        _pendingClanId = targetClan.StringId;
        _pendingKind = string.IsNullOrWhiteSpace(kind) ? "special" : kind.Trim();

        KaiRuntimeLog.Write(
            "SPECIAL_MARRIAGE_PROPOSAL_QUEUED",
            $"kind={_pendingKind}; clan={targetClan.StringId}; member={playerClanMember.StringId}; target={target.StringId}");

        TryPresentPendingOffer();
        return true;
    }

    private void TryPresentPendingOffer()
    {
        if (_inquiryOpen || !HasPendingSpecialOffer || !CanPresentNow())
            return;

        if (!TryResolvePending(out var member, out var target, out var targetClan) ||
            !IsPairStillValid(member, target, targetClan))
        {
            KaiRuntimeLog.Write(
                "SPECIAL_MARRIAGE_PROPOSAL_DROPPED",
                $"kind={_pendingKind}; member={_pendingMemberId}; target={_pendingTargetId}; clan={_pendingClanId}; reason=stale");
            ClearPending();
            return;
        }

        var kind = _pendingKind ?? "special";
        _inquiryOpen = true;

        var childlessWarning = Models.TorFamilySafety.CanUseVanillaPregnancy(member, target)
            ? string.Empty
            : " " + Ui(
                "kaitor_diplomacy_special_marriage_childless",
                "This social marriage will not produce biological children.");

        var kindText = GetKindText(kind);

        InformationManager.ShowInquiry(
            new InquiryData(
                Ui("kaitor_diplomacy_special_marriage_title", "KaiTOR: Special marriage proposal"),
                UiFormat(
                    "kaitor_diplomacy_special_marriage_body",
                    "{CLAN} proposes a {KIND} marriage between {MEMBER} and {TARGET}. Accepting opens the normal Bannerlord marriage barter; the marriage is not forced.",
                    ("CLAN", targetClan.Name),
                    ("KIND", kindText),
                    ("MEMBER", member.Name),
                    ("TARGET", target.Name)) + childlessWarning,
                true,
                true,
                Ui("kaitor_diplomacy_special_marriage_review", "Review proposal"),
                Ui("kaitor_diplomacy_special_marriage_decline", "Decline"),
                () =>
                {
                    _inquiryOpen = false;
                    AcceptPendingOffer();
                },
                () =>
                {
                    _inquiryOpen = false;
                    KaiRuntimeLog.Write(
                        "SPECIAL_MARRIAGE_PROPOSAL_DECLINED",
                        $"kind={kind}; clan={targetClan.StringId}; member={member.StringId}; target={target.StringId}");
                    ClearPending();
                }),
            false,
            false);

        KaiRuntimeLog.Write(
            "SPECIAL_MARRIAGE_PROPOSAL_OPEN",
            $"kind={kind}; clan={targetClan.StringId}; member={member.StringId}; target={target.StringId}");
    }

    private void AcceptPendingOffer()
    {
        if (!TryResolvePending(out var member, out var target, out var targetClan) ||
            !IsPairStillValid(member, target, targetClan))
        {
            ClearPending();
            return;
        }

        var kind = _pendingKind ?? "special";
        ClearPending();

        if (!KaiMarriageBarterBridge.TryStart(member, target, targetClan, out var reason))
        {
            KaiRuntimeLog.Write(
                "SPECIAL_MARRIAGE_PROPOSAL_FAILED",
                $"kind={kind}; clan={targetClan.StringId}; member={member.StringId}; target={target.StringId}; reason={reason}");
            MBInformationManager.AddQuickInformation(new TextObject(reason), 3500, target.CharacterObject, null, string.Empty);
            return;
        }

        KaiRuntimeLog.Write(
            "SPECIAL_MARRIAGE_PROPOSAL_ACCEPTED",
            $"kind={kind}; clan={targetClan.StringId}; member={member.StringId}; target={target.StringId}");
    }

    private static string Ui(string id, string fallback)
        => KaiTORDiplomacyUiText.Get(id, fallback);

    private static string UiFormat(string id, string fallback, params (string Key, object Value)[] values)
        => KaiTORDiplomacyUiText.Format(id, fallback, values);

    private static string GetKindText(string kind)
    {
        if (string.Equals(kind, "dynastic", StringComparison.OrdinalIgnoreCase))
            return Ui("kaitor_diplomacy_special_marriage_kind_dynastic", "dynastic");
        if (string.Equals(kind, "treaty", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(kind, "treaty_package", StringComparison.OrdinalIgnoreCase))
            return Ui("kaitor_diplomacy_special_marriage_kind_treaty", "treaty-linked");
        return Ui("kaitor_diplomacy_special_marriage_kind_special", "special");
    }

    private static bool IsPairStillValid(Hero member, Hero target, Clan targetClan)
    {
        var model = Campaign.Current?.Models?.MarriageModel;
        return model != null &&
               member != null &&
               target != null &&
               targetClan != null &&
               member.Clan == Clan.PlayerClan &&
               target.Clan == targetClan &&
               member.IsAlive &&
               target.IsAlive &&
               member.Spouse == null &&
               target.Spouse == null &&
               !target.IsPrisoner &&
               model.IsSuitableForMarriage(member) &&
               model.IsSuitableForMarriage(target) &&
               model.IsCoupleSuitableForMarriage(member, target);
    }

    private static bool CanPresentNow()
    {
        var campaign = Campaign.Current;
        var mainParty = MobileParty.MainParty;
        if (campaign == null || mainParty == null || Hero.MainHero == null)
            return false;
        if (campaign.ConversationManager?.IsConversationInProgress == true)
            return false;
        if (mainParty.MapEvent != null || mainParty.BesiegedSettlement != null)
            return false;
        if (TaleWorlds.CampaignSystem.Encounters.PlayerEncounter.Current != null || Hero.MainHero.IsPrisoner)
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

    private void ClearPending()
    {
        _pendingMemberId = null;
        _pendingTargetId = null;
        _pendingClanId = null;
        _pendingKind = null;
    }
}
