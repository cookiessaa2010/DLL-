using System;
using System.Collections.Generic;
using System.Linq;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Stable native-menu family front end for the player clan.
/// Multi-step workflows always return to a GameMenu between selection inquiries so
/// Bannerlord never has to open one inquiry from another inquiry callback.
/// </summary>
public sealed class KaiFamilyAffairsBehavior : CampaignBehaviorBase
{
    private const int MaximumLivingChildren = 6;

    private const string FamilyMenuId = "kaitor_family_affairs";
    private const string HouseMenuId = "kaitor_family_house";
    private const string MarriageMenuId = "kaitor_family_marriage";
    private const string AdoptionMenuId = "kaitor_family_adoption";

    private static string _returnMenuId = "town_tavern";
    private static Hero _selectedHouseMember;
    private static Clan _selectedTargetClan;
    private static Hero _selectedTargetHero;

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        CampaignEvents.BeforeHeroesMarried.AddNonSerializedListener(this, OnBeforeHeroesMarried);
    }

    public override void SyncData(IDataStore dataStore)
    {
        // Selection state is UI-only and deliberately not persisted.
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        // Current UX: no family/diplomacy submenus in towns or castles.
        // Adoption is the only settlement family action and lives directly in the tavern.
        // Marriage is handled by Bannerlord's native lord dialogue.
        ResetMarriageSelection();
        RegisterAdoptionMenu(starter);

        starter.AddGameMenuOption(
            "town_tavern",
            "kaitor_family_adoption_tavern",
            "Принять в род",
            FamilyEntryCondition,
            _ =>
            {
                _returnMenuId = "town_tavern";
                SafeSwitchToMenu(AdoptionMenuId, "tavern_adoption");
            },
            false,
            8);
    }

    private static void RegisterRootMenu(CampaignGameStarter starter)
    {
        starter.AddGameMenu(
            FamilyMenuId,
            "Здесь решаются дела вашего дома: родственные узы, брачные союзы и принятие новых наследников.",
            _ => KaiRuntimeLog.Write("FAMILY_MENU_OPEN", $"return={_returnMenuId}"),
            GameMenu.MenuOverlayType.None,
            GameMenu.MenuFlags.None,
            null);

        starter.AddGameMenuOption(
            FamilyMenuId,
            "kaitor_family_house_open",
            "Мой род",
            ManageOptionCondition,
            _ => SafeSwitchToMenu(HouseMenuId, "root_house"),
            false,
            0);

        starter.AddGameMenuOption(
            FamilyMenuId,
            "kaitor_family_marriage_open",
            "Брачные союзы",
            ManageOptionCondition,
            _ =>
            {
                ResetMarriageSelection();
                SafeSwitchToMenu(MarriageMenuId, "root_marriage");
            },
            false,
            1);

        starter.AddGameMenuOption(
            FamilyMenuId,
            "kaitor_family_adoption_open",
            "Принять в род",
            ManageOptionCondition,
            _ => SafeSwitchToMenu(AdoptionMenuId, "root_adoption"),
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
    }

    private static void RegisterHouseMenu(CampaignGameStarter starter)
    {
        starter.AddGameMenu(
            HouseMenuId,
            "Сведения о вашем доме и ближайших наследниках.",
            _ => KaiRuntimeLog.Write("HOUSE_OPEN", $"hero={Hero.MainHero?.StringId ?? "null"}"),
            GameMenu.MenuOverlayType.None,
            GameMenu.MenuFlags.None,
            null);

        starter.AddGameMenuOption(
            HouseMenuId,
            "kaitor_family_house_summary",
            "Показать состав рода",
            ManageOptionCondition,
            _ => ShowHousehold(),
            false,
            0);

        starter.AddGameMenuOption(
            HouseMenuId,
            "kaitor_family_house_back",
            "Назад",
            BackOptionCondition,
            _ => SafeSwitchToMenu(FamilyMenuId, "back_family"),
            true,
            99);
    }

    private static void RegisterMarriageMenu(CampaignGameStarter starter)
    {
        starter.AddGameMenu(
            MarriageMenuId,
            "Выберите члена своего рода, другой знатный дом и подходящего кандидата. После подтверждения откроются настоящие брачные переговоры.",
            _ => KaiRuntimeLog.Write(
                "MARRIAGE_OPEN",
                $"member={_selectedHouseMember?.StringId ?? "none"}; clan={_selectedTargetClan?.StringId ?? "none"}; target={_selectedTargetHero?.StringId ?? "none"}"),
            GameMenu.MenuOverlayType.None,
            GameMenu.MenuFlags.None,
            null);

        starter.AddGameMenuOption(
            MarriageMenuId,
            "kaitor_family_marriage_member",
            "Выбрать члена рода",
            MarriageMemberCondition,
            _ => ShowMarriageHouseMembers(),
            false,
            0);

        starter.AddGameMenuOption(
            MarriageMenuId,
            "kaitor_family_marriage_clan",
            "Выбрать другой дом",
            MarriageClanCondition,
            _ => ShowMarriageTargetClans(),
            false,
            1);

        starter.AddGameMenuOption(
            MarriageMenuId,
            "kaitor_family_marriage_target",
            "Выбрать кандидата",
            MarriageTargetCondition,
            _ => ShowMarriageTargetHeroes(),
            false,
            2);

        starter.AddGameMenuOption(
            MarriageMenuId,
            "kaitor_family_marriage_negotiate",
            "Начать брачные переговоры",
            MarriageNegotiateCondition,
            _ => ConfirmAndBeginMarriageNegotiation(),
            false,
            3);

        starter.AddGameMenuOption(
            MarriageMenuId,
            "kaitor_family_dynastic_marriage",
            $"Династический брак ({KaiDynasticMarriageBehavior.PoliticalMarriageCost:N0} динаров)",
            PoliticalMarriageCondition,
            _ => ConfirmPoliticalMarriage(),
            false,
            4);

        starter.AddGameMenuOption(
            MarriageMenuId,
            "kaitor_family_marriage_reset",
            "Сбросить выбор",
            ManageOptionCondition,
            _ =>
            {
                ResetMarriageSelection();
                ShowQuick("Выбор для брачного союза сброшен.");
            },
            false,
            5);

        starter.AddGameMenuOption(
            MarriageMenuId,
            "kaitor_family_marriage_back",
            "Назад",
            BackOptionCondition,
            _ =>
            {
                ResetMarriageSelection();
                SafeSwitchToMenu(FamilyMenuId, "back_family");
            },
            true,
            99);
    }

    private static void RegisterAdoptionMenu(CampaignGameStarter starter)
    {
        starter.AddGameMenu(
            AdoptionMenuId,
            "Здесь можно признать взрослого спутника или члена вашего клана своим ребёнком и наследником. Кандидат должен принадлежать вашему дому, быть той же расы и не иметь собственной семьи.",
            _ => KaiRuntimeLog.Write("ADOPTION_OPEN", $"children={GetLivingChildrenCount()}"),
            GameMenu.MenuOverlayType.None,
            GameMenu.MenuFlags.None,
            null);

        starter.AddGameMenuOption(
            AdoptionMenuId,
            "kaitor_family_adoption_choose",
            "Выбрать кандидата",
            AdoptionChooseCondition,
            _ => ShowAdoptionCandidates(),
            false,
            0);

        starter.AddGameMenuOption(
            AdoptionMenuId,
            "kaitor_family_adoption_back",
            "Назад",
            BackOptionCondition,
            _ => SafeSwitchToMenu(string.IsNullOrWhiteSpace(_returnMenuId) ? "town_tavern" : _returnMenuId, "adoption_back"),
            true,
            99);
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

    private static bool BackOptionCondition(MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Leave;
        return true;
    }

    private static bool MarriageMemberCondition(MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Manage;
        args.Text = new TextObject(_selectedHouseMember == null
            ? "Выбрать члена рода"
            : $"Член рода: {_selectedHouseMember.Name}");
        return Hero.MainHero != null && Clan.PlayerClan != null;
    }

    private static bool MarriageClanCondition(MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Manage;
        args.Text = new TextObject(_selectedTargetClan == null
            ? "Выбрать другой дом"
            : $"Другой дом: {_selectedTargetClan.Name}");

        args.IsEnabled = IsSelectedHouseMemberStillValid();
        if (!args.IsEnabled)
            args.Tooltip = new TextObject("Сначала выберите свободного взрослого члена своего рода.");
        return true;
    }

    private static bool MarriageTargetCondition(MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Manage;
        args.Text = new TextObject(_selectedTargetHero == null
            ? "Выбрать кандидата"
            : $"Кандидат: {_selectedTargetHero.Name}");

        args.IsEnabled = IsSelectedHouseMemberStillValid() && _selectedTargetClan != null;
        if (!args.IsEnabled)
            args.Tooltip = new TextObject("Сначала выберите члена своего рода и другой знатный дом.");
        return true;
    }

    private static bool MarriageNegotiateCondition(MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Manage;
        var valid = ValidateSelectedMarriage(out var reason);
        args.IsEnabled = valid;
        if (!valid)
            args.Tooltip = new TextObject(reason);
        return true;
    }

    private static bool PoliticalMarriageCondition(MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Manage;

        if (!ValidateSelectedMarriage(out var reason))
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject(reason);
            return true;
        }

        var behavior = Campaign.Current?.GetCampaignBehavior<KaiDynasticMarriageBehavior>();
        if (behavior == null || !behavior.CanBeginPoliticalMarriage(_selectedHouseMember, _selectedTargetHero, _selectedTargetClan, out reason))
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject(string.IsNullOrWhiteSpace(reason) ? "Династический брак сейчас недоступен." : reason);
            return true;
        }

        args.IsEnabled = true;
        args.Tooltip = new TextObject(
            $"Политический брак резервирует {KaiDynasticMarriageBehavior.PoliticalMarriageCost:N0} динаров. После состоявшейся свадьбы дома получат династические узы, +20 отношений, +30 доверия и до 180 дней пакта о ненападении между державами.");
        return true;
    }

    private static bool AdoptionChooseCondition(MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Manage;
        args.IsEnabled = CanAdoptMoreChildren();
        if (!args.IsEnabled)
            args.Tooltip = new TextObject($"В вашем доме уже {MaximumLivingChildren} живых детей или принятых наследников.");
        return Hero.MainHero != null && Clan.PlayerClan != null;
    }

    private static void OpenFamilyMenu()
    {
        var currentMenuId = Campaign.Current?.CurrentMenuContext?.GameMenu?.StringId;
        if (!string.IsNullOrWhiteSpace(currentMenuId) && !IsFamilyMenu(currentMenuId))
            _returnMenuId = currentMenuId;

        KaiRuntimeLog.Write("FAMILY_MENU_OPEN", $"from={_returnMenuId}");
        SafeSwitchToMenu(FamilyMenuId, "back_family");
    }

    private static bool IsFamilyMenu(string menuId)
        => string.Equals(menuId, FamilyMenuId, StringComparison.Ordinal) ||
           string.Equals(menuId, HouseMenuId, StringComparison.Ordinal) ||
           string.Equals(menuId, MarriageMenuId, StringComparison.Ordinal) ||
           string.Equals(menuId, AdoptionMenuId, StringComparison.Ordinal);

    private static bool SafeSwitchToMenu(string menuId, string stage)
    {
        if (string.IsNullOrWhiteSpace(menuId))
        {
            KaiRuntimeLog.Write("UI_NAV_FAILED", $"stage={stage}; reason=empty_menu_id");
            ShowQuick("Не удалось открыть раздел: не задан идентификатор меню.");
            return false;
        }

        try
        {
            var before = Campaign.Current?.CurrentMenuContext?.GameMenu?.StringId ?? "none";
            KaiRuntimeLog.Write("UI_NAV_BEGIN", $"stage={stage}; from={before}; to={menuId}");
            GameMenu.SwitchToMenu(menuId);

            var after = Campaign.Current?.CurrentMenuContext?.GameMenu?.StringId ?? "none";
            KaiRuntimeLog.Write("UI_NAV_OK", $"stage={stage}; requested={menuId}; current={after}");
            return true;
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("UI_NAV_FAILED", ex, $"stage={stage}; to={menuId}");
            ShowQuick("Не удалось открыть этот раздел.");
            return false;
        }
    }

    private static void ReturnFromFamilyMenu()
    {
        ResetMarriageSelection();
        var target = string.IsNullOrWhiteSpace(_returnMenuId) ? "town" : _returnMenuId;
        SafeSwitchToMenu(target, "return_previous");
    }

    private static void ShowHousehold()
    {
        try
        {
            var mainHero = Hero.MainHero;
            if (mainHero == null)
                return;

            KaiRuntimeLog.Write("HOUSE_OPEN", $"hero={mainHero.StringId}");
            var lines = new List<string>();

            lines.Add(mainHero.Spouse != null && mainHero.Spouse.IsAlive
                ? $"Супруг: {mainHero.Spouse.Name}."
                : "Супруг: нет.");

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
                lines.Add("Дети и принятые наследники:");
                foreach (var child in children)
                {
                    var marriage = child.Spouse != null && child.Spouse.IsAlive
                        ? $", в браке с {child.Spouse.Name}"
                        : child.CanMarry() ? ", свободен для брачного союза" : string.Empty;
                    var heir = child.IsAlive && child.Clan == Clan.PlayerClan ? ", член вашего дома" : string.Empty;
                    lines.Add($"• {child.Name}, {Math.Max(0, (int)child.Age)} лет{marriage}{heir}.");
                }
            }

            ShowText("Мой род", string.Join("\n", lines));
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("HOUSE_OPEN_FAILED", ex);
            ShowQuick("Не удалось открыть сведения о роде.");
        }
    }

    private static void ShowMarriageHouseMembers()
    {
        try
        {
            var model = Campaign.Current?.Models?.MarriageModel;
            var candidates = GetMarriageHouseMembers()
                .OrderBy(h => h == Hero.MainHero ? 0 : 1)
                .ThenBy(h => h.Name.ToString())
                .Select(h => new InquiryElement(
                    h,
                    $"{h.Name}, {Math.Max(0, (int)h.Age)} лет",
                    null,
                    true,
                    h == Hero.MainHero ? "Вы лично." : "Член вашего рода."))
                .ToList();

            if (model == null || candidates.Count == 0)
            {
                ShowText("Брачные союзы", "Сейчас в вашем доме нет свободного взрослого героя, для которого можно заключить брачный союз.");
                return;
            }

            MBInformationManager.ShowMultiSelectionInquiry(
                new MultiSelectionInquiryData(
                    "Выберите члена рода",
                    "После выбора вы вернётесь в семейное меню и сможете выбрать другой дом.",
                    candidates,
                    true,
                    1,
                    1,
                    "Выбрать",
                    "Отмена",
                    selected =>
                    {
                        if (selected.Count == 0 || selected[0].Identifier is not Hero hero)
                            return;

                        _selectedHouseMember = hero;
                        _selectedTargetClan = null;
                        _selectedTargetHero = null;
                        KaiRuntimeLog.Write(
                            "MARRIAGE_MEMBER_SELECTED",
                            $"hero={hero.StringId}; valid={IsSelectedHouseMemberStillValid()}");
                        ShowQuick($"Для брачного союза выбран: {hero.Name}.", hero);
                        SafeSwitchToMenu(MarriageMenuId, "marriage_member_refresh");
                    },
                    null),
                true,
                true);
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("MARRIAGE_FAILED", ex, "stage=select_member");
            ShowQuick("Не удалось открыть список членов рода.");
        }
    }

    private static void ShowMarriageTargetClans()
    {
        if (!IsSelectedHouseMemberStillValid())
        {
            ShowQuick("Сначала выберите свободного члена своего рода.");
            return;
        }

        try
        {
            var clans = Clan.All
                .Where(IsPotentialMarriageClan)
                .Where(clan => GetSuitableTargetHeroes(_selectedHouseMember, clan).Any())
                .OrderByDescending(clan => clan.Tier)
                .ThenBy(clan => clan.Name.ToString())
                .Select(clan => new InquiryElement(
                    clan,
                    $"{clan.Name} — уровень {clan.Tier}",
                    null,
                    true,
                    $"Подходящих кандидатов: {GetSuitableTargetHeroes(_selectedHouseMember, clan).Count()}."))
                .ToList();

            if (clans.Count == 0)
            {
                ShowText("Брачные союзы", "Для выбранного члена вашего рода сейчас не найдено подходящих знатных домов.");
                return;
            }

            MBInformationManager.ShowMultiSelectionInquiry(
                new MultiSelectionInquiryData(
                    "Выберите другой дом",
                    $"Ищем союз для {_selectedHouseMember.Name}.",
                    clans,
                    true,
                    1,
                    1,
                    "Выбрать",
                    "Отмена",
                    selected =>
                    {
                        if (selected.Count == 0 || selected[0].Identifier is not Clan clan)
                            return;

                        _selectedTargetClan = clan;
                        _selectedTargetHero = null;
                        KaiRuntimeLog.Write(
                            "MARRIAGE_CLAN_SELECTED",
                            $"clan={clan.StringId}; member={_selectedHouseMember?.StringId ?? "none"}; validMember={IsSelectedHouseMemberStillValid()}");
                        ShowQuick($"Выбран дом: {clan.Name}.", clan.Leader);
                        SafeSwitchToMenu(MarriageMenuId, "marriage_clan_refresh");
                    },
                    null),
                true,
                true);
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("MARRIAGE_FAILED", ex, "stage=select_clan");
            ShowQuick("Не удалось открыть список домов.");
        }
    }

    private static void ShowMarriageTargetHeroes()
    {
        if (!IsSelectedHouseMemberStillValid() || _selectedTargetClan == null)
        {
            ShowQuick("Сначала выберите члена своего рода и другой дом.");
            return;
        }

        try
        {
            var targets = GetSuitableTargetHeroes(_selectedHouseMember, _selectedTargetClan)
                .OrderBy(h => h.Name.ToString())
                .Select(h => new InquiryElement(
                    h,
                    $"{h.Name}, {Math.Max(0, (int)h.Age)} лет",
                    null,
                    true,
                    TorFamilySafety.CanUseVanillaPregnancy(_selectedHouseMember, h)
                        ? "Биологическое потомство для этой пары поддерживается."
                        : "Социальный брак допустим, но обычных биологических детей у этой пары не будет."))
                .ToList();

            if (targets.Count == 0)
            {
                _selectedTargetHero = null;
                ShowText("Брачные союзы", "В выбранном доме больше нет подходящего кандидата. Возможно, обстоятельства изменились.");
                return;
            }

            MBInformationManager.ShowMultiSelectionInquiry(
                new MultiSelectionInquiryData(
                    "Выберите кандидата",
                    $"Союз для {_selectedHouseMember.Name} с домом {_selectedTargetClan.Name}.",
                    targets,
                    true,
                    1,
                    1,
                    "Выбрать",
                    "Отмена",
                    selected =>
                    {
                        if (selected.Count == 0 || selected[0].Identifier is not Hero hero)
                            return;

                        _selectedTargetHero = hero;
                        KaiRuntimeLog.Write(
                            "MARRIAGE_TARGET_SELECTED",
                            $"member={_selectedHouseMember?.StringId ?? "none"}; target={hero.StringId}; clan={_selectedTargetClan?.StringId ?? "none"}");
                        ShowQuick($"Кандидат выбран: {hero.Name}.", hero);
                        SafeSwitchToMenu(MarriageMenuId, "marriage_target_refresh");
                    },
                    null),
                true,
                true);
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("MARRIAGE_FAILED", ex, "stage=select_target");
            ShowQuick("Не удалось открыть список кандидатов.");
        }
    }

    private static void ConfirmAndBeginMarriageNegotiation()
    {
        if (!ValidateSelectedMarriage(out var reason))
        {
            ShowQuick(reason);
            return;
        }

        var member = _selectedHouseMember;
        var target = _selectedTargetHero;
        if (!TorFamilySafety.CanUseVanillaPregnancy(member, target))
        {
            InformationManager.ShowInquiry(
                new InquiryData(
                    "Брачный союз без биологических детей",
                    $"{member.Name} и {target.Name} могут заключить полноценный брачный союз, но у этой пары не будет биологических детей. Продолжить переговоры?",
                    true,
                    true,
                    "Продолжить",
                    "Отмена",
                    BeginMarriageBarter,
                    null),
                false,
                false);
            return;
        }

        BeginMarriageBarter();
    }

    private static void ConfirmPoliticalMarriage()
    {
        if (!ValidateSelectedMarriage(out var reason))
        {
            ShowQuick(reason);
            return;
        }

        var behavior = Campaign.Current?.GetCampaignBehavior<KaiDynasticMarriageBehavior>();
        if (behavior == null || !behavior.CanBeginPoliticalMarriage(_selectedHouseMember, _selectedTargetHero, _selectedTargetClan, out reason))
        {
            ShowQuick(string.IsNullOrWhiteSpace(reason) ? "Династический брак сейчас недоступен." : reason);
            return;
        }

        var childlessWarning = TorFamilySafety.CanUseVanillaPregnancy(_selectedHouseMember, _selectedTargetHero)
            ? string.Empty
            : " У этой пары не будет биологических детей.";

        InformationManager.ShowInquiry(
            new InquiryData(
                "Династический брак",
                $"Заключить политический брачный договор между {_selectedHouseMember.Name} и {_selectedTargetHero.Name}? " +
                $"До окончания переговоров будет зарезервировано {KaiDynasticMarriageBehavior.PoliticalMarriageCost:N0} динаров. " +
                "Если свадьба состоится, другой дом получит эту сумму, отношения домов улучшатся, дипломатическое доверие вырастет, а между разными державами будет заключён пакт о ненападении сроком до 180 дней. При отмене переговоров деньги вернутся полностью." +
                childlessWarning,
                true,
                true,
                "Начать переговоры",
                "Отмена",
                BeginPoliticalMarriage,
                null),
            false,
            false);
    }

    private static void BeginPoliticalMarriage()
    {
        if (!ValidateSelectedMarriage(out var reason))
        {
            ShowQuick(reason);
            return;
        }

        var behavior = Campaign.Current?.GetCampaignBehavior<KaiDynasticMarriageBehavior>();
        if (behavior == null)
        {
            ShowQuick("Династический брак сейчас недоступен.");
            return;
        }

        var member = _selectedHouseMember;
        var target = _selectedTargetHero;
        var clan = _selectedTargetClan;
        var returnMenu = string.IsNullOrWhiteSpace(_returnMenuId) ? "town" : _returnMenuId;
        if (!SafeSwitchToMenu(returnMenu, "dynastic_barter_return"))
            return;

        if (!behavior.BeginPoliticalMarriage(member, target, clan, out reason))
            ShowQuick(reason);
    }

    private static void BeginMarriageBarter()
    {
        if (!ValidateSelectedMarriage(out var reason))
        {
            KaiRuntimeLog.Write("MARRIAGE_FAILED", $"stage=pre_barter; reason={reason}");
            ShowQuick(reason);
            return;
        }

        var member = _selectedHouseMember;
        var target = _selectedTargetHero;
        var targetClan = _selectedTargetClan;

        // Return to the settlement menu before opening native barter. The shared bridge
        // is also used by incoming AI proposals and political marriages so all three
        // frontends exercise exactly the same MarriageBarterable -> MarriageAction path.
        var returnMenu = string.IsNullOrWhiteSpace(_returnMenuId) ? "town" : _returnMenuId;
        if (!SafeSwitchToMenu(returnMenu, "marriage_barter_return"))
            return;

        if (!KaiMarriageBarterBridge.TryStart(member, target, targetClan, out reason))
            ShowQuick(reason);
    }

    public static string DescribeUiStatus()
    {
        var current = Campaign.Current?.CurrentMenuContext?.GameMenu?.StringId ?? "none";
        return $"KaiTOR UI: currentMenu={current}; returnMenu={_returnMenuId}; " +
               $"member={_selectedHouseMember?.StringId ?? "none"}; " +
               $"targetClan={_selectedTargetClan?.StringId ?? "none"}; " +
               $"targetHero={_selectedTargetHero?.StringId ?? "none"}.";
    }

    public static string OpenFamilyMenuFromConsole()
    {
        if (Campaign.Current == null)
            return "No campaign is active.";
        if (Hero.MainHero == null || Clan.PlayerClan == null)
            return "Main hero or player clan is unavailable.";

        _returnMenuId = "town_tavern";
        return SafeSwitchToMenu(AdoptionMenuId, "console_adoption")
            ? "Adoption menu opened. Normal entry point: town tavern."
            : "Could not open adoption menu. Check KaiTOR.log.";
    }

    private static IEnumerable<Hero> GetMarriageHouseMembers()
    {
        var playerClan = Clan.PlayerClan;
        var model = Campaign.Current?.Models?.MarriageModel;
        if (playerClan == null || model == null)
            yield break;

        foreach (var hero in playerClan.Heroes.Where(h => h != null).Distinct())
        {
            if (!hero.IsAlive || !hero.IsActive || hero.IsTemplate || hero.IsMinorFactionHero)
                continue;
            if (!hero.IsLord && hero != Hero.MainHero)
                continue;
            if (!hero.CanMarry() || !model.IsSuitableForMarriage(hero))
                continue;

            yield return hero;
        }
    }

    private static bool IsPotentialMarriageClan(Clan clan)
    {
        if (clan == null || clan == Clan.PlayerClan || clan.IsEliminated || clan.Leader == null || !clan.Leader.IsAlive)
            return false;
        if (clan.IsBanditFaction || clan.IsMinorFaction || clan.IsRebelClan || clan.IsClanTypeMercenary)
            return false;
        if (!Campaign.Current.Models.MarriageModel.IsClanSuitableForMarriage(clan))
            return false;
        return true;
    }

    private static IEnumerable<Hero> GetSuitableTargetHeroes(Hero member, Clan clan)
    {
        var model = Campaign.Current?.Models?.MarriageModel;
        if (member == null || clan == null || model == null)
            yield break;

        foreach (var hero in clan.AliveLords)
        {
            if (hero == null || !hero.IsAlive || !hero.IsActive || hero.IsPrisoner || hero.Spouse != null)
                continue;
            if (!hero.CanMarry() || !model.IsSuitableForMarriage(hero))
                continue;
            if (!model.IsCoupleSuitableForMarriage(member, hero))
                continue;

            var alreadyCourted = Romance.GetCourtedHeroInOtherClan(member, hero);
            if (alreadyCourted != null && alreadyCourted != hero)
                continue;

            yield return hero;
        }
    }

    private static bool IsSelectedHouseMemberStillValid()
        => _selectedHouseMember != null && GetMarriageHouseMembers().Contains(_selectedHouseMember);

    private static bool ValidateSelectedMarriage(out string reason)
    {
        reason = string.Empty;
        if (!IsSelectedHouseMemberStillValid())
        {
            reason = "Выберите свободного взрослого члена своего рода.";
            return false;
        }

        if (_selectedTargetClan == null || !IsPotentialMarriageClan(_selectedTargetClan))
        {
            reason = "Выберите подходящий другой знатный дом.";
            return false;
        }

        if (_selectedTargetHero == null || _selectedTargetHero.Clan != _selectedTargetClan ||
            !GetSuitableTargetHeroes(_selectedHouseMember, _selectedTargetClan).Contains(_selectedTargetHero))
        {
            reason = "Выберите подходящего кандидата из другого дома.";
            return false;
        }

        var leader = _selectedTargetClan.Leader;
        if (leader == null || !leader.IsAlive || leader.IsPrisoner)
        {
            reason = "Глава выбранного дома сейчас не может вести брачные переговоры.";
            return false;
        }

        return true;
    }

    private static void ResetMarriageSelection()
    {
        _selectedHouseMember = null;
        _selectedTargetClan = null;
        _selectedTargetHero = null;
    }

    private void OnBeforeHeroesMarried(Hero firstHero, Hero secondHero, bool showNotification)
    {
        if (firstHero == null || secondHero == null)
            return;

        if (!KaiPlayerMarriageModel.InvolvesPlayerClan(firstHero, secondHero))
            return;

        KaiRuntimeLog.Write(
            "MARRIAGE_SUCCESS",
            $"first={firstHero.StringId}; second={secondHero.StringId}; firstClan={firstHero.Clan?.StringId ?? "null"}; secondClan={secondHero.Clan?.StringId ?? "null"}");

        if ((firstHero == _selectedHouseMember && secondHero == _selectedTargetHero) ||
            (secondHero == _selectedHouseMember && firstHero == _selectedTargetHero))
            ResetMarriageSelection();
    }

    private static void ShowAdoptionCandidates()
    {
        if (!CanAdoptMoreChildren())
        {
            ShowText("Принять в род", $"Ваш дом уже достиг предела: {MaximumLivingChildren} живых детей или принятых наследников.");
            return;
        }

        try
        {
            var pool = GetAdoptionCandidatePool().ToArray();
            if (pool.Length == 0)
            {
                ShowText("Принять в род", "Сейчас в вашем клане нет спутников или других взрослых героев, которых можно рассмотреть для принятия в род.");
                return;
            }

            var elements = pool
                .OrderByDescending(h => Hero.MainHero.GetRelation(h))
                .ThenBy(h => h.Name.ToString())
                .Select(h =>
                {
                    var allowed = IsEligibleForAdoption(h, out var reason);
                    var hint = allowed
                        ? $"Отношение: {Hero.MainHero.GetRelation(h):+0;-0;0}. Может быть принят в род."
                        : reason;
                    return new InquiryElement(
                        h,
                        $"{h.Name}, {Math.Max(0, (int)h.Age)} лет",
                        null,
                        allowed,
                        hint);
                })
                .ToList();

            KaiRuntimeLog.Write("ADOPTION_OPEN", $"pool={pool.Length}; eligible={elements.Count(x => x.IsEnabled)}");
            MBInformationManager.ShowMultiSelectionInquiry(
                new MultiSelectionInquiryData(
                    "Принять в род",
                    "Выберите героя. Недоступные кандидаты показаны вместе с причиной. Принятие сделает героя вашим ребёнком и членом PlayerClan.",
                    elements,
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
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("ADOPTION_FAILED", ex, "stage=open_candidates");
            ShowQuick("Не удалось открыть список кандидатов.");
        }
    }

    private static void Adopt(Hero candidate)
    {
        if (!IsEligibleForAdoption(candidate, out var reason))
        {
            KaiRuntimeLog.Write("ADOPTION_FAILED", $"candidate={candidate?.StringId ?? "null"}; reason={reason}");
            ShowQuick(reason, candidate);
            return;
        }

        try
        {
            var playerClan = Clan.PlayerClan;
            var companionClan = candidate.CompanionOf;
            if (companionClan != null)
                RemoveCompanionAction.ApplyByByTurningToLord(companionClan, candidate);

            if (!candidate.IsLord)
                candidate.SetNewOccupation(Occupation.Lord);

            AdoptHeroAction.Apply(candidate);
            candidate.IsKnownToPlayer = true;

            var parentLinked = candidate.Father == Hero.MainHero || candidate.Mother == Hero.MainHero;
            if (candidate.Clan != playerClan || !parentLinked)
                throw new InvalidOperationException("Native adoption postcondition failed.");

            KaiRuntimeLog.Write(
                "ADOPTION_SUCCESS",
                $"candidate={candidate.StringId}; clan={candidate.Clan?.StringId ?? "null"}; father={candidate.Father?.StringId ?? "null"}; mother={candidate.Mother?.StringId ?? "null"}");

            ShowQuick(
                $"{candidate.Name} принят{(candidate.IsFemale ? "а" : string.Empty)} в ваш род и признан{(candidate.IsFemale ? "а" : string.Empty)} наследником.",
                candidate);
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("ADOPTION_FAILED", ex, $"candidate={candidate?.StringId ?? "null"}; stage=apply");
            ShowQuick("Принятие в род не удалось.", candidate);
        }
    }

    private static IEnumerable<Hero> GetAdoptionCandidatePool()
    {
        var playerClan = Clan.PlayerClan;
        if (playerClan == null)
            yield break;

        foreach (var hero in Hero.AllAliveHeroes
                     .Where(h => h != null && h != Hero.MainHero)
                     .Where(h => h.Clan == playerClan || h.CompanionOf == playerClan)
                     .Distinct())
            yield return hero;
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
            reason = $"Ваш дом уже достиг предела: {MaximumLivingChildren} живых детей или принятых наследников.";
            return false;
        }

        if (hero == null || hero == mainHero || !hero.IsAlive || !hero.IsActive || hero.IsTemplate || hero.IsMinorFactionHero)
        {
            reason = "Этот герой не может быть принят в ваш род.";
            return false;
        }

        if (hero.Clan != playerClan && hero.CompanionOf != playerClan)
        {
            reason = "Сначала герой должен присоединиться к вашему клану как спутник или член дома.";
            return false;
        }

        if (hero.Age < Campaign.Current.Models.AgeModel.HeroComesOfAge)
        {
            reason = "Этот герой ещё слишком молод.";
            return false;
        }

        if (hero.Father != null || hero.Mother != null)
        {
            reason = "У этого героя уже есть родители.";
            return false;
        }

        if (hero.Spouse != null)
        {
            reason = "У этого героя уже есть супруг.";
            return false;
        }

        if (hero.Children.Count > 0)
        {
            reason = "У этого героя уже есть собственные дети.";
            return false;
        }

        if (hero.IsPrisoner || hero.PartyBelongedToAsPrisoner != null)
        {
            reason = "Нельзя провести церемонию, пока герой находится в плену.";
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
        => Hero.MainHero != null && GetLivingChildrenCount() < MaximumLivingChildren;

    private static int GetLivingChildrenCount()
        => Hero.MainHero?.Children.Count(h => h != null && h.IsAlive) ?? 0;

    private static void ShowText(string title, string body)
    {
        InformationManager.ShowInquiry(
            new InquiryData(
                title,
                body,
                true,
                false,
                "ОК",
                string.Empty,
                null,
                null),
            false,
            false);
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
