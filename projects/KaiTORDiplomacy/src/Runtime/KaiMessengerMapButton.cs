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
/// Messenger backend shared by the campaign-map shortcut and the Shokuho-style
/// Encyclopedia hero-page button. The important v0.6.5.3 change is that the
/// target is a concrete Hero, not only a clan leader.
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
        public TextObject Tooltip => new TextObject("Гонец — связаться с лордом или леди");
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

        var candidates = Clan.All
            .Where(c => c != null && !c.IsEliminated && !c.IsBanditFaction && !c.IsRebelClan)
            .SelectMany(c => c.Heroes ?? Enumerable.Empty<Hero>())
            .Where(h => h != null && h.IsLord && CanContactHero(h, out _))
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
            ShowQuick("Нет доступных лордов или леди, с которыми можно связаться через гонца.");
            return;
        }

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Отправить гонца",
                "Выберите персонажа. После выбора откроется обычный диалог с ним; брачные предложения остаются внутри диалога.",
                candidates,
                true,
                1,
                1,
                "Связаться",
                "Отмена",
                selected =>
                {
                    if (selected.Count == 0 || selected[0].Identifier is not Hero hero)
                        return;
                    ContactHero(hero);
                },
                null),
            true,
            true);
    }

    internal static bool CanContactHero(Hero hero, out TextObject reason)
    {
        reason = TextObject.GetEmpty();

        if (Campaign.Current == null || Hero.MainHero == null || MobileParty.MainParty == null)
        {
            reason = new TextObject("Кампания не запущена.");
            return false;
        }

        if (hero == null || hero.CharacterObject == null)
        {
            reason = new TextObject("Персонаж недоступен.");
            return false;
        }

        if (hero == Hero.MainHero || hero.IsHumanPlayerCharacter)
        {
            reason = new TextObject("Нельзя отправить гонца самому себе.");
            return false;
        }

        if (!hero.IsAlive)
        {
            reason = new TextObject("Этот персонаж мёртв.");
            return false;
        }

        if (!hero.IsActive)
        {
            reason = new TextObject("Этот персонаж сейчас неактивен.");
            return false;
        }

        if (hero.IsChild)
        {
            reason = new TextObject("К этому персонажу нельзя отправить гонца, пока он ребёнок.");
            return false;
        }

        if (hero.IsPrisoner)
        {
            reason = new TextObject("Этот персонаж находится в плену.");
            return false;
        }

        if (hero.IsFugitive)
        {
            reason = new TextObject("Этот персонаж скрывается и сейчас недоступен.");
            return false;
        }

        if (Hero.MainHero.IsPrisoner)
        {
            reason = new TextObject("Из плена нельзя отправить гонца.");
            return false;
        }

        if (Campaign.Current.ConversationManager?.IsConversationInProgress == true)
        {
            reason = new TextObject("Сначала завершите текущий разговор.");
            return false;
        }

        if (MobileParty.MainParty.MapEvent != null || MobileParty.MainParty.BesiegedSettlement != null)
        {
            reason = new TextObject("Во время боя или осады гонец недоступен.");
            return false;
        }

        return true;
    }

    internal static void ContactHero(Hero hero)
    {
        try
        {
            if (!CanContactHero(hero, out var reason))
            {
                ShowQuick(reason?.ToString() ?? "Этот персонаж сейчас недоступен.");
                return;
            }

            TryCloseEncyclopedia();

            var playerData = new ConversationCharacterData(
                CharacterObject.PlayerCharacter,
                PartyBase.MainParty,
                false, false, false, false, false, false);

            var heroParty = hero.PartyBelongedTo?.Party;
            var heroData = new ConversationCharacterData(
                hero.CharacterObject,
                heroParty,
                false, false, false, false, false, false);

            KaiRuntimeLog.Write(
                "MESSENGER_CONTACT",
                $"hero={hero.StringId}; clan={hero.Clan?.StringId ?? "none"}; faction={hero.MapFaction?.StringId ?? "none"}; female={hero.IsFemale}; dawi={KaiRaceLifecycle.IsDawi(hero)}");

            CampaignMapConversation.OpenConversation(playerData, heroData);
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("MESSENGER_CONTACT_FAILED", ex, $"hero={hero?.StringId ?? "null"}");
            ShowQuick("Не удалось связаться с персонажем. Ошибка записана в KaiTOR.log.");
        }
    }

    /// <summary>
    /// Encyclopedia is a map overlay rather than a standalone ScreenBase. Keep this
    /// reflection-only so the diplomacy DLL does not need a hard compile reference
    /// to SandBox.View just to close the overlay before opening a map conversation.
    /// </summary>
    private static void TryCloseEncyclopedia()
    {
        try
        {
            var mapScreenType = AccessTools.TypeByName("SandBox.View.Map.MapScreen");
            var instance = AccessTools.Property(mapScreenType, "Instance")?.GetValue(null);
            if (instance == null)
                return;

            var manager = AccessTools.Property(mapScreenType, "EncyclopediaScreenManager")?.GetValue(instance);
            if (manager == null)
                return;

            AccessTools.Method(manager.GetType(), "CloseEncyclopedia")?.Invoke(manager, null);
            KaiRuntimeLog.Write("MESSENGER_ENCYCLOPEDIA_CLOSE", "closed_before_contact=true");
        }
        catch (Exception ex)
        {
            // Closing the encyclopedia is best-effort. Never block messenger contact.
            KaiRuntimeLog.Exception("MESSENGER_ENCYCLOPEDIA_CLOSE_FAILED", ex);
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
