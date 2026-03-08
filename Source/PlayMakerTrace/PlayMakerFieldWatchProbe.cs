using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace DebugMod.PlayMakerTrace
{
    internal sealed class PlayMakerFieldWatchProbe : IPlayMakerTraceProbe
    {
        private readonly PlayMakerTraceRuntime _runtime;
        private readonly Func<PlayMakerTraceProfile?> _activeProfileAccessor;
        private readonly Dictionary<string, FieldInfo?> _fieldCache = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Type?> _watchTypeCache = new(StringComparer.Ordinal);

        private bool _attached;
        private GameObject? _driverObject;
        private PlayMakerTraceSamplerDriver? _driver;
        private float _nextCadenceTime;

        internal PlayMakerFieldWatchProbe(PlayMakerTraceRuntime runtime, Func<PlayMakerTraceProfile?> activeProfileAccessor)
        {
            _runtime = runtime;
            _activeProfileAccessor = activeProfileAccessor;
        }

        public string ProbeType => "field_watch";

        public void Attach()
        {
            if (_attached)
            {
                return;
            }

            On.HealthManager.Hit += OnHealthManagerHit;
            On.HealthManager.TakeDamage += OnHealthManagerTakeDamage;
            On.HealthManager.Invincible += OnHealthManagerInvincible;
            On.HealthManager.NonFatalHit += OnHealthManagerNonFatalHit;
            On.LimitSendEvents.Add += OnLimitSendEventsAdd;
            On.LimitSendEvents.OnEnable += OnLimitSendEventsOnEnable;

            _driverObject = new GameObject("PMTrace.FieldWatchProbeDriver")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            UnityEngine.Object.DontDestroyOnLoad(_driverObject);
            _driver = _driverObject.AddComponent<PlayMakerTraceSamplerDriver>();
            _driver.Tick = OnTick;

            _nextCadenceTime = 0f;
            _attached = true;
        }

        public void Detach()
        {
            if (!_attached)
            {
                return;
            }

            On.HealthManager.Hit -= OnHealthManagerHit;
            On.HealthManager.TakeDamage -= OnHealthManagerTakeDamage;
            On.HealthManager.Invincible -= OnHealthManagerInvincible;
            On.HealthManager.NonFatalHit -= OnHealthManagerNonFatalHit;
            On.LimitSendEvents.Add -= OnLimitSendEventsAdd;
            On.LimitSendEvents.OnEnable -= OnLimitSendEventsOnEnable;

            if (_driver != null)
            {
                _driver.Tick = null;
            }

            if (_driverObject != null)
            {
                UnityEngine.Object.Destroy(_driverObject);
            }

            _driver = null;
            _driverObject = null;
            _attached = false;
        }

        private void OnHealthManagerHit(On.HealthManager.orig_Hit orig, HealthManager self, HitInstance hitInstance)
        {
            if (ShouldEmitDamageEvent())
            {
                CaptureObjectFields(self, "health_manager_hit_pre");
            }

            orig(self, hitInstance);

            if (ShouldEmitDamageEvent())
            {
                CaptureObjectFields(self, "health_manager_hit_post");
            }
        }

        private void OnHealthManagerTakeDamage(On.HealthManager.orig_TakeDamage orig, HealthManager self, HitInstance hitInstance)
        {
            if (ShouldEmitDamageEvent())
            {
                CaptureObjectFields(self, "health_manager_take_damage_pre");
            }

            orig(self, hitInstance);

            if (ShouldEmitDamageEvent())
            {
                CaptureObjectFields(self, "health_manager_take_damage_post");
            }
        }

        private void OnHealthManagerInvincible(On.HealthManager.orig_Invincible orig, HealthManager self, HitInstance hitInstance)
        {
            if (ShouldEmitDamageEvent())
            {
                CaptureObjectFields(self, "health_manager_invincible_pre");
            }

            orig(self, hitInstance);

            if (ShouldEmitDamageEvent())
            {
                CaptureObjectFields(self, "health_manager_invincible_post");
            }
        }

        private void OnHealthManagerNonFatalHit(On.HealthManager.orig_NonFatalHit orig, HealthManager self, bool ignoreEvasion)
        {
            if (ShouldEmitDamageEvent())
            {
                CaptureObjectFields(self, "health_manager_non_fatal_hit_pre");
            }

            orig(self, ignoreEvasion);

            if (ShouldEmitDamageEvent())
            {
                CaptureObjectFields(self, "health_manager_non_fatal_hit_post");
            }
        }

        private bool OnLimitSendEventsAdd(On.LimitSendEvents.orig_Add orig, LimitSendEvents self, GameObject obj)
        {
            if (ShouldEmitLimitSendEvent())
            {
                CaptureObjectFields(self, "limit_send_add_pre");
            }

            bool result = orig(self, obj);

            if (ShouldEmitLimitSendEvent())
            {
                CaptureObjectFields(self, "limit_send_add_post");
            }

            return result;
        }

        private void OnLimitSendEventsOnEnable(On.LimitSendEvents.orig_OnEnable orig, LimitSendEvents self)
        {
            if (ShouldEmitLimitSendEvent())
            {
                CaptureObjectFields(self, "limit_send_on_enable_pre");
            }

            orig(self);

            if (ShouldEmitLimitSendEvent())
            {
                CaptureObjectFields(self, "limit_send_on_enable_post");
            }
        }

        private void OnTick(float unscaledTime)
        {
            if (!_runtime.IsProbeEnabled(ProbeType))
            {
                return;
            }

            PlayMakerTraceProfile? profile = _activeProfileAccessor();
            PlayMakerTraceFieldWatchProbeConfig? config = profile?.FieldWatchProbe;
            if (profile == null ||
                config == null ||
                config.CadenceSeconds <= 0f ||
                profile.FieldWatches.Count == 0)
            {
                return;
            }

            if (unscaledTime < _nextCadenceTime)
            {
                return;
            }

            _nextCadenceTime = unscaledTime + config.CadenceSeconds;

            int remainingBudget = Math.Max(1, config.MaxObjectsPerSample);
            foreach (IGrouping<string, PlayMakerTraceFieldWatchConfig> watchGroup in profile.FieldWatches.GroupBy(watch => watch.TypeName))
            {
                if (remainingBudget <= 0)
                {
                    break;
                }

                Type? watchType = ResolveWatchType(watchGroup.Key);
                if (watchType == null)
                {
                    continue;
                }

                UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(watchType);
                foreach (UnityEngine.Object target in objects)
                {
                    if (remainingBudget <= 0)
                    {
                        break;
                    }

                    CaptureObjectFields(target, "cadence");
                    remainingBudget--;
                }
            }
        }

        private void CaptureObjectFields(object target, string trigger)
        {
            if (!_runtime.IsProbeEnabled(ProbeType))
            {
                return;
            }

            PlayMakerTraceProfile? profile = _activeProfileAccessor();
            PlayMakerTraceFieldWatchProbeConfig? config = profile?.FieldWatchProbe;
            if (profile == null || config == null || profile.FieldWatches.Count == 0)
            {
                return;
            }

            Type targetType = target.GetType();
            foreach (PlayMakerTraceFieldWatchConfig watch in profile.FieldWatches)
            {
                if (!IsWatchMatch(watch.TypeName, targetType))
                {
                    continue;
                }

                FieldInfo? field = ResolveField(targetType, watch.FieldName);
                if (field == null)
                {
                    continue;
                }

                object? value;
                try
                {
                    value = field.GetValue(target);
                }
                catch
                {
                    continue;
                }

                string sourceName = ResolveSourceName(target);
                int? sourceId = ResolveSourceInstanceId(target);

                _runtime.TryEmit(new PlayMakerTraceEventRequest
                {
                    ProbeType = ProbeType,
                    SceneName = PlayMakerTraceProbeUtil.ResolveSceneName(),
                    SourceObject = sourceName,
                    SourceInstanceId = sourceId,
                    EventName = trigger,
                    ComponentType = targetType.FullName ?? targetType.Name,
                    Payload = new
                    {
                        watch_type = watch.TypeName,
                        resolved_type = targetType.FullName ?? targetType.Name,
                        field_name = watch.FieldName,
                        value_type = value?.GetType().FullName,
                        value = PlayMakerTraceProbeUtil.SerializeFieldValue(value, config.SerializeCollectionsAsCount, config.MaxCollectionPreviewItems)
                    }
                });
            }
        }

        private bool ShouldEmitDamageEvent()
        {
            PlayMakerTraceFieldWatchProbeConfig? config = _activeProfileAccessor()?.FieldWatchProbe;
            return _runtime.IsProbeEnabled(ProbeType) && config != null && config.EmitOnDamageEvents;
        }

        private bool ShouldEmitLimitSendEvent()
        {
            PlayMakerTraceFieldWatchProbeConfig? config = _activeProfileAccessor()?.FieldWatchProbe;
            return _runtime.IsProbeEnabled(ProbeType) && config != null && config.EmitOnLimitSendEvents;
        }

        private Type? ResolveWatchType(string typeName)
        {
            if (_watchTypeCache.TryGetValue(typeName, out Type? cached))
            {
                return cached;
            }

            Type? resolved = PlayMakerTraceProbeUtil.ResolveType(typeName);
            _watchTypeCache[typeName] = resolved;
            return resolved;
        }

        private bool IsWatchMatch(string watchTypeName, Type targetType)
        {
            if (string.Equals(targetType.Name, watchTypeName, StringComparison.Ordinal) ||
                string.Equals(targetType.FullName, watchTypeName, StringComparison.Ordinal))
            {
                return true;
            }

            Type? watchType = ResolveWatchType(watchTypeName);
            return watchType != null && watchType.IsAssignableFrom(targetType);
        }

        private FieldInfo? ResolveField(Type targetType, string fieldName)
        {
            string key = $"{targetType.AssemblyQualifiedName}|{fieldName}";
            if (_fieldCache.TryGetValue(key, out FieldInfo? cached))
            {
                return cached;
            }

            FieldInfo? resolved = targetType.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            _fieldCache[key] = resolved;
            return resolved;
        }

        private static string ResolveSourceName(object target)
        {
            if (target is Component component && component.gameObject != null)
            {
                return component.gameObject.name;
            }

            if (target is GameObject gameObject)
            {
                return gameObject.name;
            }

            if (target is UnityEngine.Object unityObject)
            {
                return unityObject.name;
            }

            return target.GetType().Name;
        }

        private static int? ResolveSourceInstanceId(object target)
        {
            if (target is Component component && component.gameObject != null)
            {
                return component.gameObject.GetInstanceID();
            }

            if (target is UnityEngine.Object unityObject)
            {
                return unityObject.GetInstanceID();
            }

            return null;
        }
    }
}
