using System;
using HarmonyLib;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Shared messenger contact backend. UI ownership belongs to the Encyclopedia hero page,
/// matching the Diplomacy implementation integrated by Shokuho.
/// </summary>
internal static class KaiMessengerService
{
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

            var heroData = new ConversationCharacterData(
                hero.CharacterObject,
                hero.PartyBelongedTo?.Party,
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
