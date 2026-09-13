using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace KaiCleave
{
    public sealed class SubModule : MBSubModuleBase
    {
        internal const string HarmonyId = "kai.cleave.bannerlord.1.3.15";
        internal const string Version = "0.2.1-coop-beta";

        private Harmony _harmony;
        private bool _combatPatchesActive;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            KaiSettings.Load();
            DebugLogger.StartSession(Version);
            DebugLogger.Write("runtime " + CoopRuntime.Describe());

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
                // The KaiTOR campaign server owns campaign state but Coop battle collision/damage is
                // simulated by mission peers. Keep this module loaded for exact module validation,
                // but do not patch mission combat in the /server /coopsave process.
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
                                           " loaded | Bannerlord 1.3.15.110062 | " + authority));
            }
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
            base.OnSubModuleUnloaded();
        }
    }
}
