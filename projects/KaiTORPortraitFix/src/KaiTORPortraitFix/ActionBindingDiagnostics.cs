using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Tableaus;

namespace KaiTORPortraitFix
{
    /// <summary>
    /// Read-only diagnostics for the UI portrait bind/prone pose.
    ///
    /// The same hero is horizontal both in Save/Load (BasicCharacterTableau) and in an
    /// in-campaign skills UI, so this build compares the runtime animation binding state
    /// of BasicCharacterTableau and the normal CharacterTableau path. No action, race,
    /// skeleton, frame, equipment or save data is changed here.
    /// </summary>
    [HarmonyPatch]
    internal static class BasicActionBindingDiagnostics
    {
        [HarmonyTargetMethods]
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            var methods = typeof(BasicCharacterTableau)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => string.Equals(m.Name, "RefreshCharacterTableau", StringComparison.Ordinal))
                .Cast<MethodBase>()
                .ToArray();

            PortraitFixLog.Event("ACTION_BIND_TARGETS", "kind=BasicCharacterTableau; count=" + methods.Length);
            return methods;
        }

        [HarmonyPrefix]
        internal static void Prefix(object __instance, MethodBase __originalMethod)
        {
            ActionBindingInspector.Capture("BASIC_BEFORE", __instance, __originalMethod);
        }

        [HarmonyPostfix]
        internal static void Postfix(object __instance, MethodBase __originalMethod)
        {
            ActionBindingInspector.Capture("BASIC_AFTER", __instance, __originalMethod);
        }
    }

    [HarmonyPatch]
    internal static class CharacterActionBindingDiagnostics
    {
        [HarmonyTargetMethods]
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            var type = AccessTools.TypeByName("TaleWorlds.MountAndBlade.View.Tableaus.CharacterTableau");
            if (type == null)
            {
                PortraitFixLog.Event("ACTION_BIND_TARGETS", "kind=CharacterTableau; count=0; typeFound=false");
                return Array.Empty<MethodBase>();
            }

            var methods = type
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m =>
                    string.Equals(m.Name, "RefreshCharacterTableau", StringComparison.Ordinal) ||
                    string.Equals(m.Name, "RefreshCharacter", StringComparison.Ordinal))
                .Cast<MethodBase>()
                .Distinct()
                .ToArray();

            PortraitFixLog.Event(
                "ACTION_BIND_TARGETS",
                "kind=CharacterTableau; count=" + methods.Length + "; typeFound=true; methods=" +
                string.Join(",", methods.Select(m => m.Name)));
            return methods;
        }

        [HarmonyPrefix]
        internal static void Prefix(object __instance, MethodBase __originalMethod)
        {
            ActionBindingInspector.Capture("CHARACTER_BEFORE", __instance, __originalMethod);
        }

        [HarmonyPostfix]
        internal static void Postfix(object __instance, MethodBase __originalMethod)
        {
            ActionBindingInspector.Capture("CHARACTER_AFTER", __instance, __originalMethod);
        }
    }

    internal static class ActionBindingInspector
    {
        private static int _basicBefore;
        private static int _basicAfter;
        private static int _characterBefore;
        private static int _characterAfter;
        private static int _typeMethodDumps;
        private const int MaxSamplesPerStage = 16;
        private const int MaxInterestingFields = 18;
        private const int MaxObjectProbes = 8;

        internal static void Capture(string stage, object instance, MethodBase originalMethod)
        {
            if (instance == null)
                return;

            var sample = NextSample(stage);
            if (sample <= 0 || sample > MaxSamplesPerStage)
                return;

            try
            {
                var idle = ActionIndexCache.Create("act_inventory_idle");
                var idleStart = ActionIndexCache.Create("act_inventory_idle_start");
                var type = instance.GetType();

                PortraitFixLog.Event(
                    "ACTION_BIND",
                    "stage=" + stage +
                    "; sample=" + sample +
                    "; type=" + type.FullName +
                    "; method=" + (originalMethod?.Name ?? string.Empty) +
                    "; idleIndex=" + idle.Index +
                    "; idleStartIndex=" + idleStart.Index +
                    "; race=" + ReadNamedValue(instance, "_race", "Race") +
                    "; skeletonName=" + ReadNamedValue(instance, "_skeletonName", "SkeletonName") +
                    "; isFemale=" + ReadNamedValue(instance, "_isFemale", "IsFemale"));

                DumpInterestingFields(stage, sample, instance);
                DumpObjectGraph(stage, sample, instance, idle, idleStart);
            }
            catch (Exception ex)
            {
                PortraitFixLog.Event(
                    "ACTION_BIND_ERROR",
                    "stage=" + stage + "; sample=" + sample + "; error=" + ex.GetType().Name + ": " + Sanitize(ex.Message));
            }
        }

        private static int NextSample(string stage)
        {
            switch (stage)
            {
                case "BASIC_BEFORE": return Interlocked.Increment(ref _basicBefore);
                case "BASIC_AFTER": return Interlocked.Increment(ref _basicAfter);
                case "CHARACTER_BEFORE": return Interlocked.Increment(ref _characterBefore);
                case "CHARACTER_AFTER": return Interlocked.Increment(ref _characterAfter);
                default: return -1;
            }
        }

        private static void DumpInterestingFields(string stage, int sample, object instance)
        {
            var fields = GetAllFields(instance.GetType())
                .Where(IsInterestingField)
                .Take(MaxInterestingFields)
                .ToArray();

            foreach (var field in fields)
            {
                object value = null;
                string error = null;
                try { value = field.GetValue(instance); }
                catch (Exception ex) { error = ex.GetType().Name; }

                PortraitFixLog.Event(
                    "ACTION_BIND_FIELD",
                    "stage=" + stage +
                    "; sample=" + sample +
                    "; field=" + field.Name +
                    "; fieldType=" + field.FieldType.FullName +
                    "; value=" + (error == null ? DescribeSimple(value) : "<error:" + error + ">"));
            }
        }

        private static void DumpObjectGraph(
            string stage,
            int sample,
            object instance,
            ActionIndexCache idle,
            ActionIndexCache idleStart)
        {
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var queue = new Queue<Tuple<string, object, int>>();
            queue.Enqueue(Tuple.Create("self", instance, 0));
            var probes = 0;

            while (queue.Count > 0 && probes < MaxObjectProbes)
            {
                var item = queue.Dequeue();
                var path = item.Item1;
                var value = item.Item2;
                var depth = item.Item3;
                if (value == null || !seen.Add(value))
                    continue;

                var type = value.GetType();
                if (depth > 0 && IsBindingObject(type, path))
                {
                    probes++;
                    PortraitFixLog.Event(
                        "ACTION_BIND_OBJECT",
                        "stage=" + stage +
                        "; sample=" + sample +
                        "; path=" + path +
                        "; type=" + type.FullName +
                        "; desc=" + DescribeKnownMembers(value));

                    ProbeActionAnimationGetters(stage, sample, path, value, idle, idleStart);
                    DumpGetterSurfaceOnce(type, stage, sample, path);
                }

                if (depth >= 2)
                    continue;

                foreach (var field in GetAllFields(type).Where(IsGraphField).Take(14))
                {
                    try
                    {
                        var child = field.GetValue(value);
                        if (child != null)
                            queue.Enqueue(Tuple.Create(path + "." + field.Name, child, depth + 1));
                    }
                    catch { }
                }

                foreach (var getterName in new[] { "GetSkeleton", "GetActionSet", "GetMonster", "GetEntity", "GetVisuals" })
                {
                    try
                    {
                        var getter = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            .FirstOrDefault(m => m.Name == getterName && m.GetParameters().Length == 0 && m.ReturnType != typeof(void));
                        if (getter == null)
                            continue;
                        var child = getter.Invoke(value, null);
                        if (child != null)
                            queue.Enqueue(Tuple.Create(path + "." + getterName + "()", child, depth + 1));
                    }
                    catch { }
                }
            }
        }

        private static void ProbeActionAnimationGetters(
            string stage,
            int sample,
            string path,
            object value,
            ActionIndexCache idle,
            ActionIndexCache idleStart)
        {
            var methods = value.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m =>
                    m.Name.StartsWith("Get", StringComparison.Ordinal) &&
                    (m.Name.IndexOf("Action", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     m.Name.IndexOf("Animation", StringComparison.OrdinalIgnoreCase) >= 0) &&
                    m.ReturnType != typeof(void))
                .Take(20)
                .ToArray();

            foreach (var method in methods)
            {
                TryInvokeGetter(stage, sample, path, value, method, "idle", idle);
                TryInvokeGetter(stage, sample, path, value, method, "idleStart", idleStart);
            }
        }

        private static void TryInvokeGetter(
            string stage,
            int sample,
            string path,
            object target,
            MethodInfo method,
            string actionLabel,
            ActionIndexCache action)
        {
            var parameters = method.GetParameters();
            object[] args;

            if (parameters.Length == 0)
            {
                args = Array.Empty<object>();
            }
            else if (parameters.Length == 1)
            {
                var p = parameters[0].ParameterType;
                if (p == typeof(int))
                {
                    // Channel-based getters use channel 0; index-based getters commonly use the
                    // action index. Prefer channel 0 for names mentioning Channel, otherwise action index.
                    args = new object[] {
                        method.Name.IndexOf("Channel", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : action.Index
                    };
                }
                else if (p == typeof(string))
                {
                    args = new object[] { actionLabel == "idleStart" ? "act_inventory_idle_start" : "act_inventory_idle" };
                }
                else if (p == typeof(ActionIndexCache))
                {
                    args = new object[] { action };
                }
                else
                {
                    return;
                }
            }
            else
            {
                return;
            }

            try
            {
                var result = method.Invoke(target, args);
                if (!IsSimpleResult(result))
                    return;

                PortraitFixLog.Event(
                    "ACTION_BIND_GETTER",
                    "stage=" + stage +
                    "; sample=" + sample +
                    "; path=" + path +
                    "; getter=" + method.Name +
                    "; action=" + actionLabel +
                    "; result=" + DescribeSimple(result));
            }
            catch (TargetInvocationException tie)
            {
                var inner = tie.InnerException;
                PortraitFixLog.Event(
                    "ACTION_BIND_GETTER",
                    "stage=" + stage +
                    "; sample=" + sample +
                    "; path=" + path +
                    "; getter=" + method.Name +
                    "; action=" + actionLabel +
                    "; error=" + (inner?.GetType().Name ?? tie.GetType().Name));
            }
            catch (Exception ex)
            {
                PortraitFixLog.Event(
                    "ACTION_BIND_GETTER",
                    "stage=" + stage +
                    "; sample=" + sample +
                    "; path=" + path +
                    "; getter=" + method.Name +
                    "; action=" + actionLabel +
                    "; error=" + ex.GetType().Name);
            }
        }

        private static void DumpGetterSurfaceOnce(Type type, string stage, int sample, string path)
        {
            if (Interlocked.Increment(ref _typeMethodDumps) > 12)
                return;

            try
            {
                var names = type
                    .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m =>
                        m.Name.IndexOf("Action", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        m.Name.IndexOf("Animation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        m.Name.IndexOf("Skeleton", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(FormatMethod)
                    .Distinct()
                    .Take(24)
                    .ToArray();

                PortraitFixLog.Event(
                    "ACTION_BIND_SURFACE",
                    "stage=" + stage +
                    "; sample=" + sample +
                    "; path=" + path +
                    "; type=" + type.FullName +
                    "; methods=" + Sanitize(string.Join(" | ", names)));
            }
            catch { }
        }

        private static string ReadNamedValue(object instance, params string[] names)
        {
            foreach (var name in names)
            {
                try
                {
                    var field = FindField(instance.GetType(), name);
                    if (field != null)
                        return DescribeSimple(field.GetValue(instance));

                    var property = instance.GetType().GetProperty(
                        name,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                    if (property != null && property.GetIndexParameters().Length == 0)
                        return DescribeSimple(property.GetValue(instance, null));
                }
                catch { }
            }
            return "<missing>";
        }

        private static IEnumerable<FieldInfo> GetAllFields(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    yield return field;
            }
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            return null;
        }

        private static bool IsInterestingField(FieldInfo field)
        {
            var text = (field.Name + " " + field.FieldType.FullName).ToLowerInvariant();
            return text.Contains("race") ||
                   text.Contains("skeleton") ||
                   text.Contains("action") ||
                   text.Contains("animation") ||
                   text.Contains("monster") ||
                   text.Contains("agentvisual") ||
                   text.Contains("entity") ||
                   text.Contains("spawnframe");
        }

        private static bool IsGraphField(FieldInfo field)
        {
            if (field.FieldType.IsPrimitive || field.FieldType == typeof(string) || field.FieldType.IsEnum)
                return false;
            var text = (field.Name + " " + field.FieldType.FullName).ToLowerInvariant();
            return text.Contains("skeleton") ||
                   text.Contains("action") ||
                   text.Contains("animation") ||
                   text.Contains("monster") ||
                   text.Contains("agentvisual") ||
                   text.Contains("entity") ||
                   text.Contains("visual");
        }

        private static bool IsBindingObject(Type type, string path)
        {
            var text = (type.FullName + " " + path).ToLowerInvariant();
            return text.Contains("skeleton") ||
                   text.Contains("actionset") ||
                   text.Contains("animation") ||
                   text.Contains("monster") ||
                   text.Contains("agentvisual") ||
                   text.Contains("gameentity") ||
                   text.Contains("entity");
        }

        private static string DescribeKnownMembers(object value)
        {
            var parts = new List<string>();
            var type = value.GetType();
            foreach (var name in new[] { "Name", "StringId", "Id", "Index", "ActionSet", "Skeleton", "Monster" })
            {
                try
                {
                    var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                    if (prop != null && prop.GetIndexParameters().Length == 0)
                    {
                        var propValue = prop.GetValue(value, null);
                        if (IsSimpleResult(propValue))
                            parts.Add(name + "=" + DescribeSimple(propValue));
                    }
                }
                catch { }
            }

            if (parts.Count == 0)
                parts.Add("value=" + DescribeSimple(value));
            return Sanitize(string.Join(";", parts));
        }

        private static string DescribeSimple(object value)
        {
            if (value == null)
                return "<null>";

            if (value is string s)
                return Sanitize(s);
            if (value is bool b)
                return b ? "true" : "false";
            if (value is ActionIndexCache action)
                return "ActionIndexCache(" + action.Index + ")";
            if (value is IFormattable formattable &&
                (value.GetType().IsPrimitive || value is decimal || value.GetType().IsEnum))
                return Sanitize(formattable.ToString(null, CultureInfo.InvariantCulture));

            var text = value.ToString();
            if (string.IsNullOrEmpty(text) || text == value.GetType().FullName)
                return "<" + value.GetType().Name + ">";
            return Sanitize(text);
        }

        private static bool IsSimpleResult(object value)
        {
            if (value == null)
                return true;
            var type = value.GetType();
            return value is string || value is ActionIndexCache || type.IsPrimitive || type.IsEnum || value is decimal;
        }

        private static string FormatMethod(MethodInfo method)
        {
            var parameters = string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name));
            return method.Name + "(" + parameters + ")->" + method.ReturnType.Name;
        }

        private static string Sanitize(string text)
        {
            if (text == null)
                return string.Empty;
            text = text.Replace("\r", " ").Replace("\n", " ").Replace("|", "/");
            return text.Length <= 360 ? text : text.Substring(0, 360) + "...";
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
