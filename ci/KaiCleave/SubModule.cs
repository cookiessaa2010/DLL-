using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace KaiCleave
{
    public sealed class SubModule : MBSubModuleBase
    {
        internal const string HarmonyId = "kai.cleave.bannerlord.1.3.15";
        internal const string Version = "0.1.0-beta";

        private Harmony _harmony;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(SubModule).Assembly);

            InformationManager.DisplayMessage(
                new InformationMessage("[KaiCleave] " + Version + " loaded | Bannerlord 1.3.15 / TOR"));
        }

        protected override void OnSubModuleUnloaded()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchAll(HarmonyId);
                _harmony = null;
            }

            base.OnSubModuleUnloaded();
        }
    }
}
