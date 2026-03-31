using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DebugMod
{
    internal sealed class UumuuTraceLogger
    {
        private readonly List<string> _entries = new List<string>();
        private string _logPath;

        internal bool Enabled { get; private set; }

        internal string LogPath => _logPath;

        internal bool HasEntries => _entries.Count > 0;

        internal void StartTrace(string sceneName)
        {
            _entries.Clear();
            _logPath = Path.Combine(Application.persistentDataPath, "uumuu_core_trace_1432.log");
            Enabled = true;
            WriteLine("Trace started in scene " + sceneName);
        }

        internal void StopTrace(string sceneName)
        {
            if (!Enabled)
            {
                return;
            }

            WriteLine("Trace stopped in scene " + sceneName);
            Enabled = false;
        }

        internal void Log(string message)
        {
            if (!Enabled)
            {
                return;
            }

            WriteLine(message);
        }

        internal void DumpTrace()
        {
            if (_entries.Count == 0)
            {
                Console.AddLine("No Uumuu trace data to dump");
                return;
            }

            try
            {
                File.WriteAllLines(_logPath, _entries.ToArray());
                Console.AddLine("Uumuu trace written to " + _logPath);
            }
            catch (Exception e)
            {
                DebugMod.instance.LogError("[UUMUU TRACE] Failed to write trace log: " + e);
                Console.AddLine("Unable to write Uumuu trace log");
            }
        }

        private void WriteLine(string message)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message;
            _entries.Add(line);

            try
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch (Exception e)
            {
                DebugMod.instance.LogError("[UUMUU TRACE] Failed to append trace log: " + e);
            }
        }
    }
}
