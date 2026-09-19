using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.ScreenSystem;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Save-safe asynchronous courier modeled on the Diplomacy/Shokuho flow:
/// send from a hero encyclopedia page, wait for travel, then open a real lord dialogue.
/// State is stored only as primitive hero-id -> arrival-day dictionaries.
/// </summary>
public sealed class KaiMessengerBehavior : CampaignBehaviorBase
{
    private const int MinimumGoldCost = 75;
    private const double MinimumTravelDays = 0.25d;
    private const double MaximumTravelDays = 3.0d;
    private const string ArrivalSaveKey = "kaitor_messenger_arrival_days_v1";

    private Dictionary<string, double> _arrivalDayByHeroId = new(StringComparer.Ordinal);
    private bool _inquiryOpen;

    public override void RegisterEvents()
    {
        CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(ArrivalSaveKey, ref _arrivalDayByHeroId);
        _arrivalDayByHeroId ??= new Dictionary<string, double>(StringComparer.Ordinal);
    }

    public bool HasMessengerFor(Hero target)
        => target != null && _arrivalDayByHeroId.ContainsKey(target.StringId);

    public int GetSendCost(Hero target)
        => MinimumGoldCost + (int)Math.Round(GetTravelDays(target) * 24d);

    public double GetTravelDays(Hero target)
    {
        if (target == null || MobileParty.MainParty == null)
            return 1d;

        try
        {
            var targetPosition = target.PartyBelongedTo?.Position
                                 ?? target.CurrentSettlement?.Position
                                 ?? target.Clan?.FactionMidSettlement?.Position;

            if (targetPosition == null)
                return 1d;

            var distance = (targetPosition.Value - MobileParty.MainParty.Position).Length;
            var diagonal = Math.Max(1f, Campaign.MapDiagonal);
            var normalized = Math.Max(0d, Math.Min(1d, distance / diagonal));
            return Math.Max(MinimumTravelDays, Math.Min(MaximumTravelDays, MinimumTravelDays + normalized * (MaximumTravelDays - MinimumTravelDays)));
        }
        catch
        {
            return 1d;
        }
    }

    public bool CanSend(Hero target, out string reason)
    {
        reason = string.Empty;
        if (Campaign.Current == null || Hero.MainHero == null || MobileParty.MainParty == null)
        {
            reason = "Кампания сейчас недоступна.";
            return false;
        }
        if (target == null || target == Hero.MainHero || !target.IsAlive || !target.IsActive || !target.IsLord)
        {
            reason = "Гонца можно отправить только к доступному живому лорду.";
            return false;
        }
        if (target.IsPrisoner)
        {
            reason = "Этот лорд находится в плену.";
            return false;
        }
        if (HasMessengerFor(target))
        {
            reason = "Гонец к этому лорду уже в пути.";
            return false;
        }
        if (Hero.MainHero.IsPrisoner || MobileParty.MainParty.MapEvent != null || MobileParty.MainParty.BesiegedSettlement != null)
        {
            reason = "Сейчас отправить гонца невозможно.";
            return false;
        }

        var cost = GetSendCost(target);
        if (Hero.MainHero.Gold < cost)
        {
            reason = $"Для отправки гонца требуется {cost:N0} динаров.";
            return false;
        }

        return true;
    }

    public void RequestSend(Hero target)
    {
        if (!CanSend(target, out var reason))
        {
            ShowQuick(reason);
            return;
        }

        var cost = GetSendCost(target);
        var travelDays = GetTravelDays(target);
        var travelHours = Math.Max(1, (int)Math.Ceiling(travelDays * 24d));

        InformationManager.ShowInquiry(
            new InquiryData(
                "Отправить гонца",
                $"Отправить гонца к {target.Name}?\n\nСтоимость: {cost:N0} динаров.\nОжидаемое время в пути: около {travelHours} ч.",
                true,
                true,
                "Отправить",
                "Отмена",
                () => Dispatch(target, cost, travelDays),
                null),
            false,
            false);
    }

    private void Dispatch(Hero target, int cost, double travelDays)
    {
        if (!CanSend(target, out var reason))
        {
            ShowQuick(reason);
            return;
        }

        GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, cost, false);
        var arrival = CampaignTime.Now.ToDays + travelDays;
        _arrivalDayByHeroId[target.StringId] = arrival;

        KaiRuntimeLog.Write(
            "MESSENGER_SENT",
            $"target={target.StringId}; clan={target.Clan?.StringId ?? "none"}; kingdom={target.Clan?.Kingdom?.StringId ?? "none"}; cost={cost}; travelDays={travelDays:0.00}; arrivalDay={arrival:0.00}");

