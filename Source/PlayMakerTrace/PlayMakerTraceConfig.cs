using System.Collections.Generic;
using Newtonsoft.Json;

namespace DebugMod.PlayMakerTrace
{
    internal sealed class PlayMakerTraceConfig
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = false;

        [JsonProperty("maxRows")]
        public int MaxRows { get; set; } = 50000;

        [JsonProperty("filters")]
        public PlayMakerTraceFilterConfig Filters { get; set; } = new();

        [JsonProperty("snapshots")]
        public PlayMakerTraceSnapshotConfig Snapshots { get; set; } = new();

        [JsonProperty("output")]
        public PlayMakerTraceOutputConfig Output { get; set; } = new();

        public static PlayMakerTraceConfig CreateDefault() => new();
    }

    internal sealed class PlayMakerTraceFilterConfig
    {
        [JsonProperty("sceneAllowlist")]
        public List<string> SceneAllowlist { get; set; } = new();

        [JsonProperty("gameObjectFilter")]
        public PlayMakerTracePatternFilter GameObjectFilter { get; set; } = new();

        [JsonProperty("fsmFilter")]
        public PlayMakerTracePatternFilter FsmFilter { get; set; } = new();

        [JsonProperty("eventFilter")]
        public PlayMakerTracePatternFilter EventFilter { get; set; } = new();
    }

    internal sealed class PlayMakerTracePatternFilter
    {
        [JsonProperty("mode")]
        public string Mode { get; set; } = "off";

        [JsonProperty("value")]
        public string Value { get; set; } = "";

        [JsonProperty("ignoreCase")]
        public bool IgnoreCase { get; set; } = true;
    }

    internal sealed class PlayMakerTraceSnapshotConfig
    {
        [JsonProperty("includeHeroSnapshot")]
        public bool IncludeHeroSnapshot { get; set; } = true;

        [JsonProperty("includeFsmVars")]
        public bool IncludeFsmVars { get; set; } = true;

        [JsonProperty("fsmBoolAllowlist")]
        public List<string> FsmBoolAllowlist { get; set; } = new();

        [JsonProperty("fsmIntAllowlist")]
        public List<string> FsmIntAllowlist { get; set; } = new();

        [JsonProperty("fsmFloatAllowlist")]
        public List<string> FsmFloatAllowlist { get; set; } = new();
    }

    internal sealed class PlayMakerTraceOutputConfig
    {
        [JsonProperty("outputDirOverride")]
        public string OutputDirOverride { get; set; } = "";

        [JsonProperty("filePrefix")]
        public string FilePrefix { get; set; } = "pmtrace";
    }
}
