using System;
using System.Collections.Generic;
using UnityEngine;

namespace DebugMod.PlayMakerTrace
{
    internal sealed class PlayMakerComponentToggleProbe : IPlayMakerTraceProbe
    {
        private readonly PlayMakerTraceRuntime _runtime;
        private readonly Func<PlayMakerTraceProfile?> _activeProfileAccessor;
        private readonly Dictionary<int, bool> _lastEnabledByInstanceId = new();

        private bool _attached;
        private GameObject? _driverObject;
        private PlayMakerTraceSamplerDriver? _driver;
        private float _nextSampleTime;

        internal PlayMakerComponentToggleProbe(PlayMakerTraceRuntime runtime, Func<PlayMakerTraceProfile?> activeProfileAccessor)
        {
            _runtime = runtime;
            _activeProfileAccessor = activeProfileAccessor;
        }

        public string ProbeType => "component_toggle";

        public void Attach()
        {
            if (_attached)
            {
                return;
            }

            _driverObject = new GameObject("PMTrace.ComponentToggleProbeDriver")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            UnityEngine.Object.DontDestroyOnLoad(_driverObject);
            _driver = _driverObject.AddComponent<PlayMakerTraceSamplerDriver>();
            _driver.Tick = OnTick;
            _nextSampleTime = 0f;
            _attached = true;
        }

        public void Detach()
        {
            if (!_attached)
            {
                return;
            }

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
            _lastEnabledByInstanceId.Clear();
            _attached = false;
        }

        private void OnTick(float unscaledTime)
        {
            if (!_runtime.IsProbeEnabled(ProbeType))
            {
                return;
            }

            PlayMakerTraceComponentToggleConfig? config = _activeProfileAccessor()?.ComponentToggle;
            if (config == null ||
                config.ComponentTypeAllowlist.Count == 0 ||
                config.PollIntervalSeconds <= 0f)
            {
                return;
            }

            if (unscaledTime < _nextSampleTime)
            {
                return;
            }

            _nextSampleTime = unscaledTime + config.PollIntervalSeconds;

            int remainingBudget = Math.Max(1, config.MaxComponentsPerSample);
            foreach (string typeName in config.ComponentTypeAllowlist)
            {
                if (remainingBudget <= 0)
                {
                    break;
                }

                Type? componentType = PlayMakerTraceProbeUtil.ResolveType(typeName);
                if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
                {
                    continue;
                }

                UnityEngine.Object[] components = Resources.FindObjectsOfTypeAll(componentType);
                foreach (UnityEngine.Object candidate in components)
                {
                    if (remainingBudget <= 0)
                    {
                        break;
                    }

                    if (candidate is not Component component || component.gameObject == null)
                    {
                        continue;
                    }

                    if (!config.IncludeInactiveGameObjects && !component.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    int instanceId = component.GetInstanceID();
                    bool isEnabled = PlayMakerTraceProbeUtil.IsComponentEnabled(component);

                    if (!_lastEnabledByInstanceId.TryGetValue(instanceId, out bool previousEnabled))
                    {
                        _lastEnabledByInstanceId[instanceId] = isEnabled;
                        continue;
                    }

                    if (previousEnabled == isEnabled)
                    {
                        continue;
                    }

                    _lastEnabledByInstanceId[instanceId] = isEnabled;
                    remainingBudget--;

                    _runtime.TryEmit(new PlayMakerTraceEventRequest
                    {
                        ProbeType = ProbeType,
                        SceneName = PlayMakerTraceProbeUtil.ResolveSceneName(),
                        SourceObject = component.gameObject.name,
                        SourceInstanceId = component.gameObject.GetInstanceID(),
                        EventName = isEnabled ? "component_enabled" : "component_disabled",
                        ComponentType = component.GetType().FullName ?? component.GetType().Name,
                        Payload = new
                        {
                            component_instance_id = instanceId,
                            enabled = isEnabled,
                            active_self = component.gameObject.activeSelf,
                            active_in_hierarchy = component.gameObject.activeInHierarchy,
                            poll_interval_seconds = config.PollIntervalSeconds
                        }
                    });
                }
            }
        }
    }
}
