using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace KaiCleave
{
    /// <summary>
    /// Dependency-free in-game settings UI. Uses Bannerlord's native inquiry widgets,
    /// so KaiCleave does not require MCM/ButterLib/UIExtenderEx just to expose settings.
    /// </summary>
    internal static class InGameSettings
    {
        private static bool _menuOpen;
        private static bool _reopenRoot;

        internal static void Tick()
        {
            if (_reopenRoot && !_menuOpen)
            {
                _reopenRoot = false;
                OpenRoot();
            }
        }

        internal static void OpenRoot()
        {
            if (_menuOpen)
                return;

            _menuOpen = true;
            var options = new List<InquiryElement>
            {
                Entry("enabled", "Клив: " + OnOff(KaiSettings.Enabled),
                    "Главный переключатель мода. Изменение применяется сразу."),
                Entry("maxTargets", "Максимум целей за взмах: " + KaiSettings.MaxTargetsPerSwing,
                    "Считаются только цели, получившие урон. Блок щитом или оружием лимит не расходует."),
                Entry("shield", "Двуручное / древковое проходит через щит: " + OnOff(KaiSettings.HeavyShieldContinue),
                    "Если включено, двуручное и древковое оружие может продолжить взмах после блока щитом. Одноручное оружие по-прежнему останавливается."),
                Entry("weaponBlock", "Двуручное / древковое проходит через блок оружием: " + OnOff(KaiSettings.HeavyWeaponBlockContinue),
                    "Если включено, двуручное и древковое оружие может продолжить взмах после блока оружием, парирования или chamber-блока. Сильнее влияет на баланс дуэлей."),
                Entry("thrusts", "Клив колющими атаками: " + OnOff(KaiSettings.AllowThrusts),
                    "Рекомендуется выключить. Обычный клив рассчитан на рубящие и вертикальные взмахи."),
                Entry("friendly", "Удары по союзникам: " + OnOff(KaiSettings.AllowFriendlyTargets),
                    "Рекомендуется выключить."),
                Entry("weapons", "Классы оружия...",
                    "Включение или отключение клива для отдельных классов оружия. Это не профиль урона и не снижение урона по цепочке."),
                Entry("debug", "Отладочный лог: " + OnOff(KaiSettings.DebugLogging),
                    "Асинхронный диагностический лог ударов, блоков и таймингов."),
                Entry("loadMessage", "Сообщение при загрузке: " + OnOff(KaiSettings.ShowLoadMessage),
                    "Показывать версию KaiCleave и режим работы при загрузке мода.")
            };

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "KaiCleave v0.3.1 — Настройки",
                "Изменения применяются сразу и сохраняются в KaiCleave.ini. Клив для NPC намеренно не добавлен.\n\nВыберите один пункт и нажмите ИЗМЕНИТЬ.",
                options,
                true,
                1,
                1,
                "ИЗМЕНИТЬ",
                "ЗАКРЫТЬ",
                OnRootSelected,
                OnClosed));
        }

        private static void OnRootSelected(List<InquiryElement> selected)
        {
            _menuOpen = false;
            if (selected == null || selected.Count == 0)
                return;

            string id = selected[0].Identifier as string;
            switch (id)
            {
                case "enabled":
                    KaiSettings.Enabled = !KaiSettings.Enabled;
                    SaveAndReturn();
                    break;
                case "maxTargets":
                    OpenMaxTargets();
                    break;
                case "shield":
                    KaiSettings.HeavyShieldContinue = !KaiSettings.HeavyShieldContinue;
                    SaveAndReturn();
                    break;
                case "weaponBlock":
                    KaiSettings.HeavyWeaponBlockContinue = !KaiSettings.HeavyWeaponBlockContinue;
                    SaveAndReturn();
                    break;
                case "thrusts":
                    KaiSettings.AllowThrusts = !KaiSettings.AllowThrusts;
                    SaveAndReturn();
                    break;
                case "friendly":
                    KaiSettings.AllowFriendlyTargets = !KaiSettings.AllowFriendlyTargets;
                    SaveAndReturn();
                    break;
                case "weapons":
                    OpenWeapons();
                    break;
                case "debug":
                    KaiSettings.DebugLogging = !KaiSettings.DebugLogging;
                    SaveAndReturn();
                    break;
                case "loadMessage":
                    KaiSettings.ShowLoadMessage = !KaiSettings.ShowLoadMessage;
                    SaveAndReturn();
                    break;
                default:
                    _reopenRoot = true;
                    break;
            }
        }

        private static void OpenMaxTargets()
        {
            _menuOpen = true;
            int[] values = { 1, 2, 3, 4, 5, 6, 8, 10, 12, 16, 24, 32 };
            var options = new List<InquiryElement>();
            foreach (int value in values)
            {
                options.Add(new InquiryElement(
                    value,
                    (value == KaiSettings.MaxTargetsPerSwing ? "[ТЕКУЩЕЕ] " : string.Empty) + value + " целей",
                    null,
                    true,
                    "Максимальное количество целей, которые могут получить урон за один отслеживаемый взмах."));
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "KaiCleave — Максимум целей",
                "Выберите максимальное количество целей, получающих урон за один взмах.",
                options,
                true,
                1,
                1,
                "УСТАНОВИТЬ",
                "НАЗАД",
                selected =>
                {
                    _menuOpen = false;
                    if (selected != null && selected.Count > 0 && selected[0].Identifier is int value)
                    {
                        KaiSettings.MaxTargetsPerSwing = value;
                        SaveAndReturn();
                    }
                    else
                    {
                        _reopenRoot = true;
                    }
                },
                OnBack));
        }

        private static void OpenWeapons()
        {
            _menuOpen = true;
            var options = new List<InquiryElement>
            {
                WeaponEntry("1hs", "Одноручный меч", KaiSettings.OneHandedSword),
                WeaponEntry("2hs", "Двуручный меч", KaiSettings.TwoHandedSword),
                WeaponEntry("1ha", "Одноручный топор", KaiSettings.OneHandedAxe),
                WeaponEntry("2ha", "Двуручный топор", KaiSettings.TwoHandedAxe),
                WeaponEntry("1hp", "Одноручное древковое", KaiSettings.OneHandedPolearm),
                WeaponEntry("2hp", "Двуручное древковое", KaiSettings.TwoHandedPolearm),
                WeaponEntry("lgp", "Древковое с низким хватом", KaiSettings.LowGripPolearm),
                WeaponEntry("mace", "Булава", KaiSettings.Mace),
                WeaponEntry("2hm", "Двуручная булава", KaiSettings.TwoHandedMace),
                WeaponEntry("dagger", "Кинжал", KaiSettings.Dagger),
                WeaponEntry("pick", "Кирка", KaiSettings.Pick)
            };

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "KaiCleave — Классы оружия",
                "Выберите один класс оружия и нажмите ПЕРЕКЛЮЧИТЬ.",
                options,
                true,
                1,
                1,
                "ПЕРЕКЛЮЧИТЬ",
                "НАЗАД",
                OnWeaponSelected,
                OnBack));
        }

        private static void OnWeaponSelected(List<InquiryElement> selected)
        {
            _menuOpen = false;
            if (selected == null || selected.Count == 0)
            {
                _reopenRoot = true;
                return;
            }

            switch (selected[0].Identifier as string)
            {
                case "1hs": KaiSettings.OneHandedSword = !KaiSettings.OneHandedSword; break;
                case "2hs": KaiSettings.TwoHandedSword = !KaiSettings.TwoHandedSword; break;
                case "1ha": KaiSettings.OneHandedAxe = !KaiSettings.OneHandedAxe; break;
                case "2ha": KaiSettings.TwoHandedAxe = !KaiSettings.TwoHandedAxe; break;
                case "1hp": KaiSettings.OneHandedPolearm = !KaiSettings.OneHandedPolearm; break;
                case "2hp": KaiSettings.TwoHandedPolearm = !KaiSettings.TwoHandedPolearm; break;
                case "lgp": KaiSettings.LowGripPolearm = !KaiSettings.LowGripPolearm; break;
                case "mace": KaiSettings.Mace = !KaiSettings.Mace; break;
                case "2hm": KaiSettings.TwoHandedMace = !KaiSettings.TwoHandedMace; break;
                case "dagger": KaiSettings.Dagger = !KaiSettings.Dagger; break;
                case "pick": KaiSettings.Pick = !KaiSettings.Pick; break;
            }

            KaiSettings.Save();
            OpenWeaponsDeferred();
        }

        private static void OpenWeaponsDeferred()
        {
            // Native inquiries close on the same UI tick. Re-open through the root on the next tick
            // to avoid stacking screens, then the user can enter Weapon Classes again.
            _reopenRoot = true;
        }

        private static InquiryElement Entry(string id, string title, string hint)
        {
            return new InquiryElement(id, title, null, true, hint);
        }

        private static InquiryElement WeaponEntry(string id, string name, bool enabled)
        {
            return Entry(id, name + ": " + OnOff(enabled), "Переключить клив для этого класса оружия.");
        }

        private static string OnOff(bool value)
        {
            return value ? "ВКЛ" : "ВЫКЛ";
        }

        private static void SaveAndReturn()
        {
            KaiSettings.Save();
            InformationManager.DisplayMessage(new InformationMessage("[KaiCleave] Настройки сохранены | " + KaiSettings.DescribeRuntimeSettings()));
            _reopenRoot = true;
        }

        private static void OnBack(List<InquiryElement> ignored)
        {
            _menuOpen = false;
            _reopenRoot = true;
        }

        private static void OnClosed(List<InquiryElement> ignored)
        {
            _menuOpen = false;
        }
    }
}
