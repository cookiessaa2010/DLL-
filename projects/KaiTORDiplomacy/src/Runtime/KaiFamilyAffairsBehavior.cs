using System;
using System.Collections.Generic;
using System.Linq;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Native-menu family front end for the player clan. It deliberately reuses
/// Bannerlord's public AdoptHeroAction instead of writing family graph fields directly.
/// Adoption converts an existing player companion into a noble family member, which
/// makes the hero visible to the vanilla marriage-offer and arranged-marriage systems.
/// </summary>
public sealed class KaiFamilyAffairsBehavior : CampaignBehaviorBase
{
    private const int MaximumLivingChildren = 6;

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
    }

    public override void SyncData(IDataStore dataStore)
    {
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        AddFamilyMenuOption(starter, "town", "kaitor_family_affairs_town", 9);
        AddFamilyMenuOption(starter, "town_outside", "kaitor_family_affairs_town_outside", 9);
        AddFamilyMenuOption(starter, "castle", "kaitor_family_affairs_castle", 9);
    }

    private static void AddFamilyMenuOption(CampaignGameStarter starter, string menuId, string optionId, int index)
    {
        starter.AddGameMenuOption(
            menuId,
            optionId,
            "Семейные дела",
            FamilyMenuCondition,
            _ => ShowFamilyAffairs(),
            false,
            index);
    }

    private static bool FamilyMenuCondition(MenuCallbackArgs args)
    {
        if (Hero.MainHero == null || Clan.PlayerClan == null)
            return false;

        args.optionLeaveType = GameMenuOption.LeaveType.Manage;
        return true;
    }

    private static void ShowFamilyAffairs()
    {
        var candidates = GetAdoptionCandidates().ToArray();
        var canAdoptMore = CanAdoptMoreChildren();

        var options = new List<InquiryElement>
        {
            new("house", "Мой род", null),
            new("marriages", "Брачные союзы", null),
            new(
                "adopt",
                "Принять в род",
                null,
                canAdoptMore && candidates.Length > 0,
                !canAdoptMore
                    ? "Ваш дом уже достаточно велик."
                    : candidates.Length == 0
                        ? "Среди ваших спутников сейчас нет подходящего человека, которого можно принять в род."
                        : string.Empty),
        };

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Семейные дела",
                "Здесь решаются дела вашего дома: родственные узы, брачные союзы и принятие новых членов семьи.",
                options,
                true,
                1,
                1,
                "Открыть",
                "Закрыть",
                selected =>
                {
                    if (selected.Count == 0) return;
                    switch (selected[0].Identifier as string)
                    {
                        case "house": ShowHousehold(); break;
                        case "marriages": ShowMarriageAffairs(); break;
                        case "adopt": ShowAdoptionCandidates(); break;
                    }
                },
                null),
            true,
            true);
    }

    private static void ShowHousehold()
    {
        var mainHero = Hero.MainHero;
        if (mainHero == null)
            return;

        var lines = new List<string>();

        if (mainHero.Spouse != null && mainHero.Spouse.IsAlive)
            lines.Add($"Супруг: {mainHero.Spouse.Name}.");
        else
            lines.Add("Супруг: нет.");

        var children = mainHero.Children
            .Where(h => h != null && h.IsAlive)
            .OrderByDescending(h => h.Age)
            .ThenBy(h => h.Name.ToString())
            .ToArray();

        if (children.Length == 0)
        {
            lines.Add("В вашем доме пока нет детей или принятых в род наследников.");
        }
        else
        {
            lines.Add("Члены семьи:");
            foreach (var child in children)
            {
                var marriage = child.Spouse != null && child.Spouse.IsAlive
                    ? $", в браке с {child.Spouse.Name}"
                    : child.CanMarry() ? ", свободен для брачного союза" : string.Empty;
                lines.Add($"• {child.Name}, {Math.Max(0, (int)child.Age)} лет{marriage}.");
            }
        }

        ShowText("Мой род", string.Join("\n", lines));
    }

    private static void ShowMarriageAffairs()
    {
        var candidates = Clan.PlayerClan?.Heroes
            .Where(h => h != null && h != Hero.MainHero && h.IsAlive && h.CanMarry())
            .OrderBy(h => h.Name.ToString())
            .Select(h => h.Name.ToString())
            .Take(12)
            .ToArray() ?? Array.Empty<string>();

        var candidateText = candidates.Length == 0
            ? "Сейчас в вашем доме нет свободных взрослых членов семьи, для которых можно искать брачный союз."
            : "Сейчас брачный союз можно искать для: " + string.Join(", ", candidates) + ".";

        ShowText(
            "Брачные союзы",
            "Поговорите с главой другого знатного дома, чтобы предложить брак для себя или члена семьи. Другие дома также могут первыми прислать предложение, и оно появится отдельным известием на карте.\n\n" +
            "Женщина из вашего дома может заключить брачный союз с другой женщиной. Такой союз не даёт кровных наследников, но семья может продолжаться через принятие детей в род.\n\n" +
            candidateText);
    }

    private static void ShowAdoptionCandidates()
    {
        if (!CanAdoptMoreChildren())
        {
            ShowText("Принять в род", "Ваш дом уже достаточно велик, чтобы принимать новых наследников.");
            return;
        }

        var candidates = GetAdoptionCandidates()
            .OrderByDescending(h => Hero.MainHero.GetRelation(h))
            .ThenBy(h => h.Name.ToString())
            .Select(h => new InquiryElement(
                h,
                $"{h.Name}, {Math.Max(0, (int)h.Age)} лет",
                h.CharacterObject,
                true,
                $"Отношение: {Hero.MainHero.GetRelation(h):+0;-0;0}."))
            .ToList();

        if (candidates.Count == 0)
        {
            ShowText(
                "Принять в род",
                "Среди ваших спутников сейчас нет подходящего человека. Взрослый спутник должен быть свободен от других семейных уз и принадлежать к той же народности, что и вы.");
            return;
        }

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Принять в род",
                "Выберите спутника, которого вы хотите признать своим ребёнком и полноправным членом знатного дома.",
                candidates,
                true,
                1,
                1,
                "Выбрать",
                "Отмена",
                selected =>
                {
                    if (selected.Count == 0 || selected[0].Identifier is not Hero candidate)
                        return;
                    ConfirmAdoption(candidate);
                },
                null),
            true,
            true);
    }

    private static void ConfirmAdoption(Hero candidate)
    {
        if (!IsEligibleForAdoption(candidate, out var reason))
        {
            ShowText("Принять в род", reason);
            return;
        }

        InformationManager.ShowInquiry(new InquiryData(
            "Принять в род",
            $"Признать {candidate.Name} своим ребёнком и наследником вашего дома? После этого {candidate.Name} станет знатным членом семьи и сможет участвовать в династических браках.",
            true,
            true,
            candidate.IsFemale ? "Удочерить" : "Усыновить",
            "Отмена",
            () => Adopt(candidate),
            null), false, false);
    }

    private static void Adopt(Hero candidate)
    {
        if (!IsEligibleForAdoption(candidate, out var reason))
        {
            ShowText("Принять в род", reason);
            return;
        }

        try
        {
            var companionClan = candidate.CompanionOf;
            if (companionClan != null)
                RemoveCompanionAction.ApplyByByTurningToLord(companionClan, candidate);

            if (!candidate.IsLord)
                candidate.SetNewOccupation(Occupation.Lord);

            candidate.Clan = Clan.PlayerClan;
            AdoptHeroAction.Apply(candidate);
            candidate.IsKnownToPlayer = true;

            ShowText(
                "Новый член семьи",
                $"{candidate.Name} отныне признан{(candidate.IsFemale ? "а" : string.Empty)} вашим ребёнком и полноправным членом рода. Знатные дома смогут предлагать для {(candidate.IsFemale ? "неё" : "него")} брачные союзы.");
        }
        catch
        {
            ShowText(
                "Семейные дела",
                "Церемонию пришлось отложить. Обстоятельства изменились, и сейчас принять этого человека в род невозможно.");
        }
    }

    private static IEnumerable<Hero> GetAdoptionCandidates()
    {
        if (Hero.MainHero?.CharacterObject == null || Clan.PlayerClan == null)
            yield break;

        foreach (var hero in Hero.AllAliveHeroes)
        {
            if (IsEligibleForAdoption(hero, out _))
                yield return hero;
        }
    }

    private static bool IsEligibleForAdoption(Hero hero, out string reason)
    {
        reason = string.Empty;

        var mainHero = Hero.MainHero;
        var playerClan = Clan.PlayerClan;
        if (mainHero?.CharacterObject == null || playerClan == null)
        {
            reason = "Сейчас семейные дела недоступны.";
            return false;
        }

        if (!CanAdoptMoreChildren())
        {
            reason = "Ваш дом уже достаточно велик.";
            return false;
        }

        if (hero == null || hero == mainHero || !hero.IsAlive || !hero.IsActive || hero.IsTemplate || hero.IsMinorFactionHero)
        {
            reason = "Этот человек не может быть принят в ваш род.";
            return false;
        }

        // Adoption is intentionally limited to an existing player companion. This
        // avoids stealing heroes from AI clans and avoids creating a new hero at runtime.
        if (hero.CompanionOf != playerClan)
        {
            reason = "Сначала этот человек должен стать вашим спутником.";
            return false;
        }

        if (hero.Age < Campaign.Current.Models.AgeModel.HeroComesOfAge)
        {
            reason = "Этот человек ещё слишком молод.";
            return false;
        }

        if (hero.Father != null || hero.Mother != null || hero.Spouse != null || hero.Children.Count > 0)
        {
            reason = "У этого человека уже есть собственные семейные узы.";
            return false;
        }

        if (hero.IsPrisoner || hero.PartyBelongedToAsPrisoner != null)
        {
            reason = "Нельзя провести церемонию, пока этот человек находится в плену.";
            return false;
        }

        if (hero.CharacterObject.Race != mainHero.CharacterObject.Race)
        {
            reason = "Для признания наследником требуется общая народность вашего рода.";
            return false;
        }

        return true;
    }

    private static bool CanAdoptMoreChildren()
        => Hero.MainHero != null && Hero.MainHero.Children.Count(h => h != null && h.IsAlive) < MaximumLivingChildren;

    private static void ShowText(string title, string body)
    {
        InformationManager.ShowInquiry(new InquiryData(
            title,
            body,
            true,
            false,
            "ОК",
            string.Empty,
            null,
            null), false, false);
    }
}
