using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace KaiCleave
{
    public sealed class SubModule : MBSubModuleBase
    {
        internal const string HarmonyId = "kai.cleave.bannerlord.1.3.15";
        internal const string Version = "0.2.0-beta";

        private Harmony _harmony;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            KaiSettings.Load();
            DebugLogger.StartSession(Version);

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(SubModule).Assembly);
            TorCompatibility.TryPatch(_harmony);

            if (KaiSettings.ShowLoadMessage)
            {
                InformationManager.DisplayMessage(
                    new InformationMessage("[KaiCleave] " + Version +
                                           " loaded | Bannerlord 1.3.15.110062 | TOR-ready"));
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            TorCompatibility.TryPatch(_harmony);
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            SwingTracker.Reset();
            TorCompatibility.TryPatch(_harmony);
            DebugLogger.Write("mission initialized | TOR-final-patch=" + TorCompatibility.TryPatch(_harmony));
        }

        protected override void OnSubModuleUnloaded()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchAll(HarmonyId);
                _harmony = null;
            }

            SwingTracker.Reset();
            base.OnSubModuleUnloaded();
        }
    }
}
