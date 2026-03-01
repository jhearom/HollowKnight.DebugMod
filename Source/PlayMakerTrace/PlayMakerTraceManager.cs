using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HutongGames.PlayMaker;
using Newtonsoft.Json;
using UnityEngine;

namespace DebugMod.PlayMakerTrace
{
    internal static class PlayMakerTraceManager
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

        private static bool _initialized;
        private static bool _enabled;
        private static int _transitionHookDepth;
        private static int _droppedRows;
        private static string _sessionId = Guid.NewGuid().ToString("N");

        private static PlayMakerTraceConfig _config = PlayMakerTraceConfig.CreateDefault();
        private static readonly List<PlayMakerTraceRecord> _rows = new();
        private static readonly HashSet<string> _sceneAllowlist = new(StringComparer.Ordinal);
        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        private static CompiledFilter _gameObjectFilter = new();
        private static CompiledFilter _fsmFilter = new();
        private static CompiledFilter _eventFilter = new();
        private static string _configPath = "";

        internal static bool IsEnabled => _enabled;
        internal static int RowCount => _rows.Count;
        internal static int DroppedRowCount => _droppedRows;

        internal static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _sessionId = Guid.NewGuid().ToString("N");
            _configPath = Path.Combine(DebugMod.settings.ModBaseDirectory, "pmtrace_config.json");
            LoadConfig();
            Hook();
            _initialized = true;

