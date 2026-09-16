using HarmonyLib;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace KaiCleave
{
    public sealed class SubModule : MBSubModuleBase
    {
        internal const string HarmonyId = "kai.cleave.bannerlord.1.3.15";
        internal const string Version = "0.3.1-ru-ui";

        private Harmony _harmony;
        private bool _combatPatchesActive;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            KaiSettings.Load();
            DebugLogger.StartSession(Version);
            DebugLogger.Write("runtime " + CoopRuntime.Describe());
            DebugLogger.Write("settings " + KaiSettings.DescribeRuntimeSettings());

            _combatPatchesActive = CoopRuntime.CombatPatchesAllowed;
            if (_combatPatchesActive)
            {
                _harmony = new Harmony(HarmonyId);
                _harmony.PatchAll(typeof(SubModule).Assembly);
                TorCompatibility.TryPatch(_harmony);
                DebugLogger.Write("combat patches ACTIVE | owner-local battle authority");
            }
            else
            {
                DebugLogger.Write("combat patches PASSIVE on Coop campaign server; battle peers own cleave simulation");
            }

            if (KaiSettings.ShowLoadMessage)
            {
                string authority;
                if (CoopRuntime.Mode == CoopRuntimeMode.CoopPeer)
                    authority = "Coop peer local-authority";
                else if (CoopRuntime.Mode == CoopRuntimeMode.CoopCampaignServer)
                    authority = "Coop campaign server passive";
                else
                    authority = "standalone";

                InformationManager.DisplayMessage(
                    new InformationMessage("[KaiCleave] " + Version +
                                           " loaded | F10 settings | Bannerlord 1.3.15.110062 | " + authority));
            }
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            InGameSettings.Tick();

            if (Input.IsKeyPressed(InputKey.F10))
                InGameSettings.OpenRoot();
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            if (_combatPatchesActive)
                TorCompatibility.TryPatch(_harmony);
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);
            SwingTracker.Reset();

            if (!_combatPatchesActive)
            {
                DebugLogger.Write("mission initialized | Coop campaign server passive | no damage patches");
                return;
            }

            bool torPatched = TorCompatibility.TryPatch(_harmony);
            DebugLogger.Write("mission initialized | TOR-final-patch=" + torPatched +
                              " | " + CoopRuntime.Describe());
        }

        protected override void OnSubModuleUnloaded()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchAll(HarmonyId);
                _harmony = null;
            }

            SwingTracker.Reset();
            DebugLogger.Write("module unload | flushing async logger");
            DebugLogger.FlushAndStop();
            base.OnSubModuleUnloaded();
        }
    }
}
