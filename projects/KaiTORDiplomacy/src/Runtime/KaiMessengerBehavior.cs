using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Distance-based, save-persistent messenger travel inspired by the Diplomacy
/// implementation used by Shokuho. Messengers do not teleport: a request is queued,
/// campaign time advances, and the conversation becomes available only after arrival.
/// </summary>
public sealed class KaiMessengerBehavior : CampaignBehaviorBase
{
    private const float MaximumTravelDays = 3f;
    private const float MinimumHourlySpeed = 2f;
    private const string SaveKey = "kaitor_messenger_arrival_days_v1";

    private Dictionary<string, double> _arrivalDays = new();
    private string _arrivalPromptHeroId;

    public override void RegisterEvents()
    {
        CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
        CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(SaveKey, ref _arrivalDays);
        _arrivalDays ??= new Dictionary<string, double>();
    }

    private void OnGameLoaded(CampaignGameStarter starter)
    {
        _arrivalDays ??= new Dictionary<string, double>();
        RemoveInvalidEntries();
    }

    internal bool CanSendMessenger(Hero hero, out TextObject reason)
    {
        if (!KaiMessengerService.CanContactHero(hero, out reason))
            return false;

        if (_arrivalDays.ContainsKey(hero.StringId))
        {
            reason = new TextObject("Гонец к этому персонажу уже в пути.");
            return false;
        }

        return true;
    }

    internal bool SendMessenger(Hero hero)
    {
        if (!CanSendMessenger(hero, out var reason))
        {
            KaiMessengerService.ShowQuick(reason?.ToString() ?? "Гонца сейчас отправить нельзя.");
            return false;
        }

        var hours = GetHoursToArrive(hero);
        var dueDay = CampaignTime.Now.ToDays + Math.Max(1, hours) / (double)CampaignTime.HoursInDay;
        _arrivalDays[hero.StringId] = dueDay;

        KaiRuntimeLog.Write(
            "MESSENGER_SENT",
            $"hero={hero.StringId}; name={hero.Name}; hours={hours}; dueDay={dueDay:0.000}; campaignDay={CampaignTime.Now.ToDays:0.000}");

        InformationManager.ShowInquiry(
            new InquiryData(
                "Гонец отправлен",
                BuildSentText(hero, hours),
                true,
                false,
                GameTexts.FindText("str_ok").ToString(),
                string.Empty,
                null,
                null));

        return true;
    }

    internal bool HasPendingMessenger(Hero hero)
        => hero != null && _arrivalDays.ContainsKey(hero.StringId);

    internal static int GetHoursToArrive(Hero hero)
    {
        try
        {
            var playerPoint = ResolveMapPoint(Hero.MainHero);
            var targetPoint = ResolveMapPoint(hero);
            if (playerPoint == null || targetPoint == null)
                return 1;

            var distance = (targetPoint.Position - playerPoint.Position).Length;
            var hourlySpeed = Math.Max(
                Campaign.MapDiagonal / (CampaignTime.HoursInDay * MaximumTravelDays * 1.5f),
                MinimumHourlySpeed);

            return Math.Max(1, (int)Math.Round(distance / hourlySpeed));
        }
        catch
        {
            return 1;
        }
    }

    private void OnHourlyTick()
    {
        if (_arrivalDays == null || _arrivalDays.Count == 0)
            return;

        RemoveInvalidEntries();

        if (!string.IsNullOrEmpty(_arrivalPromptHeroId))
            return;

        var now = CampaignTime.Now.ToDays;
        foreach (var pair in _arrivalDays
                     .Where(x => x.Value <= now)
                     .OrderBy(x => x.Value)
                     .ToList())
        {
            var hero = FindHero(pair.Key);
            if (hero == null || !hero.IsAlive)
            {
                _arrivalDays.Remove(pair.Key);
                KaiRuntimeLog.Write("MESSENGER_CANCELLED", $"hero={pair.Key}; reason=target_missing_or_dead");
                continue;
            }

            if (!KaiMessengerService.CanArriveAtHeroNow(hero))
                continue;

            if (!KaiMessengerService.CanInterruptPlayerOnMap())
                return;

            ShowArrival(hero);
            return;
        }
    }

    private void ShowArrival(Hero hero)
    {
        _arrivalPromptHeroId = hero.StringId;
        KaiRuntimeLog.Write(
            "MESSENGER_ARRIVED",
            $"hero={hero.StringId}; name={hero.Name}; campaignDay={CampaignTime.Now.ToDays:0.000}");

        InformationManager.ShowInquiry(
            new InquiryData(
                "Гонец прибыл",
                $"Гонец добрался до {hero.Name}. Перейти к разговору?",
                true,
                true,
                "Начать разговор",
                "Отменить встречу",
                () =>
                {
                    try
                    {
                        _arrivalDays.Remove(hero.StringId);
                        KaiRuntimeLog.Write("MESSENGER_ARRIVAL_ACCEPTED", $"hero={hero.StringId}");
                        KaiMessengerService.StartRemoteConversation(hero);
                    }
                    finally
                    {
                        _arrivalPromptHeroId = null;
                    }
                },
                () =>
                {
                    _arrivalDays.Remove(hero.StringId);
                    _arrivalPromptHeroId = null;
                    KaiRuntimeLog.Write("MESSENGER_ARRIVAL_CANCELLED", $"hero={hero.StringId}");
                }),
            true);
    }

    private void RemoveInvalidEntries()
    {
        if (_arrivalDays == null)
            return;

        foreach (var id in _arrivalDays.Keys.ToList())
        {
            var hero = FindHero(id);
            if (hero == null || !hero.IsAlive)
                _arrivalDays.Remove(id);
        }
    }

    private static Hero FindHero(string id)
        => string.IsNullOrWhiteSpace(id)
            ? null
            : Hero.AllAliveHeroes.FirstOrDefault(h => h != null && string.Equals(h.StringId, id, StringComparison.Ordinal));

    private static IMapPoint ResolveMapPoint(Hero hero)
    {
        if (hero == null)
            return null;

        if (hero.PartyBelongedTo != null)
            return hero.PartyBelongedTo;

        if (hero.CurrentSettlement != null)
            return hero.CurrentSettlement;

        if (hero.BornSettlement != null)
            return hero.BornSettlement;

        return null;
    }

    private static string BuildSentText(Hero hero, int travelHours)
    {
        if (travelHours < 2)
            return $"Гонец отправился к {hero.Name}. Он должен добраться менее чем за час.";

        if (travelHours < CampaignTime.HoursInDay)
            return $"Гонец отправился к {hero.Name}. Примерное время в пути: {travelHours} ч.";

        var days = Math.Max(1, (int)Math.Ceiling(travelHours / (double)CampaignTime.HoursInDay));
        return $"Гонец отправился к {hero.Name}. Примерное время в пути: {days} дн.";
    }
}