            Console.AddLine("PM Trace initialized (default off unless config enabled)");
        }

        internal static void Shutdown()
        {
            if (!_initialized)
            {
                return;
            }

            Unhook();
            _initialized = false;
            _enabled = false;
        }

        internal static void Enable()
        {
            if (!_initialized)
            {
                Initialize();
            }

            _enabled = true;
            _config.Enabled = true;
            SaveConfig();
            Console.AddLine("PM Trace enabled");
        }

        internal static void Disable()
        {
            _enabled = false;
            _config.Enabled = false;
            SaveConfig();
            Console.AddLine("PM Trace disabled");
        }

        internal static void ClearBuffer()
        {
            _rows.Clear();
            _droppedRows = 0;
            Console.AddLine("PM Trace buffer cleared");
        }

        internal static void ReloadConfig()
        {
            LoadConfig();
            Console.AddLine("PM Trace config reloaded");
        }

        internal static string FlushToDisk()
        {
            if (!_initialized)
            {
                Initialize();
            }

            string outputDir = ResolveOutputDirectory();
            Directory.CreateDirectory(outputDir);

            string prefix = SanitizeFileNamePart(_config.Output.FilePrefix);
            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = "pmtrace";
            }

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string fileName = $"{prefix}_{stamp}_{_sessionId.Substring(0, 8)}.jsonl";
            string filePath = Path.Combine(outputDir, fileName);

            try
            {
                using StreamWriter writer = new(filePath, false, new UTF8Encoding(false));
                foreach (PlayMakerTraceRecord row in _rows)
                {
                    writer.WriteLine(JsonConvert.SerializeObject(row, _jsonSettings));
                }

                Console.AddLine($"PM Trace flush complete: {_rows.Count} rows -> {filePath}");
            }
            catch (Exception e)
            {
                DebugMod.instance.LogError("PM Trace flush failed: " + e);
                Console.AddLine("PM Trace flush failed. Check ModLog for details.");
                return "";
            }

            return filePath;
        }

        internal static void PrintStatus()
        {
            foreach (string line in GetStatusLines())
            {
                Console.AddLine(line);
            }
        }

        internal static List<string> GetStatusLines()
        {
            string sceneFilter = _sceneAllowlist.Count > 0
                ? string.Join(", ", _sceneAllowlist.Take(5)) + (_sceneAllowlist.Count > 5 ? ", ..." : "")
                : "(off)";

            return new List<string>
            {
                $"PM Trace status: enabled={_enabled}, rows={_rows.Count}, dropped={_droppedRows}",
                $"PM Trace config: {_configPath}",
                $"PM Trace output dir: {ResolveOutputDirectory()}",
                $"PM Trace filters: scene={sceneFilter}, go={DescribeFilter(_gameObjectFilter)}, fsm={DescribeFilter(_fsmFilter)}, event={DescribeFilter(_eventFilter)}"
            };
        }

        private static void Hook()
        {
            On.HutongGames.PlayMaker.Fsm.DoTransition += OnDoTransition;
            On.HutongGames.PlayMaker.Fsm.SetState += OnSetState;
        }

        private static void Unhook()
        {
            On.HutongGames.PlayMaker.Fsm.DoTransition -= OnDoTransition;
            On.HutongGames.PlayMaker.Fsm.SetState -= OnSetState;
        }

        private static bool OnDoTransition(On.HutongGames.PlayMaker.Fsm.orig_DoTransition orig, Fsm self, FsmTransition transition, bool isGlobal)
        {
            if (_enabled)
            {
                string fromState = self?.ActiveStateName ?? "";
                string toState = transition?.ToState ?? transition?.ToFsmState?.Name ?? "";
                string eventName = transition?.EventName ?? transition?.FsmEvent?.Name ?? "";
                if (string.IsNullOrEmpty(eventName))
                {
                    eventName = "__unknown_event__";
                }

                TryRecord(self, fromState, toState, eventName);
            }

            _transitionHookDepth++;
            try
            {
                return orig(self, transition, isGlobal);
            }
            finally
            {
                _transitionHookDepth--;
            }
        }

        private static void OnSetState(On.HutongGames.PlayMaker.Fsm.orig_SetState orig, Fsm self, string stateName)
        {
            if (_enabled && _transitionHookDepth == 0)
            {
                string fromState = self?.ActiveStateName ?? "";
                TryRecord(self, fromState, stateName ?? "", "__direct_set_state__");
            }

            orig(self, stateName);
        }

        private static void TryRecord(Fsm? fsm, string fromState, string toState, string eventName)
        {
            string sceneName = GameManager.instance != null ? GameManager.instance.sceneName : "";
            string gameObjectName = fsm?.GameObjectName ?? fsm?.GameObject?.name ?? "";
            string fsmName = fsm?.Name ?? "";

            if (!MatchesScene(sceneName) ||
                !MatchesPattern(_gameObjectFilter, gameObjectName) ||
                !MatchesPattern(_fsmFilter, fsmName) ||
                !MatchesPattern(_eventFilter, eventName))
            {
                return;
            }

            int maxRows = _config.MaxRows < 1 ? 1 : _config.MaxRows;
            if (_rows.Count >= maxRows)
            {
                _droppedRows++;
                return;
            }

            float currentTime = Time.time;
            float fixedTime = Time.fixedTime;
            float fixedDelta = Time.fixedDeltaTime;
            int fixedFrameProxy = fixedDelta > 0f ? Mathf.RoundToInt(fixedTime / fixedDelta) : -1;

            PlayMakerTraceRecord row = new()
            {
                SessionId = _sessionId,
                SceneName = sceneName,
                GameObject = gameObjectName,
                FsmName = fsmName,
                FromState = fromState,
                ToState = toState,
                EventName = eventName,
                FrameCount = Time.frameCount,
                FixedFrameCount = fixedFrameProxy,
                Time = currentTime,
                FixedTime = fixedTime,
                TimeMinusFixedTime = currentTime - fixedTime,
                UnscaledTime = Time.unscaledTime
            };

            if (_config.Snapshots.IncludeHeroSnapshot)
            {
                PopulateHeroSnapshot(row);
            }

            if (_config.Snapshots.IncludeFsmVars && fsm != null)
            {
                row.FsmVars = BuildFsmVarSnapshot(fsm);
            }

            _rows.Add(row);
        }

        private static void PopulateHeroSnapshot(PlayMakerTraceRecord row)
        {
            HeroController? hero = HeroController.instance;
            if (hero == null)
            {
                return;
            }

            row.HeroState = hero.hero_state.ToString();
            row.HeroFlags = new PlayMakerTraceHeroFlags
            {
                ControlReqlinquished = hero.controlReqlinquished,
                TransitionState = hero.transitionState.ToString(),
                CStateTransitioning = hero.cState.transitioning,
                CStateWillHardLand = hero.cState.willHardLand,
                CStateDead = hero.cState.dead
            };
        }

        private static PlayMakerTraceFsmVars? BuildFsmVarSnapshot(Fsm fsm)
        {
            FsmVariables variables = fsm.Variables;
            if (variables == null)
            {
                return null;
            }

            Dictionary<string, bool>? bools = ReadBoolVars(variables, _config.Snapshots.FsmBoolAllowlist);
            Dictionary<string, int>? ints = ReadIntVars(variables, _config.Snapshots.FsmIntAllowlist);
            Dictionary<string, float>? floats = ReadFloatVars(variables, _config.Snapshots.FsmFloatAllowlist);

            if (bools == null && ints == null && floats == null)
            {
                return null;
            }

            return new PlayMakerTraceFsmVars
            {
                Bools = bools,
                Ints = ints,
                Floats = floats
            };
        }

        private static Dictionary<string, bool>? ReadBoolVars(FsmVariables variables, List<string> allowlist)
        {
            Dictionary<string, bool> output = new(StringComparer.Ordinal);
            foreach (string variableName in allowlist)
            {
                if (string.IsNullOrWhiteSpace(variableName))
                {
                    continue;
                }

                FsmBool? value = variables.GetFsmBool(variableName);
                if (value != null)
                {
                    output[variableName] = value.Value;
                }
            }

            return output.Count > 0 ? output : null;
        }

        private static Dictionary<string, int>? ReadIntVars(FsmVariables variables, List<string> allowlist)
        {
            Dictionary<string, int> output = new(StringComparer.Ordinal);
            foreach (string variableName in allowlist)
            {
                if (string.IsNullOrWhiteSpace(variableName))
                {
                    continue;
                }

                FsmInt? value = variables.GetFsmInt(variableName);
                if (value != null)
                {
                    output[variableName] = value.Value;
                }
            }

            return output.Count > 0 ? output : null;
        }

        private static Dictionary<string, float>? ReadFloatVars(FsmVariables variables, List<string> allowlist)
        {
            Dictionary<string, float> output = new(StringComparer.Ordinal);
            foreach (string variableName in allowlist)
            {
                if (string.IsNullOrWhiteSpace(variableName))
                {
                    continue;
                }

                FsmFloat? value = variables.GetFsmFloat(variableName);
                if (value != null)
                {
                    output[variableName] = value.Value;
                }
            }

            return output.Count > 0 ? output : null;
        }

        private static bool MatchesScene(string sceneName)
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

        private static void LoadConfig()
        {
            EnsureConfigDirectoryExists();

            try
            {
                if (!File.Exists(_configPath))
                {
                    _config = PlayMakerTraceConfig.CreateDefault();
                    SaveConfig();
                }
                else
                {
                    string json = File.ReadAllText(_configPath);
                    PlayMakerTraceConfig? parsed = JsonConvert.DeserializeObject<PlayMakerTraceConfig>(json);
                    _config = parsed ?? PlayMakerTraceConfig.CreateDefault();
                }
            }
            catch (Exception e)
            {
                DebugMod.instance.LogError("PM Trace config load failed, reverting to defaults: " + e);
                Console.AddLine("PM Trace config load failed, using defaults.");
                _config = PlayMakerTraceConfig.CreateDefault();
            }

            NormalizeConfig();
            _enabled = _config.Enabled;
            CompileFilters();
        }

        private static void SaveConfig()
        {
            EnsureConfigDirectoryExists();

            try
            {
                string json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception e)
            {
                DebugMod.instance.LogError("PM Trace config save failed: " + e);
                Console.AddLine("PM Trace config save failed. Check ModLog for details.");
            }
        }

        private static void CompileFilters()
        {
            _sceneAllowlist.Clear();
            foreach (string scene in _config.Filters.SceneAllowlist.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                _sceneAllowlist.Add(scene.Trim());
            }

            _gameObjectFilter = CompilePatternFilter(_config.Filters.GameObjectFilter, "gameObjectFilter");
            _fsmFilter = CompilePatternFilter(_config.Filters.FsmFilter, "fsmFilter");
            _eventFilter = CompilePatternFilter(_config.Filters.EventFilter, "eventFilter");
        }

        private static CompiledFilter CompilePatternFilter(PlayMakerTracePatternFilter source, string label)
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
                DebugMod.instance.LogError($"PM Trace regex compile failed for {label}: " + e.Message);
                Console.AddLine($"PM Trace invalid regex for {label}, disabling this filter.");
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

        private static void EnsureConfigDirectoryExists()
        {
            string baseDir = DebugMod.settings.ModBaseDirectory;
            if (!Directory.Exists(baseDir))
            {
                Directory.CreateDirectory(baseDir);
            }
        }

        private static string ResolveOutputDirectory()
        {
            string overridePath = _config.Output.OutputDirOverride;
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return overridePath;
            }

            return Path.Combine(DebugMod.settings.ModBaseDirectory, "pmtrace");
        }

        private static string SanitizeFileNamePart(string input)
        {
            StringBuilder output = new(input ?? "");
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                output.Replace(c, '_');
            }

            return output.ToString().Trim();
        }

        private static void NormalizeConfig()
        {
            _config.Filters ??= new PlayMakerTraceFilterConfig();
            _config.Snapshots ??= new PlayMakerTraceSnapshotConfig();
            _config.Output ??= new PlayMakerTraceOutputConfig();

            _config.Filters.SceneAllowlist ??= new List<string>();
            _config.Filters.GameObjectFilter ??= new PlayMakerTracePatternFilter();
            _config.Filters.FsmFilter ??= new PlayMakerTracePatternFilter();
            _config.Filters.EventFilter ??= new PlayMakerTracePatternFilter();

            _config.Snapshots.FsmBoolAllowlist ??= new List<string>();
            _config.Snapshots.FsmIntAllowlist ??= new List<string>();
            _config.Snapshots.FsmFloatAllowlist ??= new List<string>();
        }
    }
}
