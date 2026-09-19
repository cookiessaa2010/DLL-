using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.Core;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Places the courier action on the lord's encyclopedia page, matching the
/// Diplomacy/Shokuho interaction flow. No map-navigation button is created.
/// </summary>
internal static class KaiMessengerMapButton
{
    private const string LinkPrefix = "kaitor-messenger:";

    [HarmonyPatch(typeof(EncyclopediaHeroPageVM), "UpdateInformationText")]
    private static class EncyclopediaInformationPatch
    {
        [HarmonyPostfix]
        private static void Postfix(EncyclopediaHeroPageVM __instance, Hero ____hero)
        {
            try
            {
                var target = ____hero;
                var behavior = Campaign.Current?.GetCampaignBehavior<KaiMessengerBehavior>();
                if (__instance == null || target == null || behavior == null || target == Hero.MainHero || !target.IsLord)
                    return;

                string action;
                if (behavior.HasMessengerFor(target))
                {
                    action = "Гонец уже в пути.";
                }
                else if (behavior.CanSend(target, out var reason))
                {
                    var cost = behavior.GetSendCost(target);
                    action = HyperlinkTexts.GetGenericHyperlinkText(
                        LinkPrefix + target.StringId,
                        $"Отправить гонца ({cost:N0} д.)");
                }
                else
                {
                    action = $"Гонец недоступен: {reason}";
                }

                __instance.InformationText = (__instance.InformationText ?? string.Empty) + "\n\n" + action;
            }
            catch (Exception ex)
            {
                KaiRuntimeLog.Exception("MESSENGER_ENCYCLOPEDIA_FAILED", ex, "stage=information_patch");
            }
        }
    }

    [HarmonyPatch(typeof(EncyclopediaHeroPageVM), nameof(EncyclopediaHeroPageVM.ExecuteLink))]
    private static class EncyclopediaLinkPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(string link)
        {
            if (string.IsNullOrWhiteSpace(link) || !link.StartsWith(LinkPrefix, StringComparison.Ordinal))
                return true;

            try
            {
                var heroId = link.Substring(LinkPrefix.Length);
                Hero target = null;
                foreach (var hero in Hero.AllAliveHeroes)
                {
                    if (hero != null && string.Equals(hero.StringId, heroId, StringComparison.Ordinal))
                    {
                        target = hero;
                        break;
                    }
                }

                var behavior = Campaign.Current?.GetCampaignBehavior<KaiMessengerBehavior>();
                if (behavior == null || target == null)
                {
                    KaiRuntimeLog.Write("MESSENGER_ENCYCLOPEDIA_FAILED", $"stage=link; target={heroId}; behavior={(behavior != null ? "ok" : "missing")}");
                    return false;
                }

                behavior.RequestSend(target);
            }
            catch (Exception ex)
            {
                KaiRuntimeLog.Exception("MESSENGER_ENCYCLOPEDIA_FAILED", ex, $"stage=link; link={link}");
            }

            return false;
        }
    }
}
