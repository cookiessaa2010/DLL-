using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KaiTOR.Diplomacy.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Save-safe messenger system written against Bannerlord APIs.
/// It stores only primitive dictionaries, recalculates movement toward the hero's current
/// map position every hour, and opens a real Bannerlord conversation only from a safe state.
/// </summary>
public sealed class KaiMessengerBehavior : CampaignBehaviorBase
{
    public const int BaseGoldCost = 75;
    public const int HourlyGoldCost = 1;
    public const int MinimumTravelHours = 6;
    public const int MaximumTravelHours = 72;

    private const string TargetSaveKey = "kaitor_messenger_v1_targets";
    private const string SenderSaveKey = "kaitor_messenger_v1_senders";
    private const string DepartureSaveKey = "kaitor_messenger_v1_departure";
    private const string TravelHoursSaveKey = "kaitor_messenger_v1_initial_hours";
    private const string CostSaveKey = "kaitor_messenger_v1_cost";
    private const string StatusSaveKey = "kaitor_messenger_v1_status";
    private const string XSaveKey = "kaitor_messenger_v1_x";
    private const string YSaveKey = "kaitor_messenger_v1_y";
    private const string SpeedSaveKey = "kaitor_messenger_v1_speed";
    private const string SequenceSaveKey = "kaitor_messenger_v1_sequence";
    private const string LastFailureSaveKey = "kaitor_messenger_v1_last_failure";

    private Dictionary<string, string> _targets = new();
    private Dictionary<string, string> _senders = new();
    private Dictionary<string, double> _departureDays = new();
    private Dictionary<string, int> _initialTravelHours = new();
    private Dictionary<string, int> _costs = new();
    private Dictionary<string, string> _statuses = new();
    private Dictionary<string, double> _x = new();
    private Dictionary<string, double> _y = new();
    private Dictionary<string, double> _speed = new();
    private int _sequence;
    private string _lastFailure = "none";
    private bool _arrivalInquiryOpen;

    private const string Traveling = "traveling";
    private const string Waiting = "waiting";
    private const string Failed = "failed";

