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
                Entry("enabled", "Cleave: " + OnOff(KaiSettings.Enabled),
                    "Master switch. Applies immediately."),
                Entry("maxTargets", "Max targets per swing: " + KaiSettings.MaxTargetsPerSwing,
                    "Damaged targets only. Shield/weapon blocks do not consume this limit."),
                Entry("shield", "2H / polearm through shield: " + OnOff(KaiSettings.HeavyShieldContinue),
                    "If ON, two-handed weapons and polearms may keep travelling after a shield block. One-handed weapons still stop."),
                Entry("weaponBlock", "2H / polearm through weapon block: " + OnOff(KaiSettings.HeavyWeaponBlockContinue),
                    "If ON, two-handed weapons and polearms may keep travelling after a weapon block/parry. This changes duel balance more strongly."),
                Entry("thrusts", "Thrust cleave: " + OnOff(KaiSettings.AllowThrusts),
                    "Recommended OFF. Horizontal/overhead swings remain the normal cleave path."),
                Entry("friendly", "Friendly targets: " + OnOff(KaiSettings.AllowFriendlyTargets),
                    "Recommended OFF."),
                Entry("weapons", "Weapon classes...",
                    "Enable/disable cleave by weapon class. This is not a damage/falloff profile."),
                Entry("debug", "Debug logging: " + OnOff(KaiSettings.DebugLogging),
                    "Async diagnostic log for hit/block/timing analysis."),
                Entry("loadMessage", "Load message: " + OnOff(KaiSettings.ShowLoadMessage),
                    "Show KaiCleave version and runtime mode on load.")
            };

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "KaiCleave v0.3.0 Settings",
                "Changes apply immediately and are saved to KaiCleave.ini. NPC cleave is intentionally not exposed here.\n\nSelect one item and press CHANGE.",
                options,
                true,
                1,
                1,
                "CHANGE",
                "CLOSE",
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
                    (value == KaiSettings.MaxTargetsPerSwing ? "[CURRENT] " : string.Empty) + value + " targets",
                    null,
                    true,
                    "Maximum damaged targets accepted during one tracked swing."));
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "KaiCleave - Max Targets",
                "Choose the maximum number of damaged targets per swing.",
                options,
                true,
                1,
                1,
                "SET",
                "BACK",
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
                WeaponEntry("1hs", "One-handed sword", KaiSettings.OneHandedSword),
                WeaponEntry("2hs", "Two-handed sword", KaiSettings.TwoHandedSword),
                WeaponEntry("1ha", "One-handed axe", KaiSettings.OneHandedAxe),
                WeaponEntry("2ha", "Two-handed axe", KaiSettings.TwoHandedAxe),
                WeaponEntry("1hp", "One-handed polearm", KaiSettings.OneHandedPolearm),
                WeaponEntry("2hp", "Two-handed polearm", KaiSettings.TwoHandedPolearm),
                WeaponEntry("lgp", "Low-grip polearm", KaiSettings.LowGripPolearm),
                WeaponEntry("mace", "Mace", KaiSettings.Mace),
                WeaponEntry("2hm", "Two-handed mace", KaiSettings.TwoHandedMace),
                WeaponEntry("dagger", "Dagger", KaiSettings.Dagger),
                WeaponEntry("pick", "Pick", KaiSettings.Pick)
            };

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "KaiCleave - Weapon Classes",
                "Select one class to toggle it, then press TOGGLE.",
                options,
                true,
                1,
                1,
                "TOGGLE",
                "BACK",
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
            return Entry(id, name + ": " + OnOff(enabled), "Toggle cleave for this weapon class.");
        }

        private static string OnOff(bool value)
        {
            return value ? "ON" : "OFF";
        }

        private static void SaveAndReturn()
        {
            KaiSettings.Save();
            InformationManager.DisplayMessage(new InformationMessage("[KaiCleave] Settings saved | " + KaiSettings.DescribeRuntimeSettings()));
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
