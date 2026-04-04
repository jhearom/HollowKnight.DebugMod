using System;

namespace DebugMod
{
    public static partial class BindableFunctions
    {
        [BindableMethod(name = "THK DSTAB/PUPPET Repro Assist", category = "Bosses")]
        public static void ToggleThkDstabPuppetReproAssist()
        {
            DebugMod.settings.ThkDstabPuppetReproAssistEnabled = !DebugMod.settings.ThkDstabPuppetReproAssistEnabled;
            RoomSpecific.SyncThkDstabPuppetSettings();
            RoomSpecific.PrintThkDstabPuppetStatus();
        }

        [BindableMethod(name = "THK DSTAB/PUPPET Trace", category = "Bosses")]
        public static void ToggleThkDstabPuppetTrace()
        {
            DebugMod.settings.ThkDstabPuppetTraceEnabled = !DebugMod.settings.ThkDstabPuppetTraceEnabled;
            RoomSpecific.SyncThkDstabPuppetSettings();
            RoomSpecific.PrintThkDstabPuppetStatus();
        }
    }
}
