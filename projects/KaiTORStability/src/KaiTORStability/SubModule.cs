using System;
using System.Linq;
using TaleWorlds.MountAndBlade;

namespace KaiTORStability
{
    public sealed class SubModule : MBSubModuleBase
    {
        private StabilitySettings _settings;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            StabilityLog.StartSession();
            _settings = StabilitySettings.Load();
            StabilityLog.Event(
                "MODULE_LOAD",
                "KaiTOR Stability loaded after TOR_Core; optimization=" + _settings.EnableStatusEffectOptimization +
                "; rescanMs=" + _settings.FullRescanIntervalMs);
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            StabilityLog.Event("INITIAL_SCREEN_READY", "Bannerlord initial module screen is ready.");
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);

            if (_settings == null || !_settings.EnableStatusEffectOptimization || mission == null)
            {
                return;
            }

            try
            {
                var torLogic = mission.MissionBehaviors.FirstOrDefault(
                    x => string.Equals(
                        x.GetType().FullName,
                        "TOR_Core.BattleMechanics.StatusEffect.StatusEffectMissionLogic",
                        StringComparison.Ordinal));

                if (torLogic == null)
                {
                    StabilityLog.Event("OPTIMIZATION_SKIP", "TOR StatusEffectMissionLogic was not present in this mission.");
                    return;
                }

                var replacement = OptimizedStatusEffectMissionLogic.TryCreate(
                    torLogic,
                    _settings.FullRescanIntervalMs / 1000f);

                if (replacement == null)
                {
                    StabilityLog.Event("OPTIMIZATION_FALLBACK", "TOR status-effect API did not match the expected contract; original TOR logic remains active.");
                    return;
                }

                mission.RemoveMissionBehavior(torLogic);
                mission.AddMissionBehavior(replacement);

                StabilityLog.Event(
                    "OPTIMIZATION_ACTIVE",
                    "Replaced TOR StatusEffectMissionLogic; full-rescan interval=" + _settings.FullRescanIntervalMs + "ms.");
            }
            catch (Exception ex)
            {
                StabilityLog.Event("OPTIMIZATION_ERROR", ex.ToString());
                // Fail open: if replacement installation itself fails, do not abort mission creation.
            }
        }
    }
}
