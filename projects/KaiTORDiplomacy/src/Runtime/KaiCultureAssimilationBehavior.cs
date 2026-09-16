using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

public sealed class KaiCultureAssimilationBehavior : CampaignBehaviorBase
{
    public const int CultureChangeCost = 100000;
    public const int RequiredClanTier = 3;
    private const string TorSpecialSettlementId = "castle_BK1";

    private bool _runtimeEnabled;

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
    }

    public override void SyncData(IDataStore dataStore)
    {
        // Settlement culture persistence remains owned by TOR's AssimilationCampaignBehavior.
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        _runtimeEnabled = TorCompatibilityGate.TryValidate(out _);
        RegisterMenuOptions(starter);
    }

    private void RegisterMenuOptions(CampaignGameStarter starter)
    {
        // Vanilla can expose either town or town_outside depending on the entry route.
        // TOR uses town_outside for the city panel shown in the live test, so register
        // the same player-facing action in both states.
        AddCultureMenuOption(starter, "town", "kaitor_change_town_nationality", 7);
        AddCultureMenuOption(starter, "town_outside", "kaitor_change_town_outside_nationality", 7);
        AddCultureMenuOption(starter, "castle", "kaitor_change_castle_nationality", 7);
    }

    private void AddCultureMenuOption(CampaignGameStarter starter, string menuId, string optionId, int index)
    {
        starter.AddGameMenuOption(
            menuId,
            optionId,
            "Сменить народность поселения",
            CultureMenuCondition,
            CultureMenuConsequence,
            false,
            index);
    }

    private bool CultureMenuCondition(MenuCallbackArgs args)
    {
        var settlement = Settlement.CurrentSettlement;
        if (!_runtimeEnabled || settlement == null || !settlement.IsFortification) return false;
        if (settlement.OwnerClan != Clan.PlayerClan) return false;

        args.optionLeaveType = GameMenuOption.LeaveType.Manage;

        if (IsTorSpecialSettlement(settlement))
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("Это особое поселение и его народность не может быть изменена обычным способом.");
            return true;
        }

        if (settlement.IsUnderSiege)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("Нельзя менять народность поселения во время осады.");
            return true;
        }

        if (Clan.PlayerClan == null || Clan.PlayerClan.Tier < RequiredClanTier)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject($"Для смены народности требуется уровень клана {RequiredClanTier} или выше.");
            return true;
        }

        var targetCulture = GetPlayerClanCulture();
        if (targetCulture == null)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("Не удалось определить народность вашего клана.");
            return true;
        }

        if (settlement.Culture == targetCulture)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("Поселение уже принадлежит народности вашего клана.");
            return true;
        }

        if (!TorSettlementCultureBridge.ValidateFullConversion(targetCulture, settlement, out var supportReason))
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("Полная смена народности здесь недоступна: " + supportReason);
            return true;
        }

        if (Hero.MainHero == null || Hero.MainHero.Gold < CultureChangeCost)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject($"Требуется {CultureChangeCost:N0} динаров.");
            return true;
        }

        args.Tooltip = new TextObject(
            $"Полностью сменить народность {settlement.Name} на {targetCulture.Name}. " +
            "Изменятся культура города или замка, связанных деревень, местных знатных жителей, рекрутов, таверненных наёмников, караванов, ополчения и дальнейшего населения. " +
            "Именные лорды других кланов и уникальные персонажи особых локаций не изменяются.");
        return true;
    }

    private void CultureMenuConsequence(MenuCallbackArgs args)
    {
        var settlement = Settlement.CurrentSettlement;
        var targetCulture = GetPlayerClanCulture();
        if (settlement == null || targetCulture == null) return;

        var title = "Смена народности поселения";
        var body = $"Полностью сменить народность {settlement.Name} на {targetCulture.Name}?\n\n" +
                   $"Стоимость: {CultureChangeCost:N0} динаров\n" +
                   $"Требование: уровень клана {RequiredClanTier}+\n\n" +
                   "Будут обновлены связанные деревни, местные знатные жители, набор рекрутов, таверненные наёмники, будущие странники, караваны, культурные службы и дальнейшее производство.\n\n" +
                   "Уникальные персонажи и объекты, привязанные к конкретным локациям, сохраняются. Именные лорды и герои других кланов не меняют народность. Уже лежащие на рынке товары старой культуры останутся до продажи или использования.";

        InformationManager.ShowInquiry(
            new InquiryData(
                title,
                body,
                true,
                true,
                "Сменить народность",
                "Отмена",
                () =>
                {
                    if (TryChangeCultureImmediately(settlement, out var reason))
                        InformationManager.DisplayMessage(new InformationMessage(reason));
                    else
                        InformationManager.DisplayMessage(new InformationMessage("Смена народности не выполнена: " + reason));
                },
                null,
                string.Empty,
                0f,
                null,
                null,
                null),
            false,
            false);
    }

    public bool TryChangeCultureImmediately(Settlement settlement, out string reason)
    {
        reason = string.Empty;

        if (!_runtimeEnabled) { reason = "Дипломатическая система совместимости не активна."; return false; }
        if (settlement == null || !settlement.IsFortification) { reason = "Необходимо находиться в городе или замке."; return false; }
        if (settlement.OwnerClan != Clan.PlayerClan) { reason = "Можно менять народность только поселений вашего клана."; return false; }
        if (IsTorSpecialSettlement(settlement)) { reason = "Это особое поселение и его народность не может быть изменена обычным способом."; return false; }
        if (settlement.IsUnderSiege) { reason = "Нельзя менять народность поселения во время осады."; return false; }
        if (Clan.PlayerClan == null || Clan.PlayerClan.Tier < RequiredClanTier) { reason = $"Требуется уровень клана {RequiredClanTier} или выше."; return false; }

        var targetCulture = GetPlayerClanCulture();
        if (targetCulture == null) { reason = "Не удалось определить народность вашего клана."; return false; }
        if (settlement.Culture == targetCulture) { reason = "Поселение уже принадлежит народности вашего клана."; return false; }
        if (Hero.MainHero == null || Hero.MainHero.Gold < CultureChangeCost) { reason = $"Требуется {CultureChangeCost:N0} динаров."; return false; }

        if (!TorSettlementCultureBridge.ValidateFullConversion(targetCulture, settlement, out reason)) return false;

        var affected = GetAffectedSettlements(settlement).ToArray();
        foreach (var affectedSettlement in affected) ApplyCultureToSettlementAndNotables(affectedSettlement, targetCulture);

        if (!TorSettlementCultureBridge.RefreshAfterCultureChange(settlement, targetCulture, out reason))
        {
            reason = "Основная народность изменена, но не удалось обновить одну из связанных систем. Сохраните диагностические данные перед продолжением: " + reason;
            return false;
        }

        GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, CultureChangeCost, false);

        reason = $"Народность {settlement.Name} полностью изменена на {targetCulture.Name}. Потрачено {CultureChangeCost:N0} динаров. " +
                 "Связанные деревни, набор рекрутов, таверненное население, караваны, культурные службы и дальнейшее производство обновлены.";
        return true;
    }

    public string DescribeCurrentSettlement()
    {
        var settlement = Settlement.CurrentSettlement;
        if (settlement == null) return "No settlement is currently open.";
        var clanCulture = GetPlayerClanCulture();
        return $"{settlement.StringId}: settlement culture={settlement.Culture?.StringId ?? "<null>"}, " +
               $"player clan culture={clanCulture?.StringId ?? "<null>"}, owner clan={settlement.OwnerClan?.StringId ?? "<null>"}, " +
               $"player clan tier={Clan.PlayerClan?.Tier.ToString() ?? "<null>"}.";
    }

    public IEnumerable<string> DescribeCultureSupport() => TorSettlementCultureBridge.DescribePlayableCultureSupport();

    private static CultureObject GetPlayerClanCulture() => Clan.PlayerClan?.Culture ?? Hero.MainHero?.Culture;

    private static IEnumerable<Settlement> GetAffectedSettlements(Settlement settlement)
    {
        yield return settlement;
        if (settlement.BoundVillages == null) yield break;
        foreach (var village in settlement.BoundVillages)
            if (village?.Settlement != null) yield return village.Settlement;
    }

    private static void ApplyCultureToSettlementAndNotables(Settlement settlement, CultureObject targetCulture)
    {
        settlement.Culture = targetCulture;
        foreach (var notable in settlement.Notables.ToList())
        {
            if (notable == null || notable.Culture == targetCulture) continue;
            var occupation = notable.Occupation;
            KillCharacterAction.ApplyByRemove(notable);
            var replacement = HeroCreator.CreateNotable(occupation, settlement);
            if (replacement != null) EnterSettlementAction.ApplyForCharacterOnly(replacement, settlement);
        }
    }

    private static bool IsTorSpecialSettlement(Settlement settlement)
        => string.Equals(settlement?.StringId, TorSpecialSettlementId, StringComparison.Ordinal);
}