    public override void RegisterEvents()
    {
        CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, _ => NormalizeLoadedState());
        CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, _ => NormalizeLoadedState());
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(TargetSaveKey, ref _targets);
        dataStore.SyncData(SenderSaveKey, ref _senders);
        dataStore.SyncData(DepartureSaveKey, ref _departureDays);
        dataStore.SyncData(TravelHoursSaveKey, ref _initialTravelHours);
        dataStore.SyncData(CostSaveKey, ref _costs);
        dataStore.SyncData(StatusSaveKey, ref _statuses);
        dataStore.SyncData(XSaveKey, ref _x);
        dataStore.SyncData(YSaveKey, ref _y);
        dataStore.SyncData(SpeedSaveKey, ref _speed);
        dataStore.SyncData(SequenceSaveKey, ref _sequence);
        dataStore.SyncData(LastFailureSaveKey, ref _lastFailure);

        _targets ??= new Dictionary<string, string>();
        _senders ??= new Dictionary<string, string>();
        _departureDays ??= new Dictionary<string, double>();
        _initialTravelHours ??= new Dictionary<string, int>();
        _costs ??= new Dictionary<string, int>();
        _statuses ??= new Dictionary<string, string>();
        _x ??= new Dictionary<string, double>();
        _y ??= new Dictionary<string, double>();
        _speed ??= new Dictionary<string, double>();
        _lastFailure ??= "none";
    }

    public bool TrySend(Hero target, out string reason)
    {
        reason = string.Empty;
        if (!CanSendTo(target, out reason))
            return false;

        var player = Hero.MainHero;
        var sourcePoint = player?.GetMapPoint();
        var targetPoint = target?.GetMapPoint();
        if (sourcePoint == null || targetPoint == null)
        {
            reason = "Messenger route cannot be calculated.";
            return false;
        }

        var distance = (targetPoint.Position - sourcePoint.Position).Length;
        var travelHours = EstimateTravelHours(distance);
        var cost = BaseGoldCost + travelHours * HourlyGoldCost;
        if (Hero.MainHero.Gold < cost)
        {
            reason = $"Not enough gold. Required: {cost}.";
            return false;
        }

        GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, cost, false);

        var id = "msg_" + (++_sequence).ToString(CultureInfo.InvariantCulture);
        _targets[id] = target.StringId;
        _senders[id] = Hero.MainHero.StringId;
        _departureDays[id] = CampaignTime.Now.ToDays;
        _initialTravelHours[id] = travelHours;
        _costs[id] = cost;
        _statuses[id] = Traveling;
        _x[id] = sourcePoint.Position.X;
        _y[id] = sourcePoint.Position.Y;
        _speed[id] = Math.Max(0.01d, distance / Math.Max(1, travelHours));

        KaiRuntimeLog.Write(
            "MESSENGER_SENT",
            $"id={id}; target={target.StringId}; hours={travelHours}; cost={cost}; distance={distance:0.00}");

        InformationManager.DisplayMessage(new InformationMessage(
            $"Messenger sent to {target.Name}. Estimated travel time: {travelHours} h. Cost: {cost}."));

        return true;
    }

    public bool CanSendTo(Hero target, out string reason)
    {
        reason = string.Empty;
        if (Campaign.Current == null || Hero.MainHero == null)
        {
            reason = "No active campaign.";
            return false;
        }
        if (target == null)
        {
            reason = "No target hero.";
            return false;
        }
        if (target == Hero.MainHero || target.IsHumanPlayerCharacter)
        {
            reason = "You cannot send a messenger to yourself.";
            return false;
        }
        if (!target.IsAlive || !target.IsActive)
        {
            reason = "The target is not currently reachable.";
            return false;
        }
        if (target.IsChild)
        {
            reason = "The target is too young for formal correspondence.";
            return false;
        }
        if (target.CharacterObject == null)
        {
            reason = "The target has no valid campaign character.";
            return false;
        }
        if (Campaign.Current.Models.InformationRestrictionModel != null &&
            !Campaign.Current.Models.InformationRestrictionModel.DoesPlayerKnowDetailsOf(target))
        {
            reason = "You do not know this character well enough to identify them for a messenger.";
            return false;
        }
        if (_targets.Values.Any(id => string.Equals(id, target.StringId, StringComparison.Ordinal)))
        {
            reason = "A messenger is already assigned to this hero.";
            return false;
        }
        return true;
    }

    public IEnumerable<string> DescribeStatus()
    {
        if (_targets.Count == 0)
        {
            yield return $"Messenger: no active messengers; lastFailure={_lastFailure ?? "none"}.";
            yield break;
        }

        foreach (var id in _targets.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            _targets.TryGetValue(id, out var targetId);
            _statuses.TryGetValue(id, out var status);
            _initialTravelHours.TryGetValue(id, out var hours);
            _costs.TryGetValue(id, out var cost);
            yield return $"{id}: target={targetId}; status={status ?? "unknown"}; initialHours={hours}; cost={cost}";
        }
    }

    public bool TrySendByHeroId(string heroId, out string reason)
    {
        var hero = Hero.AllAliveHeroes.FirstOrDefault(h =>
            h != null && string.Equals(h.StringId, heroId, StringComparison.OrdinalIgnoreCase));
        if (hero == null)
        {
            reason = "Unknown hero id.";
            return false;
        }
        return TrySend(hero, out reason);
    }

    private void OnHourlyTick()
    {
        NormalizeLoadedState();

        foreach (var id in _targets.Keys.ToArray())
        {
            if (!_statuses.TryGetValue(id, out var status))
                continue;

            var target = ResolveTarget(id);
            if (target == null || !target.IsAlive)
            {
                Fail(id, "target_missing_or_dead");
                continue;
            }

            if (status == Traveling)
                Advance(id, target);
        }

        TryPresentArrivedMessenger();
    }

    private void Advance(string id, Hero target)
    {
        var targetPoint = target.GetMapPoint();
        if (targetPoint == null)
            return;

        if (!_x.TryGetValue(id, out var x) ||
            !_y.TryGetValue(id, out var y) ||
            !_speed.TryGetValue(id, out var speed) ||
            speed <= 0d)
        {
            Fail(id, "invalid_route_state");
            return;
        }

        var current = new Vec2((float)x, (float)y);
        var delta = targetPoint.Position.ToVec2() - current;
        var distance = delta.Length;

        // Hard safety cap: if tracking a moving lord causes pathological chasing,
        // the messenger arrives after at most the original 72h ceiling + 24h grace.
        var departed = _departureDays.TryGetValue(id, out var departure) ? departure : CampaignTime.Now.ToDays;
        var elapsedHours = Math.Max(0d, (CampaignTime.Now.ToDays - departed) * CampaignTime.HoursInDay);
        var initialHours = _initialTravelHours.TryGetValue(id, out var ih) ? ih : MaximumTravelHours;

        if (distance <= speed || elapsedHours >= Math.Min(MaximumTravelHours + 24, initialHours + 24))
        {
            _x[id] = targetPoint.Position.X;
            _y[id] = targetPoint.Position.Y;
            _statuses[id] = Waiting;
            KaiRuntimeLog.Write(
                "MESSENGER_ARRIVED",
                $"id={id}; target={target.StringId}; elapsedHours={elapsedHours:0.0}");
            return;
        }

        var step = distance > 0.001f
            ? delta * ((float)speed / distance)
            : Vec2.Zero;
        var next = current + step;
        _x[id] = next.X;
        _y[id] = next.Y;
    }

    private void TryPresentArrivedMessenger()
    {
        if (_arrivalInquiryOpen || !CanOpenConversationNow())
            return;

        var id = _statuses
            .Where(x => string.Equals(x.Value, Waiting, StringComparison.Ordinal))
            .Select(x => x.Key)
            .OrderBy(x => x, StringComparer.Ordinal)
            .FirstOrDefault();

        if (id == null)
            return;

        var target = ResolveTarget(id);
        if (target == null || !target.IsAlive)
        {
            Fail(id, "target_missing_before_conversation");
            return;
        }

        _arrivalInquiryOpen = true;

        var options = new List<InquiryElement>
        {
            new(
                "start",
                KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_messenger_start", "Start conversation"),
                null,
                true,
                null),
            new(
                "later",
                KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_messenger_later", "Later"),
                null,
                true,
                null),
            new(
                "recall",
                KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_messenger_recall", "Recall messenger"),
                null,
                true,
                null)
        };

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_messenger_arrived", "Messenger arrived"),
                KaiTORDiplomacyUiText.Format(
                    "kaitor_diplomacy_ui_messenger_reached",
                    "Your messenger has reached {HERO}.",
                    ("HERO", target.Name)),
                options,
                true,
                1,
                1,
                KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_messenger_choose", "Choose"),
                KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_messenger_later", "Later"),
                selected =>
                {
                    _arrivalInquiryOpen = false;
                    if (selected.Count == 0)
                        return;

                    var action = selected[0].Identifier as string;
                    if (string.Equals(action, "start", StringComparison.Ordinal))
                    {
                        if (!TryOpenConversation(id, target, out var reason))
                        {
                            _lastFailure = $"id={id}; target={target.StringId}; conversation={reason}";
                            KaiRuntimeLog.Write(
                                "MESSENGER_FAILED",
                                $"id={id}; target={target.StringId}; reason=conversation_open:{reason}");
                        }
                        return;
                    }

                    if (string.Equals(action, "recall", StringComparison.Ordinal))
                    {
                        TryCancel(id, out _);
                        return;
                    }

                    KaiRuntimeLog.Write(
                        "MESSENGER_WAIT",
                        $"id={id}; target={target.StringId}");
                },
                _ =>
                {
                    _arrivalInquiryOpen = false;
                    KaiRuntimeLog.Write(
                        "MESSENGER_WAIT",
                        $"id={id}; target={target.StringId}");
                }),
            true,
            true);
    }

    public bool TryCancel(string id, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(id) || !_targets.ContainsKey(id))
        {
            reason = "Unknown messenger id.";
            return false;
        }

        var targetId = _targets[id];
        _lastFailure = $"id={id}; target={targetId}; reason=cancelled";
        Remove(id);
        KaiRuntimeLog.Write("MESSENGER_FAILED", _lastFailure);
        return true;
    }

    private bool TryOpenConversation(string id, Hero target, out string reason)
    {
        reason = string.Empty;
        if (!CanOpenConversationNow())
        {
            reason = "unsafe_player_state";
            return false;
        }

        if (target.PartyBelongedTo?.MapEvent != null)
        {
            reason = "target_in_battle";
            return false;
        }

        var playerParty = PartyBase.MainParty;
        var targetParty = target.CurrentSettlement?.Party ?? target.PartyBelongedTo?.Party;
        if (playerParty == null || targetParty == null)
        {
            reason = "conversation_party_missing";
            return false;
        }

        try
        {
            Campaign.Current.CurrentConversationContext = ConversationContext.Default;
            Campaign.Current.ConversationManager.OpenMapConversation(
                new ConversationCharacterData(Hero.MainHero.CharacterObject, playerParty),
                new ConversationCharacterData(target.CharacterObject, targetParty));

            KaiRuntimeLog.Write(
                "MESSENGER_CONVERSATION_OPEN",
                $"id={id}; target={target.StringId}; settlement={target.CurrentSettlement?.StringId ?? "none"}");

            Remove(id);
            return true;
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception(
                "MESSENGER_FAILED",
                ex,
                $"id={id}; target={target.StringId}; reason=conversation_exception");
            reason = ex.GetType().Name;
            return false;
        }
    }

    private static bool CanOpenConversationNow()
    {
        var campaign = Campaign.Current;
        var main = MobileParty.MainParty;
        if (campaign == null || main == null || Hero.MainHero == null)
            return false;
        if (TorHirelingBridge.IsEnlisted())
            return false;
        if (campaign.ConversationManager?.IsConversationInProgress == true)
            return false;
        if (PlayerEncounter.Current != null)
            return false;
        if (main.MapEvent != null || main.BesiegedSettlement != null || main.SiegeEvent != null)
            return false;
        if (Hero.MainHero.IsPrisoner)
            return false;
        return true;
    }

    private Hero ResolveTarget(string id)
    {
        if (!_targets.TryGetValue(id, out var heroId) || string.IsNullOrWhiteSpace(heroId))
            return null;

        return Hero.AllAliveHeroes.FirstOrDefault(h =>
            h != null && string.Equals(h.StringId, heroId, StringComparison.Ordinal));
    }

    private static int EstimateTravelHours(float distance)
    {
        // Scale relative to campaign diagonal so a cross-map trip approaches the 72h cap.
        var diagonal = Math.Max(1f, Campaign.MapDiagonal);
        var ratio = Math.Max(0f, Math.Min(1f, distance / diagonal));
        return Math.Max(
            MinimumTravelHours,
            Math.Min(
                MaximumTravelHours,
                (int)Math.Ceiling(MinimumTravelHours + ratio * (MaximumTravelHours - MinimumTravelHours))));
    }

    private void Fail(string id, string reason)
    {
        var target = _targets.TryGetValue(id, out var targetId) ? targetId : "unknown";
        _statuses[id] = Failed;
        _lastFailure = $"id={id}; target={target}; reason={reason}";
        KaiRuntimeLog.Write("MESSENGER_FAILED", _lastFailure);
        Remove(id);
    }

    private void Remove(string id)
    {
        _targets.Remove(id);
        _senders.Remove(id);
        _departureDays.Remove(id);
        _initialTravelHours.Remove(id);
        _costs.Remove(id);
        _statuses.Remove(id);
        _x.Remove(id);
        _y.Remove(id);
        _speed.Remove(id);
    }

    private void NormalizeLoadedState()
    {
        _targets ??= new Dictionary<string, string>();
        _senders ??= new Dictionary<string, string>();
        _departureDays ??= new Dictionary<string, double>();
        _initialTravelHours ??= new Dictionary<string, int>();
        _costs ??= new Dictionary<string, int>();
        _statuses ??= new Dictionary<string, string>();
        _x ??= new Dictionary<string, double>();
        _y ??= new Dictionary<string, double>();
        _speed ??= new Dictionary<string, double>();
        _lastFailure ??= "none";

        foreach (var id in _targets.Keys.ToArray())
        {
            if (string.IsNullOrWhiteSpace(_targets[id]) ||
                !_statuses.ContainsKey(id) ||
                !_departureDays.ContainsKey(id) ||
                !_initialTravelHours.ContainsKey(id) ||
                !_costs.ContainsKey(id) ||
                !_x.ContainsKey(id) ||
                !_y.ContainsKey(id) ||
                !_speed.ContainsKey(id))
                Remove(id);
        }
    }
}
