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
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        _runtimeEnabled = TorCompatibilityGate.TryValidate(out _);
        RegisterMenuOptions(starter);
    }

    private void RegisterMenuOptions(CampaignGameStarter starter)
    {
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
            args.Tooltip = new TextObject("Это особое поселение. Его традиции нельзя изменить обычным переселением.");
            return true;
        }

        if (settlement.IsUnderSiege)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("Во время осады проводить переселение невозможно.");
            return true;
        }

        if (Clan.PlayerClan == null || Clan.PlayerClan.Tier < RequiredClanTier)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject($"Для такой реформы требуется клан не ниже {RequiredClanTier}-го уровня.");
            return true;
        }

        var targetCulture = GetPlayerClanCulture();
        if (targetCulture == null)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("Сейчас невозможно определить, какую народность должен принять город.");
            return true;
        }

        if (settlement.Culture == targetCulture)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("Большинство жителей уже принадлежит к народности вашего рода.");
            return true;
        }

        if (!TorSettlementCultureBridge.ValidateFullConversion(targetCulture, settlement, out _))
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("Для этой народности пока невозможно провести полную реформу населения этого поселения.");
            return true;
        }

        if (Hero.MainHero == null || Hero.MainHero.Gold < CultureChangeCost)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject($"Для переселения и перестройки управления требуется {CultureChangeCost:N0} динаров.");
            return true;
        }

        args.Tooltip = new TextObject(
            $"Начать переселение и утвердить в {settlement.Name} народность {targetCulture.Name}. " +
            "Изменения затронут связанные деревни, местную знать, рекрутов, ополчение, караваны, таверны и дальнейшее развитие поселения.");
        return true;
    }

    private void CultureMenuConsequence(MenuCallbackArgs args)
    {
        var settlement = Settlement.CurrentSettlement;
        var targetCulture = GetPlayerClanCulture();
        if (settlement == null || targetCulture == null) return;

        var body = $"Начать переселение и утвердить в {settlement.Name} народность {targetCulture.Name}?\n\n" +
                   $"Расходы: {CultureChangeCost:N0} динаров\n\n" +
                   "Реформа изменит жизнь города или замка и связанных деревень. Постепенно сменятся местная знать, набор рекрутов, ополчение, наёмники, караваны и другие жители, связанные с поселением.\n\n" +
                   "Именные лорды чужих кланов и уникальные персонажи останутся прежними.";

        InformationManager.ShowInquiry(
            new InquiryData(
                "Смена народности поселения",
                body,
                true,
                true,
                "Начать переселение",
                "Отмена",
                () =>
                {
                    if (TryChangeCultureImmediately(settlement, out var reason))
                        InformationManager.DisplayMessage(new InformationMessage(reason));
                    else
                        InformationManager.DisplayMessage(new InformationMessage(reason));
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

    public void OpenCultureChangeDialog()
    {
        CultureMenuConsequence(null);
    }

    public bool TryChangeCultureImmediately(Settlement settlement, out string reason)
    {
        reason = string.Empty;

        if (!_runtimeEnabled) { reason = "Сейчас провести эту реформу невозможно."; return false; }
        if (settlement == null || !settlement.IsFortification) { reason = "Такое решение можно принять только в городе или замке."; return false; }
        if (settlement.OwnerClan != Clan.PlayerClan) { reason = "Вы можете менять народность только в собственных владениях."; return false; }
        if (IsTorSpecialSettlement(settlement)) { reason = "Традиции этого особого владения нельзя изменить обычным переселением."; return false; }
        if (settlement.IsUnderSiege) { reason = "Во время осады переселение невозможно."; return false; }
        if (Clan.PlayerClan == null || Clan.PlayerClan.Tier < RequiredClanTier) { reason = $"Для такой реформы требуется клан не ниже {RequiredClanTier}-го уровня."; return false; }

        var targetCulture = GetPlayerClanCulture();
        if (targetCulture == null) { reason = "Сейчас невозможно определить народность вашего рода."; return false; }
        if (settlement.Culture == targetCulture) { reason = "Поселение уже принадлежит к народности вашего рода."; return false; }
        if (Hero.MainHero == null || Hero.MainHero.Gold < CultureChangeCost) { reason = $"Для реформы требуется {CultureChangeCost:N0} динаров."; return false; }

        if (!TorSettlementCultureBridge.ValidateFullConversion(targetCulture, settlement, out _))
        {
            reason = "Для этой народности пока невозможно безопасно провести полную реформу населения здесь.";
            return false;
        }

        var affected = GetAffectedSettlements(settlement).ToArray();
        foreach (var affectedSettlement in affected) ApplyCultureToSettlementAndNotables(affectedSettlement, targetCulture);

        if (!TorSettlementCultureBridge.RefreshAfterCultureChange(settlement, targetCulture, out _))
        {
            reason = "Переселение началось, но часть городских служб не успела перестроиться. Не продолжайте игру с этого сохранения и сообщите об ошибке.";
            return false;
        }

        GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, CultureChangeCost, false);

        reason = $"В {settlement.Name} утверждена народность {targetCulture.Name}. На переселение и перестройку управления потрачено {CultureChangeCost:N0} динаров.";
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
