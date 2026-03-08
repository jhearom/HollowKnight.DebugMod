using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DebugMod.PlayMakerTrace
{
    internal static class PlayMakerTraceManager
    {
        private const string CommandScriptFileName = "pmtrace_commands.txt";
        private const int CommandScriptMaxBytes = 64 * 1024;
        private const int CommandScriptMaxCommands = 128;
        private const int CommandScriptMaxLineLength = 512;
        private static readonly string[] ForbiddenScriptTokens = { ";", "&&", "||", "|", "`", "$(" };

        private static bool _initialized;
        private static bool _runningCommandScript;
        private static string _configPath = "";
        private static PlayMakerTraceConfig _config = PlayMakerTraceConfig.CreateDefault();
        private static PlayMakerTraceProfile? _activeProfile;
        private static string _activeProfileName = "default_fsm";

        private static PlayMakerTraceRuntime? _runtime;
        private static readonly List<IPlayMakerTraceProbe> _probes = new();

        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        internal static bool IsEnabled => _runtime != null && _runtime.Enabled;
        internal static int RowCount => _runtime?.Rows.Count ?? 0;
        internal static int DroppedRowCount => _runtime?.DroppedRows ?? 0;
        internal static string ActiveProfileName => _activeProfileName;

        internal static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _configPath = Path.Combine(DebugMod.settings.ModBaseDirectory, "pmtrace_config.json");
            _runtime = new PlayMakerTraceRuntime(Console.AddLine, message => DebugMod.instance.LogError(message));
            _runtime.ResetSession();

            LoadConfig();
            EnsureWindowsTemplateExists();
            AttachProbes();

            _initialized = true;
            Console.AddLine("PM Trace initialized (v2 profile runtime, default off)");
        }

        internal static void Shutdown()
        {
            if (!_initialized)
            {
                return;
            }

            DetachProbes();
            if (_runtime != null)
            {
                _runtime.SetEnabled(false);
            }

            _initialized = false;
        }

        internal static void Enable()
        {
            if (!_initialized)
            {
                Initialize();
            }

            _runtime?.SetEnabled(true);
            Console.AddLine("PM Trace enabled");
        }

        internal static void Disable()
        {
            AutoFlushIfNeeded("disable");
            _runtime?.SetEnabled(false);
            Console.AddLine("PM Trace disabled");
        }

        internal static void ClearBuffer()
        {
            _runtime?.ClearBuffer();
            Console.AddLine("PM Trace buffer cleared");
        }

        internal static void ReloadConfig()
        {
            LoadConfig();
            Console.AddLine("PM Trace config reloaded");
        }

        internal static IReadOnlyList<string> ListProfiles()
        {
            return _config.Profiles.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static void PrintProfiles()
        {
            if (!_initialized)
            {
                Initialize();
            }

            IReadOnlyList<string> profiles = ListProfiles();
            string profileLine = profiles.Count > 0
                ? string.Join(", ", profiles)
                : "(none)";
            Console.AddLine($"PM Trace profiles ({profiles.Count}): {profileLine}");
            Console.AddLine($"PM Trace active profile: {_activeProfileName}");
        }

        internal static bool ActivateNextProfile()
        {
            if (!_initialized)
            {
                Initialize();
            }

            IReadOnlyList<string> profiles = ListProfiles();
            if (profiles.Count == 0)
            {
                Console.AddLine("PM Trace no profiles available.");
                return false;
            }

            int idx = 0;
            for (int i = 0; i < profiles.Count; i++)
            {
                if (string.Equals(profiles[i], _activeProfileName, StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }

            string nextProfile = profiles[(idx + 1) % profiles.Count];
            return ActivateProfile(nextProfile);
        }

        internal static bool ActivateProfile(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName) || !_config.Profiles.ContainsKey(profileName))
            {
                Console.AddLine($"PM Trace profile not found: '{profileName}'");
                return false;
            }

            _config.ActiveProfile = profileName;
            SaveConfig();
            ApplyActiveProfile();
            Console.AddLine($"PM Trace active profile: {_activeProfileName}");
            return true;
        }

        internal static string FlushToDisk()
        {
            if (!_initialized)
            {
                Initialize();
            }

            if (_runtime == null)
            {
                return "";
            }

            string outputDir = ResolveOutputDirectory();
            Directory.CreateDirectory(outputDir);

            string prefix = SanitizeFileNamePart(_config.Output.FilePrefix);
            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = "pmtrace";
            }

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string fileName = $"{prefix}_{stamp}_{_runtime.SessionId.Substring(0, 8)}.jsonl";
            string filePath = Path.Combine(outputDir, fileName);

            try
            {
                using StreamWriter writer = new(filePath, false, new UTF8Encoding(false));
                foreach (PlayMakerTraceEventRecord row in _runtime.Rows)
                {
                    writer.WriteLine(JsonConvert.SerializeObject(row, _jsonSettings));
                }

                _runtime.MarkFlushed();
                Console.AddLine($"PM Trace flush complete: {_runtime.Rows.Count} rows -> {NormalizePathForStatus(filePath)}");
            }
            catch (Exception e)
            {
                DebugMod.instance.LogError("PM Trace flush failed: " + e);
                Console.AddLine("PM Trace flush failed. Check ModLog for details.");
                return "";
            }

            return filePath;
        }

        internal static void AutoFlushOnApplicationQuit()
        {
            AutoFlushIfNeeded("application quit");
        }

        internal static void PrintStatus()
        {
            foreach (string line in GetStatusLines())
            {
                Console.AddLine(line);
            }
        }

        internal static string DumpStatusSnapshot()
        {
            if (!_initialized)
            {
                Initialize();
            }

            if (_runtime == null)
            {
                return "";
            }

            string outputDir = ResolveOutputDirectory();
            Directory.CreateDirectory(outputDir);

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string fileName = $"pmtrace_status_{stamp}_{_runtime.SessionId.Substring(0, 8)}.json";
            string filePath = Path.Combine(outputDir, fileName);

            object payload = new
            {
                session_id = _runtime.SessionId,
                utc_timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                enabled = _runtime.Enabled,
                active_profile = _activeProfileName,
                profile_names = ListProfiles(),
                enabled_probes = _runtime.EnabledProbes,
                buffered_rows = _runtime.Rows.Count,
                dropped_rows = _runtime.DroppedRows,
                max_rows = _runtime.MaxRows,
                config_path = NormalizePathForStatus(_configPath),
                output_dir = NormalizePathForStatus(outputDir),
                command_script_path = NormalizePathForStatus(GetCommandScriptPath()),
                command_script_auto_run_on_reload = _config.CommandScript.AutoRunOnReload,
                filters = _runtime.DescribeFilterSummary()
            };

            try
            {
                File.WriteAllText(filePath, JsonConvert.SerializeObject(payload, Formatting.Indented), new UTF8Encoding(false));
                Console.AddLine($"PM Trace status snapshot: {NormalizePathForStatus(filePath)}");
                return filePath;
            }
            catch (Exception e)
            {
                DebugMod.instance.LogError("PM Trace status snapshot failed: " + e);
                Console.AddLine("PM Trace status snapshot failed. Check ModLog for details.");
                return "";
            }
        }

        internal static string RunCommandScript()
        {
            if (!_initialized)
            {
                Initialize();
            }

            if (_runningCommandScript)
            {
                Console.AddLine("PM Trace command script already running; skipping nested invocation.");
                return "";
            }

            string scriptPath = GetCommandScriptPath();
            if (!IsPathWithinConfigBase(scriptPath))
            {
                Console.AddLine("PM Trace command script path rejected (outside DebugModData).");
                return "";
            }

            if (!File.Exists(scriptPath))
            {
                Console.AddLine($"PM Trace command script not found: {NormalizePathForStatus(scriptPath)}");
                return "";
            }

            FileInfo info = new(scriptPath);
            if (info.Length > CommandScriptMaxBytes)
            {
                Console.AddLine($"PM Trace command script rejected: file too large ({info.Length} bytes, max {CommandScriptMaxBytes}).");
                return "";
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(scriptPath);
            }
            catch (Exception e)
            {
                DebugMod.instance.LogError("PM Trace command script read failed: " + e);
                Console.AddLine("PM Trace command script read failed. Check ModLog for details.");
                return "";
            }

            int executed = 0;
            _runningCommandScript = true;
            try
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = (lines[i] ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (trimmed.Length > CommandScriptMaxLineLength)
                    {
                        Console.AddLine($"PM Trace command script rejected at line {i + 1}: line length exceeds {CommandScriptMaxLineLength}.");
                        return "";
                    }

                    if (ContainsForbiddenScriptToken(trimmed, out string token))
                    {
                        Console.AddLine($"PM Trace command script rejected at line {i + 1}: forbidden token '{token}'.");
                        return "";
                    }

                    if (executed >= CommandScriptMaxCommands)
                    {
                        Console.AddLine($"PM Trace command script rejected: command count exceeds {CommandScriptMaxCommands}.");
                        return "";
                    }

                    string result = PlayMakerTraceCommandDispatcher.Execute(trimmed);
                    if (!TryReadCommandResult(result, out bool ok, out string code, out string message))
                    {
                        Console.AddLine($"PM Trace command script failed at line {i + 1}: unparseable command result.");
                        return "";
                    }

                    if (!ok)
                    {
                        Console.AddLine($"PM Trace command script aborted at line {i + 1}: {code} - {message}");
                        return "";
                    }

                    executed++;
                }
            }
            finally
            {
                _runningCommandScript = false;
            }

            Console.AddLine($"PM Trace command script complete: executed {executed} command(s) from {NormalizePathForStatus(scriptPath)}");
            return scriptPath;
        }

        internal static List<string> GetStatusLines()
        {
            if (_runtime == null)
            {
                return new List<string> { "PM Trace status: runtime unavailable" };
            }

            string probes = _runtime.EnabledProbes.Count > 0
                ? string.Join(", ", _runtime.EnabledProbes)
                : "(none)";

            return new List<string>
            {
                $"PM Trace status: enabled={_runtime.Enabled}, profile={_activeProfileName}, probes={probes}",
                $"PM Trace buffer: rows={_runtime.Rows.Count}, dropped={_runtime.DroppedRows}, max={_runtime.MaxRows}",
                $"PM Trace config: {NormalizePathForStatus(_configPath)}",
                $"PM Trace output dir: {NormalizePathForStatus(ResolveOutputDirectory())}",
                $"PM Trace command script: {NormalizePathForStatus(GetCommandScriptPath())} (autoRunOnReload={_config.CommandScript.AutoRunOnReload})",
                $"PM Trace filters: {_runtime.DescribeFilterSummary()}"
            };
        }

        private static void AttachProbes()
        {
            DetachProbes();

            if (_runtime == null)
            {
                return;
            }

            _probes.Add(new PlayMakerFsmTransitionProbe(_runtime, () => _activeProfile));
            _probes.Add(new PlayMakerDamagePipelineProbe(_runtime, () => _activeProfile));
            _probes.Add(new PlayMakerColliderContactProbe(_runtime, () => _activeProfile));
            _probes.Add(new PlayMakerComponentToggleProbe(_runtime, () => _activeProfile));
            _probes.Add(new PlayMakerFieldWatchProbe(_runtime, () => _activeProfile));
            foreach (IPlayMakerTraceProbe probe in _probes)
            {
                probe.Attach();
            }
        }

        private static void DetachProbes()
        {
            foreach (IPlayMakerTraceProbe probe in _probes)
            {
                probe.Detach();
            }

            _probes.Clear();
        }

        private static void AutoFlushIfNeeded(string reason)
        {
            if (_runtime == null || !_runtime.HasUnflushedRows || _runtime.Rows.Count == 0)
            {
                return;
            }

            string filePath = FlushToDisk();
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                Console.AddLine($"PM Trace auto-flush ({reason}) -> {filePath}");
            }
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
                    if (parsed == null || parsed.SchemaVersion != 2)
                    {
                        Console.AddLine("PM Trace config schema mismatch; replacing with v2 defaults.");
                        _config = PlayMakerTraceConfig.CreateDefault();
                        SaveConfig();
                    }
                    else
                    {
                        _config = parsed;
                    }
                }
            }
            catch (Exception e)
            {
                DebugMod.instance.LogError("PM Trace config load failed, reverting to v2 defaults: " + e);
                Console.AddLine("PM Trace config load failed, using v2 defaults.");
                _config = PlayMakerTraceConfig.CreateDefault();
                SaveConfig();
            }

            bool dirty = NormalizeConfig();
            if (dirty)
            {
                SaveConfig();
            }

            ApplyActiveProfile();
            _runtime?.SetEnabled(false);

            if (_config.CommandScript.AutoRunOnReload && !_runningCommandScript)
            {
                RunCommandScript();
            }
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

        private static void ApplyActiveProfile()
        {
            if (_runtime == null)
            {
                return;
            }

            if (!_config.Profiles.TryGetValue(_config.ActiveProfile, out PlayMakerTraceProfile? profile))
            {
                _activeProfileName = "default_fsm";
                _activeProfile = _config.Profiles[_activeProfileName];
            }
            else
            {
                _activeProfileName = _config.ActiveProfile;
                _activeProfile = profile;
            }

            _runtime.Configure(_config, _activeProfile);
        }

        private static bool NormalizeConfig()
        {
            bool dirty = false;

            if (_config.SchemaVersion != 2)
            {
                _config.SchemaVersion = 2;
                dirty = true;
            }

            if (_config.MaxRows < 1)
            {
                _config.MaxRows = 1;
                dirty = true;
            }

            if (_config.Output == null)
            {
                _config.Output = new PlayMakerTraceOutputConfig();
                dirty = true;
            }

            if (_config.CommandScript == null)
            {
                _config.CommandScript = new PlayMakerTraceCommandScriptConfig();
                dirty = true;
            }

            if (_config.Profiles == null)
            {
                _config.Profiles = new Dictionary<string, PlayMakerTraceProfile>();
                dirty = true;
            }

            dirty |= EnsureBuiltInProfile("default_fsm", PlayMakerTraceProfile.CreateDefaultFsm());
            dirty |= EnsureBuiltInProfile("combat_minimal", PlayMakerTraceProfile.CreateCombatMinimal());
            dirty |= EnsureBuiltInProfile("shriek_hitgate", PlayMakerTraceProfile.CreateShriekHitgate());

            List<string> keys = _config.Profiles.Keys.ToList();
            foreach (string key in keys)
            {
                if (NormalizeProfile(_config.Profiles[key]))
                {
                    dirty = true;
                }
            }

            if (string.IsNullOrWhiteSpace(_config.ActiveProfile) || !_config.Profiles.ContainsKey(_config.ActiveProfile))
            {
                _config.ActiveProfile = "default_fsm";
                dirty = true;
            }

            return dirty;
        }

        private static bool EnsureBuiltInProfile(string name, PlayMakerTraceProfile profile)
        {
            if (_config.Profiles.ContainsKey(name))
            {
                return false;
            }

            _config.Profiles[name] = profile;
            return true;
        }

        private static bool NormalizeProfile(PlayMakerTraceProfile profile)
        {
            bool dirty = false;

            profile.EnabledProbes ??= new List<string>();
            profile.Filters ??= new PlayMakerTraceFilterConfig();
            profile.FsmTransition ??= new PlayMakerTraceFsmTransitionConfig();
            profile.DamagePipeline ??= new PlayMakerTraceDamagePipelineConfig();
            profile.ColliderContact ??= new PlayMakerTraceColliderContactConfig();
            profile.ComponentToggle ??= new PlayMakerTraceComponentToggleConfig();
            profile.FieldWatches ??= new List<PlayMakerTraceFieldWatchConfig>();
            profile.FieldWatchProbe ??= new PlayMakerTraceFieldWatchProbeConfig();

            profile.Filters.SceneAllowlist ??= new List<string>();
            profile.Filters.GameObjectFilter ??= new PlayMakerTracePatternFilter();
            profile.Filters.FsmFilter ??= new PlayMakerTracePatternFilter();
            profile.Filters.EventFilter ??= new PlayMakerTracePatternFilter();
            profile.Filters.ProbeFilter ??= new PlayMakerTracePatternFilter();
            profile.Filters.ComponentTypeFilter ??= new PlayMakerTracePatternFilter();

            profile.FsmTransition.FsmBoolAllowlist ??= new List<string>();
            profile.FsmTransition.FsmIntAllowlist ??= new List<string>();
            profile.FsmTransition.FsmFloatAllowlist ??= new List<string>();
            profile.ColliderContact.SourceObjectFilter ??= new PlayMakerTracePatternFilter();
            profile.ColliderContact.TargetObjectFilter ??= new PlayMakerTracePatternFilter();
            profile.ComponentToggle.ComponentTypeAllowlist ??= new List<string>();

            if (profile.EnabledProbes.Count == 0)
            {
                profile.EnabledProbes.Add("fsm_transition");
                dirty = true;
            }

            profile.EnabledProbes = profile.EnabledProbes
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            profile.ComponentToggle.ComponentTypeAllowlist = profile.ComponentToggle.ComponentTypeAllowlist
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (profile.ComponentToggle.MaxComponentsPerSample < 1)
            {
                profile.ComponentToggle.MaxComponentsPerSample = 1;
                dirty = true;
            }

            if (profile.ComponentToggle.PollIntervalSeconds < 0f)
            {
                profile.ComponentToggle.PollIntervalSeconds = 0f;
                dirty = true;
            }

            if (profile.FieldWatchProbe.MaxObjectsPerSample < 1)
            {
                profile.FieldWatchProbe.MaxObjectsPerSample = 1;
                dirty = true;
            }

            if (profile.FieldWatchProbe.MaxCollectionPreviewItems < 1)
            {
                profile.FieldWatchProbe.MaxCollectionPreviewItems = 1;
                dirty = true;
            }

            if (profile.FieldWatchProbe.CadenceSeconds < 0f)
            {
                profile.FieldWatchProbe.CadenceSeconds = 0f;
                dirty = true;
            }

            profile.FieldWatches = profile.FieldWatches
                .Where(watch => watch != null)
                .Where(watch => !string.IsNullOrWhiteSpace(watch.TypeName) && !string.IsNullOrWhiteSpace(watch.FieldName))
                .Select(watch => new PlayMakerTraceFieldWatchConfig
                {
                    TypeName = watch.TypeName.Trim(),
                    FieldName = watch.FieldName.Trim()
                })
                .ToList();

            return dirty;
        }

        private static void EnsureConfigDirectoryExists()
        {
            string baseDir = DebugMod.settings.ModBaseDirectory;
            if (!Directory.Exists(baseDir))
            {
                Directory.CreateDirectory(baseDir);
            }
        }

        private static void EnsureWindowsTemplateExists()
        {
            string templatePath = Path.Combine(DebugMod.settings.ModBaseDirectory, "pmtrace_config.template.windows.json");
            if (File.Exists(templatePath))
            {
                return;
            }

            PlayMakerTraceConfig template = PlayMakerTraceConfig.CreateDefault();
            template.MaxRows = 20000;
            template.ActiveProfile = "shriek_hitgate";
            template.Profiles["shriek_hitgate"].Filters.SceneAllowlist = new List<string> { "level250" };
            template.Profiles["shriek_hitgate"].Filters.GameObjectFilter = new PlayMakerTracePatternFilter
            {
                Mode = "contains",
                Value = "Zombie Miner 1 (3)",
                IgnoreCase = true
            };

            try
            {
                File.WriteAllText(templatePath, JsonConvert.SerializeObject(template, Formatting.Indented), new UTF8Encoding(false));
                Console.AddLine("PM Trace wrote default Windows template config (v2)");
            }
            catch (Exception e)
            {
                DebugMod.instance.LogError("PM Trace template config write failed: " + e);
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

        private static string GetCommandScriptPath()
        {
            string baseDir = DebugMod.settings.ModBaseDirectory;
            string combined = Path.Combine(baseDir, CommandScriptFileName);
            return Path.GetFullPath(combined);
        }

        private static bool IsPathWithinConfigBase(string fullPath)
        {
            string baseDir = Path.GetFullPath(DebugMod.settings.ModBaseDirectory);
            string baseWithSeparator = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            return fullPath.StartsWith(baseWithSeparator, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsForbiddenScriptToken(string line, out string token)
        {
            foreach (string forbidden in ForbiddenScriptTokens)
            {
                if (line.IndexOf(forbidden, StringComparison.Ordinal) >= 0)
                {
                    token = forbidden;
                    return true;
                }
            }

            token = "";
            return false;
        }

        private static bool TryReadCommandResult(string json, out bool ok, out string code, out string message)
        {
            ok = false;
            code = "invalid_result";
            message = "Unable to parse command result.";

            try
            {
                JObject parsed = JObject.Parse(json);
                JToken? okToken = parsed["ok"];
                JToken? codeToken = parsed["code"];
                JToken? messageToken = parsed["message"];
                if (okToken == null || codeToken == null || messageToken == null)
                {
                    return false;
                }

                ok = okToken.Type == JTokenType.Boolean && okToken.Value<bool>();
                code = codeToken.Type == JTokenType.String ? codeToken.Value<string>() ?? "" : "";
                message = messageToken.Type == JTokenType.String ? messageToken.Value<string>() ?? "" : "";
                return true;
            }
            catch
            {
                return false;
            }
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

        private static string NormalizePathForStatus(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path ?? "";
            }

            char separator = Path.DirectorySeparatorChar;
            char altSeparator = Path.AltDirectorySeparatorChar;
            if (separator != altSeparator)
            {
                return path.Replace(altSeparator, separator);
            }

            return path;
        }
    }
}
