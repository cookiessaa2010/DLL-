using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Native-menu family front end for the player clan. Navigation deliberately uses a
/// Campaign GameMenu rather than chaining inquiry popups: Bannerlord closes a
/// MultiSelection inquiry after invoking its callback, which otherwise also closes any
/// child popup opened from that callback.
/// </summary>
public sealed class KaiFamilyAffairsBehavior : CampaignBehaviorBase
{
    private const int MaximumLivingChildren = 6;
    private const string FamilyMenuId = "kaitor_family_affairs";
    private static string _returnMenuId = "town";

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
    }

    public override void SyncData(IDataStore dataStore)
    {
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        starter.AddGameMenu(
            FamilyMenuId,
            "Здесь решаются дела вашего дома: родственные узы, брачные союзы и принятие новых членов семьи.",
            FamilyMenuInit,
            GameMenu.MenuOverlayType.None,
            GameMenu.MenuFlags.None,
            null);

        starter.AddGameMenuOption(
            FamilyMenuId,
            "kaitor_family_house",
            "Мой род",
            ManageOptionCondition,
            _ => ShowHousehold(),
            false,
            0);

        starter.AddGameMenuOption(
            FamilyMenuId,
            "kaitor_family_marriages",
            "Брачные союзы",
            ManageOptionCondition,
            _ => ShowMarriageAffairs(),
            false,
            1);

        starter.AddGameMenuOption(
            FamilyMenuId,
            "kaitor_family_adopt",
            "Принять в род",
            AdoptionOptionCondition,
            _ => ShowAdoptionCandidates(),
            false,
            2);

        starter.AddGameMenuOption(
            FamilyMenuId,
            "kaitor_family_back",
            "Назад",
            BackOptionCondition,
            _ => ReturnFromFamilyMenu(),
            true,
            99);

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
            FamilyEntryCondition,
            _ => OpenFamilyMenu(),
            false,
            index);
    }

    private static void FamilyMenuInit(MenuCallbackArgs args)
    {
    }

    private static bool FamilyEntryCondition(MenuCallbackArgs args)
    {
        if (Hero.MainHero == null || Clan.PlayerClan == null)
            return false;

        args.optionLeaveType = GameMenuOption.LeaveType.Manage;
        return true;
    }

    private static bool ManageOptionCondition(MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Manage;
        return Hero.MainHero != null && Clan.PlayerClan != null;
    }

    private static bool AdoptionOptionCondition(MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Manage;
        return Hero.MainHero != null && Clan.PlayerClan != null;
    }

    private static bool BackOptionCondition(MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Leave;
        return true;
    }

    private static void OpenFamilyMenu()
    {
        var currentMenuId = Campaign.Current?.CurrentMenuContext?.GameMenu?.StringId;
        if (!string.IsNullOrWhiteSpace(currentMenuId) && !string.Equals(currentMenuId, FamilyMenuId, StringComparison.Ordinal))
            _returnMenuId = currentMenuId;

        GameMenu.SwitchToMenu(FamilyMenuId);
    }

    private static void ReturnFromFamilyMenu()
    {
        var target = string.IsNullOrWhiteSpace(_returnMenuId) ? "town" : _returnMenuId;
        GameMenu.SwitchToMenu(target);
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
            : "Брачный союз можно искать для: " + string.Join(", ", candidates) + ".";

        ShowText(
            "Брачные союзы",
            "Брак заключается через переговоры с главой другого знатного дома. Другие дома также могут первыми прислать предложение.\n\n" +
            "Союз без возможности биологических детей остаётся допустимым, но система предупредит об этом до окончательных договорённостей.\n\n" +
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
                null,
                true,
                $"Отношение: {Hero.MainHero.GetRelation(h):+0;-0;0}."))
            .ToList();

        if (candidates.Count == 0)
        {
            ShowText(
                "Принять в род",
                "Сейчас нет подходящего кандидата. Нужен взрослый спутник или член вашего клана той же расы, без родителей, супруга и детей.");
            return;
        }

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Принять в род",
                "Выберите человека. Нажатие «Принять в род» завершит церемонию и сделает выбранного героя вашим ребёнком и наследником.",
                candidates,
                true,
                1,
                1,
                "Принять в род",
                "Отмена",
                selected =>
                {
                    if (selected.Count == 0 || selected[0].Identifier is not Hero candidate)
                        return;

                    Adopt(candidate);
                },
                null),
            true,
            true);
    }

    private static void Adopt(Hero candidate)
    {
        if (!IsEligibleForAdoption(candidate, out var reason))
        {
            ShowQuick(reason, candidate);
            return;
        }

        try
        {
            var companionClan = candidate.CompanionOf;
            if (companionClan != null)
                RemoveCompanionAction.ApplyByByTurningToLord(companionClan, candidate);

            if (!candidate.IsLord)
                candidate.SetNewOccupation(Occupation.Lord);

            AdoptHeroAction.Apply(candidate);
            candidate.IsKnownToPlayer = true;

            ShowQuick(
                $"{candidate.Name} принят{(candidate.IsFemale ? "а" : string.Empty)} в ваш род и признан{(candidate.IsFemale ? "а" : string.Empty)} наследником.",
                candidate);
        }
        catch
        {
            ShowQuick("Сейчас принять этого человека в род невозможно.", candidate);
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

        var belongsToPlayerHouse = hero.CompanionOf == playerClan || hero.Clan == playerClan;
        if (!belongsToPlayerHouse)
        {
            reason = "Сначала этот человек должен присоединиться к вашему дому.";
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

        if (hero.CharacterObject?.Race != mainHero.CharacterObject.Race)
        {
            reason = "Для признания наследником требуется общая раса вашего рода.";
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

    private static void ShowQuick(string text, Hero hero = null)
    {
        MBInformationManager.AddQuickInformation(
            new TextObject(text),
            2500,
            hero?.CharacterObject,
            null,
            string.Empty);
    }
}
