using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DebugMod.PlayMakerTrace
{
    internal interface IPlayMakerTraceProbe
    {
        string ProbeType { get; }
        void Attach();
        void Detach();
    }

    internal sealed class PlayMakerTraceRuntime
    {
        private enum FilterMode
        {
            Off,
            Exact,
            Contains,
            Regex
        }

        private sealed class CompiledFilter
        {
            public FilterMode Mode { get; set; } = FilterMode.Off;
            public string Value { get; set; } = "";
            public bool IgnoreCase { get; set; } = true;
            public Regex? Pattern { get; set; }
        }

        private readonly List<PlayMakerTraceEventRecord> _rows = new();
        private readonly HashSet<string> _sceneAllowlist = new(StringComparer.Ordinal);
        private readonly HashSet<string> _enabledProbes = new(StringComparer.OrdinalIgnoreCase);

        private readonly Action<string> _logLine;
        private readonly Action<string> _logError;

        private CompiledFilter _gameObjectFilter = new();
        private CompiledFilter _fsmFilter = new();
        private CompiledFilter _eventFilter = new();
        private CompiledFilter _probeFilter = new();
        private CompiledFilter _componentTypeFilter = new();
        private int _maxRows = 50000;
        private long _nextSequenceId;

        internal PlayMakerTraceRuntime(Action<string> logLine, Action<string> logError)
        {
            _logLine = logLine;
            _logError = logError;
            ResetSession();
        }

        internal string SessionId { get; private set; } = "";
        internal bool Enabled { get; private set; }
        internal int DroppedRows { get; private set; }
        internal bool HasUnflushedRows { get; private set; }

        internal IReadOnlyList<PlayMakerTraceEventRecord> Rows => _rows;
        internal int MaxRows => _maxRows;
        internal IReadOnlyCollection<string> EnabledProbes => _enabledProbes;

        internal void ResetSession()
        {
            SessionId = Guid.NewGuid().ToString("N");
            _nextSequenceId = 0;
        }

        internal void SetEnabled(bool enabled)
        {
            Enabled = enabled;
        }

        internal void ClearBuffer()
        {
            _rows.Clear();
            DroppedRows = 0;
            HasUnflushedRows = false;
        }

        internal void MarkFlushed()
        {
            HasUnflushedRows = false;
        }

        internal void Configure(PlayMakerTraceConfig config, PlayMakerTraceProfile profile)
        {
            _maxRows = config.MaxRows < 1 ? 1 : config.MaxRows;

            _enabledProbes.Clear();
            foreach (string probeType in profile.EnabledProbes.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                _enabledProbes.Add(probeType.Trim());
            }

            _sceneAllowlist.Clear();
            foreach (string scene in profile.Filters.SceneAllowlist.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                _sceneAllowlist.Add(scene.Trim());
            }

            _gameObjectFilter = CompilePatternFilter(profile.Filters.GameObjectFilter, "gameObjectFilter");
            _fsmFilter = CompilePatternFilter(profile.Filters.FsmFilter, "fsmFilter");
            _eventFilter = CompilePatternFilter(profile.Filters.EventFilter, "eventFilter");
            _probeFilter = CompilePatternFilter(profile.Filters.ProbeFilter, "probeFilter");
            _componentTypeFilter = CompilePatternFilter(profile.Filters.ComponentTypeFilter, "componentTypeFilter");
        }

        internal bool IsProbeEnabled(string probeType)
        {
            return Enabled && !string.IsNullOrWhiteSpace(probeType) && _enabledProbes.Contains(probeType);
        }

        internal bool TryEmit(PlayMakerTraceEventRequest request)
        {
            if (!Enabled)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.ProbeType) || !_enabledProbes.Contains(request.ProbeType))
            {
                return false;
            }

            string sceneName = request.SceneName;
            if (string.IsNullOrWhiteSpace(sceneName) && GameManager.instance != null)
            {
                sceneName = GameManager.instance.sceneName;
            }

            string objectValue = !string.IsNullOrWhiteSpace(request.SourceObject)
                ? request.SourceObject ?? ""
                : request.TargetObject ?? "";
            string fsmValue = request.SourceFsm ?? "";
            string eventValue = request.EventName ?? "";
            string probeValue = request.ProbeType;
            string componentValue = request.ComponentType ?? "";

            if (!MatchesScene(sceneName) ||
                !MatchesPattern(_gameObjectFilter, objectValue) ||
                !MatchesPattern(_fsmFilter, fsmValue) ||
                !MatchesPattern(_eventFilter, eventValue) ||
                !MatchesPattern(_probeFilter, probeValue) ||
                !MatchesPattern(_componentTypeFilter, componentValue))
            {
                return false;
            }

            if (_rows.Count >= _maxRows)
            {
                DroppedRows++;
                return false;
            }

            float currentTime = Time.time;
            float fixedTime = Time.fixedTime;
            float fixedDelta = Time.fixedDeltaTime;
            int fixedFrameProxy = fixedDelta > 0f ? Mathf.RoundToInt(fixedTime / fixedDelta) : -1;

            PlayMakerTraceEventRecord row = new()
            {
                SessionId = SessionId,
                SequenceId = ++_nextSequenceId,
                EventType = request.ProbeType,
                SceneName = sceneName,
                FrameCount = Time.frameCount,
                FixedFrameCount = fixedFrameProxy,
                Time = currentTime,
                FixedTime = fixedTime,
                TimeMinusFixedTime = currentTime - fixedTime,
                DeltaTime = Time.deltaTime,
                FixedDeltaTime = fixedDelta,
                UnscaledTime = Time.unscaledTime,
                RealtimeSinceStartup = Time.realtimeSinceStartup,
                SourceObject = request.SourceObject,
                SourceFsm = request.SourceFsm,
                SourceInstanceId = request.SourceInstanceId,
                TargetObject = request.TargetObject,
                TargetInstanceId = request.TargetInstanceId,
                EventName = request.EventName,
                FromState = request.FromState,
                ToState = request.ToState,
                ComponentType = request.ComponentType,
                Payload = request.Payload
            };

            _rows.Add(row);
            HasUnflushedRows = true;
            return true;
        }

        internal string DescribeFilterSummary()
        {
            string sceneFilter = _sceneAllowlist.Count > 0
                ? string.Join(", ", _sceneAllowlist.Take(5)) + (_sceneAllowlist.Count > 5 ? ", ..." : "")
                : "(off)";

            return $"scene={sceneFilter}, go={DescribeFilter(_gameObjectFilter)}, fsm={DescribeFilter(_fsmFilter)}, event={DescribeFilter(_eventFilter)}, probe={DescribeFilter(_probeFilter)}, component={DescribeFilter(_componentTypeFilter)}";
        }

        private bool MatchesScene(string sceneName)
        {
            return _sceneAllowlist.Count == 0 || _sceneAllowlist.Contains(sceneName);
        }

        private static bool MatchesPattern(CompiledFilter filter, string input)
        {
            switch (filter.Mode)
            {
                case FilterMode.Off:
                    return true;
                case FilterMode.Exact:
                    return string.Equals(input, filter.Value, filter.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
                case FilterMode.Contains:
                    if (filter.IgnoreCase)
                    {
                        return input.IndexOf(filter.Value, StringComparison.OrdinalIgnoreCase) >= 0;
                    }

                    return input.Contains(filter.Value);
                case FilterMode.Regex:
                    return filter.Pattern != null && filter.Pattern.IsMatch(input);
                default:
                    return true;
            }
        }

        private static string DescribeFilter(CompiledFilter filter)
        {
            if (filter.Mode == FilterMode.Off)
            {
                return "(off)";
            }

            return $"{filter.Mode.ToString().ToLowerInvariant()}:{filter.Value}";
        }

        private CompiledFilter CompilePatternFilter(PlayMakerTracePatternFilter source, string label)
        {
            CompiledFilter compiled = new()
            {
                Mode = ParseFilterMode(source.Mode),
                Value = source.Value ?? "",
                IgnoreCase = source.IgnoreCase
            };

            if (compiled.Mode != FilterMode.Regex || string.IsNullOrWhiteSpace(compiled.Value))
            {
                return compiled;
            }

            RegexOptions options = RegexOptions.CultureInvariant;
            if (compiled.IgnoreCase)
            {
                options |= RegexOptions.IgnoreCase;
            }

            try
            {
                compiled.Pattern = new Regex(compiled.Value, options);
            }
            catch (Exception e)
            {
                _logError($"PM Trace regex compile failed for {label}: {e.Message}");
                _logLine($"PM Trace invalid regex for {label}, disabling this filter.");
                compiled.Mode = FilterMode.Off;
                compiled.Pattern = null;
            }

            return compiled;
        }

        private static FilterMode ParseFilterMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                return FilterMode.Off;
            }

            switch (mode.Trim().ToLowerInvariant())
            {
                case "exact":
                    return FilterMode.Exact;
                case "contains":
                    return FilterMode.Contains;
                case "regex":
                    return FilterMode.Regex;
                default:
                    return FilterMode.Off;
            }
        }
    }
}
