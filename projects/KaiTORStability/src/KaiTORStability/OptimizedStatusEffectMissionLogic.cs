using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace KaiTORStability
{
    internal sealed class OptimizedStatusEffectMissionLogic : MissionLogic
    {
        private readonly object _originalLogic;
        private readonly Type _componentType;
        private readonly ConstructorInfo _componentConstructor;
        private readonly MethodInfo _getComponentMethod;
        private readonly PropertyInfo _needsTickProperty;
        private readonly MethodInfo _onTickMethod;
        private readonly MethodInfo _synchronizeBaseValuesMethod;
        private readonly MethodInfo _checkPermanentEffectsMethod;
        private readonly float _rescanIntervalSeconds;
        private readonly bool _activationHookAvailable;

        private readonly Dictionary<Agent, AgentComponent> _components = new Dictionary<Agent, AgentComponent>();
        private readonly HashSet<AgentComponent> _knownComponents = new HashSet<AgentComponent>();
        private readonly HashSet<AgentComponent> _activeComponents = new HashSet<AgentComponent>();
        private readonly Queue<Agent> _pendingPermanentEffects = new Queue<Agent>();
        private readonly List<AgentComponent> _scratch = new List<AgentComponent>();

        private readonly object _activationQueueLock = new object();
        private readonly Queue<AgentComponent> _pendingActivations = new Queue<AgentComponent>();
        private readonly HashSet<AgentComponent> _queuedActivations = new HashSet<AgentComponent>();

        private float _rescanAccumulator;
        private float _telemetryAccumulator;
        private int _framesSinceTelemetry;
        private int _rescansSinceTelemetry;
        private int _activationNotificationsSinceTelemetry;

        private OptimizedStatusEffectMissionLogic(
            object originalLogic,
            Type componentType,
            ConstructorInfo componentConstructor,
            MethodInfo getComponentMethod,
            PropertyInfo needsTickProperty,
            MethodInfo onTickMethod,
            MethodInfo synchronizeBaseValuesMethod,
            MethodInfo checkPermanentEffectsMethod,
            float rescanIntervalSeconds,
            bool activationHookAvailable)
        {
            _originalLogic = originalLogic;
            _componentType = componentType;
            _componentConstructor = componentConstructor;
            _getComponentMethod = getComponentMethod;
            _needsTickProperty = needsTickProperty;
            _onTickMethod = onTickMethod;
            _synchronizeBaseValuesMethod = synchronizeBaseValuesMethod;
            _checkPermanentEffectsMethod = checkPermanentEffectsMethod;
            _rescanIntervalSeconds = Math.Max(0.05f, Math.Min(10.0f, rescanIntervalSeconds));
            _activationHookAvailable = activationHookAvailable;
        }

        public static OptimizedStatusEffectMissionLogic TryCreate(
            object originalLogic,
            float fallbackRescanIntervalSeconds,
            float hookedSafetyRescanIntervalSeconds)
        {
            if (originalLogic == null)
            {
                return null;
            }

            try
            {
                var assembly = originalLogic.GetType().Assembly;
                var componentType = assembly.GetType(
                    "TOR_Core.BattleMechanics.StatusEffect.StatusEffectComponent",
                    false);
                if (componentType == null || !typeof(AgentComponent).IsAssignableFrom(componentType))
                {
                    return null;
                }

                var constructor = componentType.GetConstructor(new[] { typeof(Agent) });
                var needsTick = componentType.GetProperty("NeedsStatusEffectTick", BindingFlags.Instance | BindingFlags.Public);
                var onTick = componentType.GetMethod("OnTick", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(float) }, null);
                var syncBase = componentType.GetMethod("SynchronizeBaseValues", BindingFlags.Instance | BindingFlags.Public);
                var checkPermanent = originalLogic.GetType().GetMethod(
                    "CheckUnitForAddingPermanentEffects",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                var getComponentDefinition = typeof(Agent)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(x =>
                        x.Name == "GetComponent" &&
                        x.IsGenericMethodDefinition &&
                        x.GetParameters().Length == 0);

                if (constructor == null || needsTick == null || onTick == null || syncBase == null ||
                    checkPermanent == null || getComponentDefinition == null)
                {
                    return null;
                }

                var activationHookAvailable = StatusEffectActivationBridge.EnsureInstalled(componentType);
                var selectedRescanSeconds = activationHookAvailable
                    ? hookedSafetyRescanIntervalSeconds
                    : fallbackRescanIntervalSeconds;

                var getComponent = getComponentDefinition.MakeGenericMethod(componentType);
                return new OptimizedStatusEffectMissionLogic(
                    originalLogic,
                    componentType,
                    constructor,
                    getComponent,
                    needsTick,
                    onTick,
                    syncBase,
                    checkPermanent,
                    selectedRescanSeconds,
                    activationHookAvailable);
            }
            catch (Exception ex)
            {
                StabilityLog.Event("OPTIMIZATION_BIND_ERROR", ex.ToString());
                return null;
            }
        }

        public override void AfterStart()
        {
            base.AfterStart();

            // Migration guard. Normally this replacement is installed before agents spawn, but
            // existing agents are adopted if another mission type created them unusually early.
            if (Mission?.AllAgents == null)
            {
                return;
            }

            foreach (var agent in Mission.AllAgents)
            {
                if (agent == null)
                {
                    continue;
                }

                bool created;
                GetOrCreateComponent(agent, out created);
                if (created)
                {
                    _pendingPermanentEffects.Enqueue(agent);
                }
            }

            ForceRescan();
        }

        public override void OnAgentCreated(Agent agent)
        {
            if (agent == null)
            {
                return;
            }

            bool created;
            GetOrCreateComponent(agent, out created);
            _pendingPermanentEffects.Enqueue(agent);
        }

        public override void OnAgentMount(Agent agent)
        {
            AgentComponent component;
            if (agent != null && TryGetComponent(agent, out component))
            {
                InvokeUnwrapped(_synchronizeBaseValuesMethod, component, new object[] { true });
            }
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow)
        {
            ForgetAgent(affectedAgent);
        }

        public override void OnAgentDeleted(Agent affectedAgent)
        {
            ForgetAgent(affectedAgent);
        }

        public override void OnMissionTick(float dt)
        {
            _framesSinceTelemetry++;
            _rescanAccumulator += dt;
            _telemetryAccumulator += dt;

            DrainPermanentEffectQueue();
            DrainActivationQueue();

            if (_rescanAccumulator >= _rescanIntervalSeconds)
            {
                _rescanAccumulator = 0f;
                RescanForActiveComponents();
            }

            _scratch.Clear();
            _scratch.AddRange(_activeComponents);
            foreach (var component in _scratch)
            {
                InvokeUnwrapped(_onTickMethod, component, new object[] { dt });

                if (!NeedsTick(component))
                {
                    _activeComponents.Remove(component);
                }
            }

            if (_telemetryAccumulator >= 30f)
            {
                StabilityLog.Event(
                    "BATTLE_TELEMETRY",
                    "knownComponents=" + _knownComponents.Count +
                    "; activeComponents=" + _activeComponents.Count +
                    "; frames=" + _framesSinceTelemetry +
                    "; fullRescans=" + _rescansSinceTelemetry +
                    "; activationNotifications=" + _activationNotificationsSinceTelemetry +
                    "; activationHook=" + _activationHookAvailable +
                    "; rescanMs=" + (int)(_rescanIntervalSeconds * 1000f));

                _telemetryAccumulator = 0f;
                _framesSinceTelemetry = 0;
                _rescansSinceTelemetry = 0;
                _activationNotificationsSinceTelemetry = 0;
            }
        }

        public override void OnRemoveBehavior()
        {
            StatusEffectActivationBridge.UnregisterOwner(this);
            _components.Clear();
            _knownComponents.Clear();
            _activeComponents.Clear();
            _pendingPermanentEffects.Clear();
            _scratch.Clear();
            lock (_activationQueueLock)
            {
                _pendingActivations.Clear();
                _queuedActivations.Clear();
            }
            base.OnRemoveBehavior();
        }

        internal void NotifyComponentActivated(AgentComponent component)
        {
            if (component == null) return;

            lock (_activationQueueLock)
            {
                if (_queuedActivations.Add(component))
                {
                    _pendingActivations.Enqueue(component);
                    _activationNotificationsSinceTelemetry++;
                }
            }
        }

        private AgentComponent GetOrCreateComponent(Agent agent, out bool created)
        {
            created = false;
            AgentComponent existing;
            if (_components.TryGetValue(agent, out existing))
            {
                return existing;
            }

            existing = InvokeUnwrapped(_getComponentMethod, agent, null) as AgentComponent;
            if (existing == null && agent.IsHuman)
            {
                existing = _componentConstructor.Invoke(new object[] { agent }) as AgentComponent;
                if (existing != null)
                {
                    agent.AddComponent(existing);
                    created = true;
                }
            }

            if (existing != null)
            {
                _components[agent] = existing;
                _knownComponents.Add(existing);
                StatusEffectActivationBridge.Register(existing, this);
                if (NeedsTick(existing))
                {
                    _activeComponents.Add(existing);
                }
            }

            return existing;
        }

        private bool TryGetComponent(Agent agent, out AgentComponent component)
        {
            if (_components.TryGetValue(agent, out component))
            {
                return component != null;
            }

            bool created;
            component = GetOrCreateComponent(agent, out created);
            return component != null;
        }

        private void ForgetAgent(Agent agent)
        {
            if (agent == null)
            {
                return;
            }

            AgentComponent component;
            if (_components.TryGetValue(agent, out component))
            {
                StatusEffectActivationBridge.Unregister(component);
                _activeComponents.Remove(component);
                _knownComponents.Remove(component);
                _components.Remove(agent);
            }
        }

        private void DrainPermanentEffectQueue()
        {
            while (_pendingPermanentEffects.Count > 0)
            {
                var agent = _pendingPermanentEffects.Dequeue();
                if (agent == null)
                {
                    continue;
                }

                InvokeUnwrapped(_checkPermanentEffectsMethod, _originalLogic, new object[] { agent });
            }
        }

        private void DrainActivationQueue()
        {
            while (true)
            {
                AgentComponent component;
                lock (_activationQueueLock)
                {
                    if (_pendingActivations.Count == 0) break;
                    component = _pendingActivations.Dequeue();
                    _queuedActivations.Remove(component);
                }

                if (component != null && _knownComponents.Contains(component) && NeedsTick(component))
                {
                    _activeComponents.Add(component);
                }
            }
        }

        private void ForceRescan()
        {
            _rescanAccumulator = _rescanIntervalSeconds;
        }

        private void RescanForActiveComponents()
        {
            _rescansSinceTelemetry++;
            foreach (var component in _knownComponents)
            {
                if (component != null && NeedsTick(component))
                {
                    _activeComponents.Add(component);
                }
            }
        }

        private bool NeedsTick(AgentComponent component)
        {
            var value = _needsTickProperty.GetValue(component, null);
            return value is bool && (bool)value;
        }

        private static object InvokeUnwrapped(MethodInfo method, object instance, object[] args)
        {
            try
            {
                return method.Invoke(instance, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }
}