        ShowQuick($"Гонец отправлен к {target.Name}.");
    }

    private void OnHourlyTick()
    {
        if (_inquiryOpen || Campaign.Current == null || _arrivalDayByHeroId.Count == 0)
            return;

        var now = CampaignTime.Now.ToDays;
        foreach (var entry in _arrivalDayByHeroId.OrderBy(x => x.Value).ToArray())
        {
            if (entry.Value > now)
                break;

            var target = Hero.AllAliveHeroes.FirstOrDefault(h => h != null && string.Equals(h.StringId, entry.Key, StringComparison.Ordinal));
            if (target == null || !target.IsAlive)
            {
                _arrivalDayByHeroId.Remove(entry.Key);
                KaiRuntimeLog.Write("MESSENGER_FAILED", $"target={entry.Key}; reason=target_missing_or_dead");
                continue;
            }

            if (!CanPresentArrival(target))
                continue;

            ShowArrival(target);
            return;
        }
    }

    private void ShowArrival(Hero target)
    {
        _inquiryOpen = true;
        KaiRuntimeLog.Write("MESSENGER_ARRIVED", $"target={target.StringId}; clan={target.Clan?.StringId ?? "none"}");

        InformationManager.ShowInquiry(
            new InquiryData(
                "Гонец прибыл",
                $"Гонец добрался до {target.Name}. Начать разговор с лордом?",
                true,
                true,
                "Поговорить",
                "Позже",
                () =>
                {
                    _inquiryOpen = false;
                    OpenLordConversation(target);
                },
                () =>
                {
                    _inquiryOpen = false;
                    _arrivalDayByHeroId[target.StringId] = CampaignTime.Now.ToDays + 0.25d;
                    KaiRuntimeLog.Write("MESSENGER_DEFERRED", $"target={target.StringId}; retry=6h");
                }),
            false,
            false);
    }

    private void OpenLordConversation(Hero target)
    {
        if (!CanPresentArrival(target))
        {
            _arrivalDayByHeroId[target.StringId] = CampaignTime.Now.ToDays + 0.05d;
            ShowQuick("Лорд сейчас занят. Гонец попробует связаться с ним позже.");
            return;
        }

        try
        {
            var targetParty = target.PartyBelongedTo?.Party ?? target.CurrentSettlement?.Party;
            var playerData = new ConversationCharacterData(
                CharacterObject.PlayerCharacter,
                PartyBase.MainParty,
                false, false, false, false, false, false);
            var targetData = new ConversationCharacterData(
                target.CharacterObject,
                targetParty,
                false, false, false, false, false, false);

            _arrivalDayByHeroId.Remove(target.StringId);
            KaiRuntimeLog.Write(
                "MESSENGER_CONVERSATION",
                $"target={target.StringId}; clan={target.Clan?.StringId ?? "none"}; settlement={target.CurrentSettlement?.StringId ?? "none"}; party={target.PartyBelongedTo?.StringId ?? "none"}");

            CampaignMapConversation.OpenConversation(playerData, targetData);
        }
        catch (Exception ex)
        {
            _arrivalDayByHeroId[target.StringId] = CampaignTime.Now.ToDays + 0.05d;
            KaiRuntimeLog.Exception("MESSENGER_CONVERSATION_FAILED", ex, $"target={target.StringId}");
            ShowQuick("Разговор не открылся. Гонец попробует снова позже.");
        }
    }

    private static bool CanPresentArrival(Hero target)
    {
        if (target == null || !target.IsAlive || !target.IsActive || target.IsPrisoner)
            return false;
        if (target.PartyBelongedTo?.MapEvent != null)
            return false;
        if (Campaign.Current?.ConversationManager?.IsConversationInProgress == true)
            return false;
        if (PlayerEncounter.Current != null || Hero.MainHero?.IsPrisoner == true)
            return false;
        if (MobileParty.MainParty == null || MobileParty.MainParty.MapEvent != null || MobileParty.MainParty.BesiegedSettlement != null)
            return false;

        var top = ScreenManager.TopScreen;
        return top != null && string.Equals(top.GetType().Name, "MapScreen", StringComparison.Ordinal);
    }

    private static void ShowQuick(string text)
    {
        MBInformationManager.AddQuickInformation(
            new TextObject(text ?? string.Empty),
            3500,
            null,
            null,
            string.Empty);
    }
}
