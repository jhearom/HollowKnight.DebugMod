using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DebugMod.PlayMakerTrace
{
    internal static class PlayMakerTraceProbeUtil
    {
        private static readonly Dictionary<string, Type?> _typeCache = new(StringComparer.Ordinal);

        internal static Type? ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            string key = typeName.Trim();
            if (_typeCache.TryGetValue(key, out Type? cached))
            {
                return cached;
            }

            Type? resolved = Type.GetType(key, false);
            if (resolved == null)
            {
                foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    resolved = assembly.GetType(key, false);
                    if (resolved != null)
                    {
                        break;
                    }
                }
            }

            if (resolved == null)
            {
                foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        resolved = assembly.GetTypes().FirstOrDefault(type => string.Equals(type.Name, key, StringComparison.Ordinal));
                        if (resolved != null)
                        {
                            break;
                        }
                    }
                    catch
                    {
                        // Some assemblies may fail type load; safe to skip while resolving a best-effort watch type.
                    }
                }
            }

            _typeCache[key] = resolved;
            return resolved;
        }

        internal static string ResolveSceneName(string? fallback = "")
        {
            return !string.IsNullOrWhiteSpace(fallback)
                ? fallback ?? ""
                : (GameManager.instance != null ? GameManager.instance.sceneName : "");
        }

        internal static bool MatchesPattern(PlayMakerTracePatternFilter? filter, string input)
        {
            if (filter == null || string.IsNullOrWhiteSpace(filter.Mode))
            {
                return true;
            }

            string mode = filter.Mode.Trim().ToLowerInvariant();
            string value = filter.Value ?? "";
            StringComparison comparison = filter.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            switch (mode)
            {
                case "off":
                    return true;
                case "exact":
                    return string.Equals(input, value, comparison);
                case "contains":
                    return input.IndexOf(value, comparison) >= 0;
                case "regex":
                    RegexOptions options = RegexOptions.CultureInvariant;
                    if (filter.IgnoreCase)
                    {
                        options |= RegexOptions.IgnoreCase;
                    }

                    try
                    {
                        return Regex.IsMatch(input, value, options);
                    }
                    catch
                    {
                        return false;
                    }
                default:
                    return true;
            }
        }

        internal static bool IsComponentEnabled(Component component)
        {
            if (component is Behaviour behaviour)
            {
                return behaviour.enabled;
            }

            if (component is Collider2D collider2D)
            {
                return collider2D.enabled;
            }

            if (component is Renderer renderer)
            {
                return renderer.enabled;
            }

            return component.gameObject != null && component.gameObject.activeInHierarchy;
        }

        internal static object? SerializeFieldValue(object? value, bool collectionsAsCount, int maxCollectionPreviewItems)
        {
            if (value == null)
            {
                return null;
            }

            if (value is string || value.GetType().IsPrimitive || value is decimal)
            {
                return value;
            }

            if (value is Enum)
            {
                return value.ToString();
            }

            if (value is UnityEngine.Object unityObject)
            {
                return new
                {
                    unity_type = unityObject.GetType().FullName ?? unityObject.GetType().Name,
                    name = unityObject.name,
                    instance_id = unityObject.GetInstanceID()
                };
            }

            if (value is IDictionary dictionary)
            {
                return collectionsAsCount
                    ? new { count = dictionary.Count }
                    : new
                    {
                        count = dictionary.Count,
                        preview = SnapshotEnumerable(dictionary.Keys.Cast<object?>(), maxCollectionPreviewItems)
                    };
            }

            if (value is ICollection collection)
            {
                return collectionsAsCount
                    ? new { count = collection.Count }
                    : new
                    {
                        count = collection.Count,
                        preview = SnapshotEnumerable(collection.Cast<object?>(), maxCollectionPreviewItems)
                    };
            }

            return value.ToString();
        }

        private static List<object?> SnapshotEnumerable(IEnumerable<object?> values, int maxItems)
        {
            int limit = Math.Max(1, maxItems);
            List<object?> snapshot = new();
            foreach (object? value in values)
            {
                if (snapshot.Count >= limit)
                {
                    break;
                }

                snapshot.Add(value is UnityEngine.Object unityObject
                    ? new
                    {
                        unity_type = unityObject.GetType().FullName ?? unityObject.GetType().Name,
                        name = unityObject.name,
                        instance_id = unityObject.GetInstanceID()
                    }
                    : value?.ToString());
            }

            return snapshot;
        }
    }
}
