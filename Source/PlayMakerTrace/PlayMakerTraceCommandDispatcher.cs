using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace DebugMod.PlayMakerTrace
{
    internal static class PlayMakerTraceCommandDispatcher
    {
        private static readonly Regex ProfileNamePattern = new("^[A-Za-z0-9_.-]{1,64}$", RegexOptions.CultureInvariant);

        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        internal static string Execute(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return Result(false, "empty_command", "Command line is empty.");
            }

            string[] tokens = commandLine
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return Result(false, "empty_command", "Command line is empty.");
            }

            int offset = string.Equals(tokens[0], "pmtrace", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            if (tokens.Length <= offset)
            {
                return Help();
            }

            string command = tokens[offset].ToLowerInvariant();
            try
            {
                if (command != "help" && command != "--help" && command != "-h")
                {
                    PlayMakerTraceManager.Initialize();
                }

                switch (command)
                {
                    case "help":
                    case "--help":
                    case "-h":
                        return Help();
                    case "enable":
                        PlayMakerTraceManager.Enable();
                        return Result(true, "enabled", "PM Trace enabled.");
                    case "disable":
                        PlayMakerTraceManager.Disable();
                        return Result(true, "disabled", "PM Trace disabled.");
                    case "clear":
                        PlayMakerTraceManager.ClearBuffer();
                        return Result(true, "cleared", "PM Trace buffer cleared.");
                    case "flush":
                        string flushPath = PlayMakerTraceManager.FlushToDisk();
                        return Result(true, "flushed", "PM Trace flush requested.", new
                        {
                            output_path = flushPath
                        });
                    case "reload":
                        PlayMakerTraceManager.ReloadConfig();
                        return Result(true, "reloaded", "PM Trace config reloaded.");
                    case "status":
                        return Result(true, "status", "PM Trace status.", new
                        {
                            lines = PlayMakerTraceManager.GetStatusLines(),
                            enabled = PlayMakerTraceManager.IsEnabled,
                            active_profile = PlayMakerTraceManager.ActiveProfileName,
                            row_count = PlayMakerTraceManager.RowCount,
                            dropped_rows = PlayMakerTraceManager.DroppedRowCount
                        });
                    case "dump_status":
                    case "dump-status":
                        string statusPath = PlayMakerTraceManager.DumpStatusSnapshot();
                        return Result(true, "status_dumped", "PM Trace status dumped.", new
                        {
                            output_path = statusPath
                        });
                    case "profiles":
                        return ExecuteProfiles(tokens, offset + 1);
                    case "profile":
                        return ExecuteProfile(tokens, offset + 1);
                    default:
                        return Result(false, "unknown_command", $"Unknown PM Trace command: '{command}'.");
                }
            }
            catch (Exception e)
            {
                DebugMod.instance.LogError("PM Trace command execution failed: " + e);
                return Result(false, "command_exception", "PM Trace command failed. Check ModLog for details.");
            }
        }

        internal static string Help()
        {
            return Result(true, "help", "PM Trace command help.", new
            {
                commands = new[]
                {
                    "pmtrace help",
                    "pmtrace enable",
                    "pmtrace disable",
                    "pmtrace clear",
                    "pmtrace flush",
                    "pmtrace reload",
                    "pmtrace status",
                    "pmtrace dump_status",
                    "pmtrace profiles list",
                    "pmtrace profile activate <name>"
                }
            });
        }

        private static string ExecuteProfiles(IReadOnlyList<string> tokens, int index)
        {
            if (index >= tokens.Count)
            {
                return Result(false, "missing_subcommand", "Expected: profiles list");
            }

            string subcommand = tokens[index].ToLowerInvariant();
            if (subcommand != "list")
            {
                return Result(false, "unknown_subcommand", $"Unknown profiles subcommand: '{subcommand}'.");
            }

            IReadOnlyList<string> profiles = PlayMakerTraceManager.ListProfiles();
            return Result(true, "profiles_list", "PM Trace profiles listed.", new
            {
                active_profile = PlayMakerTraceManager.ActiveProfileName,
                profiles
            });
        }

        private static string ExecuteProfile(IReadOnlyList<string> tokens, int index)
        {
            if (index >= tokens.Count)
            {
                return Result(false, "missing_subcommand", "Expected: profile activate <name>");
            }

            string subcommand = tokens[index].ToLowerInvariant();
            switch (subcommand)
            {
                case "activate":
                    if (index + 1 >= tokens.Count)
                    {
                        return Result(false, "missing_argument", "Expected profile name: profile activate <name>");
                    }

                    string profileName = tokens[index + 1];
                    if (!ProfileNamePattern.IsMatch(profileName))
                    {
                        return Result(false, "invalid_profile_name", "Profile name must match [A-Za-z0-9_.-]{1,64}.");
                    }

                    bool activated = PlayMakerTraceManager.ActivateProfile(profileName);
                    return activated
                        ? Result(true, "profile_activated", $"Activated profile '{PlayMakerTraceManager.ActiveProfileName}'.", new
                        {
                            active_profile = PlayMakerTraceManager.ActiveProfileName
                        })
                        : Result(false, "profile_not_found", $"Profile not found: '{profileName}'.");
                default:
                    return Result(false, "unknown_subcommand", $"Unknown profile subcommand: '{subcommand}'.");
            }
        }

        private static string Result(bool ok, string code, string message, object? data = null)
        {
            return JsonConvert.SerializeObject(new
            {
                ok,
                code,
                message,
                data
            }, _jsonSettings);
        }
    }
}
