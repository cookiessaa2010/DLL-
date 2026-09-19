using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KaiTOR.Diplomacy.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.UI;

/// <summary>
/// Injects a safe rich-text hyperlink into the hero encyclopedia information block.
/// No TOR prefab is replaced and no dynamic Gauntlet widget tree is introduced.
/// </summary>
[HarmonyPatch]
internal static class KaiMessengerEncyclopediaPatch
{
    private const string LinkPrefix = "kaitor-messenger:";
    private static readonly ConditionalWeakTable<EncyclopediaHeroPageVM, Holder> Heroes = new();

    private sealed class Holder
    {
        public Hero Hero;
    }

    [HarmonyPatch(typeof(EncyclopediaHeroPageVM), MethodType.Constructor, typeof(EncyclopediaPageArgs))]
    [HarmonyPostfix]
    private static void ConstructorPostfix(EncyclopediaHeroPageVM __instance, EncyclopediaPageArgs args)
    {
        if (args.Obj is Hero hero)
        {
            Heroes.Remove(__instance);
            Heroes.Add(__instance, new Holder { Hero = hero });
        }
    }

    [HarmonyPatch(typeof(EncyclopediaHeroPageVM), nameof(EncyclopediaHeroPageVM.Refresh))]
    [HarmonyPostfix]
    private static void RefreshPostfix(EncyclopediaHeroPageVM __instance)
    {
        if (__instance == null || !Heroes.TryGetValue(__instance, out var holder) || holder?.Hero == null)
            return;

        var messenger = Campaign.Current?.GetCampaignBehavior<KaiMessengerBehavior>();
        if (messenger == null || !messenger.CanSendTo(holder.Hero, out _))
            return;

        var label = KaiTORDiplomacyUiText.Get(
            "kaitor_diplomacy_ui_send_messenger",
            "Send Messenger");

        var link = HyperlinkTexts.GetGenericHyperlinkText(
            LinkPrefix + holder.Hero.StringId,
            label);

        var current = __instance.InformationText ?? string.Empty;
        if (!current.Contains(LinkPrefix, StringComparison.Ordinal))
            __instance.InformationText = string.IsNullOrWhiteSpace(current)
                ? link
                : current + Environment.NewLine + Environment.NewLine + link;
    }

    [HarmonyPatch(typeof(EncyclopediaHeroPageVM), nameof(EncyclopediaHeroPageVM.ExecuteLink))]
    [HarmonyPrefix]
    private static bool ExecuteLinkPrefix(string link)
    {
        if (string.IsNullOrWhiteSpace(link) ||
            !link.StartsWith(LinkPrefix, StringComparison.Ordinal))
            return true;

        var heroId = link.Substring(LinkPrefix.Length);
        var messenger = Campaign.Current?.GetCampaignBehavior<KaiMessengerBehavior>();
        if (messenger == null)
        {
            InformationManager.DisplayMessage(new InformationMessage("KaiTOR Messenger is not loaded."));
            return false;
        }

        if (!messenger.TrySendByHeroId(heroId, out var reason))
            InformationManager.DisplayMessage(new InformationMessage(reason));

        return false;
    }
}
