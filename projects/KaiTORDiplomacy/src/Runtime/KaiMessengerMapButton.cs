using System;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Native map-bar messenger entry. It reuses a stock map-navigation visual so no
/// custom map prefab or texture override is required. The action itself is KaiTOR:
/// choose an available clan leader and open a native Bannerlord map conversation.
/// Marriage proposals therefore remain inside lord dialogue.
/// </summary>
[HarmonyPatch(typeof(MapNavigationVM))]
internal static class KaiMessengerMapButton
{
    [HarmonyPatch(MethodType.Constructor, typeof(INavigationHandler), typeof(Func<MapBarShortcuts>))]
    [HarmonyPostfix]
    private static void AddMessengerButton(MapNavigationVM __instance)
    {
        try
        {
            if (__instance?.NavigationItems == null)
                return;

            if (__instance.NavigationItems.Any(x => x?.NavigationElement is KaiMessengerNavigationElement))
                return;

            __instance.NavigationItems.Add(new MapNavigationItemVM(new KaiMessengerNavigationElement()));
            KaiRuntimeLog.Write("MESSENGER_BUTTON_READY", "map navigation item added");
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("MESSENGER_BUTTON_FAILED", ex, "stage=map_navigation_ctor");
        }
    }

    private sealed class KaiMessengerNavigationElement : INavigationElement
    {
        // Use the stock quest/scroll icon so the map bar can render this item safely.
        public string StringId => "quest";

        public NavigationPermissionItem Permission
        {
            get
            {
                if (Campaign.Current == null || Hero.MainHero == null || MobileParty.MainParty == null)
                    return new NavigationPermissionItem(false, new TextObject("Кампания не запущена."));
                if (Hero.MainHero.IsPrisoner)
                    return new NavigationPermissionItem(false, new TextObject("Из плена нельзя отправить гонца."));
                if (Campaign.Current.ConversationManager?.IsConversationInProgress == true)
                    return new NavigationPermissionItem(false, new TextObject("Сначала завершите текущий разговор."));
                if (MobileParty.MainParty.MapEvent != null || MobileParty.MainParty.BesiegedSettlement != null)
                    return new NavigationPermissionItem(false, new TextObject("Во время боя или осады гонец недоступен."));
                return new NavigationPermissionItem(true, TextObject.GetEmpty());
            }
        }

        public bool IsLockingNavigation => false;
        public bool IsActive => false;
        public TextObject Tooltip => new TextObject("Гонец — связаться с главой другого дома");
        public bool HasAlert => false;
        public TextObject AlertTooltip => TextObject.GetEmpty();

        public void OpenView() => OpenMessenger();
        public void OpenView(params object[] parameters) => OpenMessenger();
        public void GoToLink() => OpenMessenger();
    }

    private static void OpenMessenger()
    {
        if (!new KaiMessengerNavigationElement().Permission.IsAuthorized)
            return;

        var playerClan = Clan.PlayerClan;
        var candidates = Clan.All
            .Where(c => c != null &&
                        c != playerClan &&
                        !c.IsEliminated &&
                        !c.IsBanditFaction &&
                        !c.IsRebelClan &&
                        c.Leader != null &&
                        c.Leader.IsAlive &&
                        !c.Leader.IsPrisoner &&
                        c.Leader.IsActive)
            .Select(c => c.Leader)
            .Distinct()
            .OrderBy(h => h.MapFaction?.Name?.ToString() ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(h => h.Clan?.Name?.ToString() ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(h => h.Name?.ToString() ?? string.Empty, StringComparer.Ordinal)
            .Select(h => new InquiryElement(
                h,
                h.Name.ToString(),
                null,
                true,
                $"{h.Clan?.Name} — {h.MapFaction?.Name}"))
            .ToList();

        if (candidates.Count == 0)
        {
            ShowQuick("Нет доступных глав домов, с которыми можно связаться через гонца.");
            return;
        }

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Отправить гонца",
                "Выберите главу дома. После выбора откроется обычный диалог с лордом; брачные предложения делаются только через этот диалог.",
                candidates,
                true,
                1,
                1,
                "Связаться",
                "Отмена",
                selected =>
                {
                    if (selected.Count == 0 || selected[0].Identifier is not Hero lord)
                        return;
                    OpenLordConversation(lord);
                },
                null),
            true,
            true);
    }

    private static void OpenLordConversation(Hero lord)
    {
        try
        {
            if (lord == null || !lord.IsAlive || lord.IsPrisoner)
            {
                ShowQuick("Этот лорд сейчас недоступен.");
                return;
            }

            var playerData = new ConversationCharacterData(
                CharacterObject.PlayerCharacter,
                PartyBase.MainParty,
                false, false, false, false, false, false);

            var lordParty = lord.PartyBelongedTo?.Party;
            var lordData = new ConversationCharacterData(
                lord.CharacterObject,
                lordParty,
                false, false, false, false, false, false);

            KaiRuntimeLog.Write(
                "MESSENGER_CONTACT",
                $"lord={lord.StringId}; clan={lord.Clan?.StringId ?? "none"}; faction={lord.MapFaction?.StringId ?? "none"}");

            CampaignMapConversation.OpenConversation(playerData, lordData);
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("MESSENGER_CONTACT_FAILED", ex, $"lord={lord?.StringId ?? "null"}");
            ShowQuick("Не удалось связаться с лордом. Ошибка записана в KaiTOR.log.");
        }
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
