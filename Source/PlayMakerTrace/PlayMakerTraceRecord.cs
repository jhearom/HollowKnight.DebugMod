using System.Collections.Generic;
using Newtonsoft.Json;

namespace DebugMod.PlayMakerTrace
{
    internal sealed class PlayMakerTraceEventRecord
    {
        [JsonProperty("session_id")]
        public string SessionId { get; set; } = "";

        [JsonProperty("sequence_id")]
        public long SequenceId { get; set; }

        [JsonProperty("event_type")]
        public string EventType { get; set; } = "";

        [JsonProperty("scene_name")]
        public string SceneName { get; set; } = "";

        [JsonProperty("frame_count")]
        public int FrameCount { get; set; }

        [JsonProperty("fixed_frame_count")]
        public int FixedFrameCount { get; set; }

        [JsonProperty("time")]
        public float Time { get; set; }

        [JsonProperty("fixed_time")]
        public float FixedTime { get; set; }

        [JsonProperty("t_ft")]
        public float TimeMinusFixedTime { get; set; }

        [JsonProperty("delta_time")]
        public float DeltaTime { get; set; }

        [JsonProperty("fixed_delta_time")]
        public float FixedDeltaTime { get; set; }

        [JsonProperty("unscaled_time")]
        public float UnscaledTime { get; set; }

        [JsonProperty("realtime_since_startup")]
        public float RealtimeSinceStartup { get; set; }

        [JsonProperty("source_object", NullValueHandling = NullValueHandling.Ignore)]
        public string? SourceObject { get; set; }

        [JsonProperty("source_fsm", NullValueHandling = NullValueHandling.Ignore)]
        public string? SourceFsm { get; set; }

        [JsonProperty("source_instance_id", NullValueHandling = NullValueHandling.Ignore)]
        public int? SourceInstanceId { get; set; }

        [JsonProperty("target_object", NullValueHandling = NullValueHandling.Ignore)]
        public string? TargetObject { get; set; }

        [JsonProperty("target_instance_id", NullValueHandling = NullValueHandling.Ignore)]
        public int? TargetInstanceId { get; set; }

        [JsonProperty("event_name", NullValueHandling = NullValueHandling.Ignore)]
        public string? EventName { get; set; }

        [JsonProperty("from_state", NullValueHandling = NullValueHandling.Ignore)]
        public string? FromState { get; set; }

        [JsonProperty("to_state", NullValueHandling = NullValueHandling.Ignore)]
        public string? ToState { get; set; }

        [JsonProperty("component_type", NullValueHandling = NullValueHandling.Ignore)]
        public string? ComponentType { get; set; }

        [JsonProperty("payload", NullValueHandling = NullValueHandling.Ignore)]
        public object? Payload { get; set; }
    }

    internal sealed class PlayMakerTraceEventRequest
    {
        public string ProbeType { get; set; } = "";
        public string SceneName { get; set; } = "";
        public string? SourceObject { get; set; }
        public string? SourceFsm { get; set; }
        public int? SourceInstanceId { get; set; }
        public string? TargetObject { get; set; }
        public int? TargetInstanceId { get; set; }
        public string? EventName { get; set; }
        public string? FromState { get; set; }
        public string? ToState { get; set; }
        public string? ComponentType { get; set; }
        public object? Payload { get; set; }
    }

    internal sealed class PlayMakerTraceFsmPayload
    {
        [JsonProperty("hero_state", NullValueHandling = NullValueHandling.Ignore)]
        public string? HeroState { get; set; }

        [JsonProperty("hero_flags", NullValueHandling = NullValueHandling.Ignore)]
        public PlayMakerTraceHeroFlags? HeroFlags { get; set; }

        [JsonProperty("fsm_vars", NullValueHandling = NullValueHandling.Ignore)]
        public PlayMakerTraceFsmVars? FsmVars { get; set; }
    }

    internal sealed class PlayMakerTraceHeroFlags
    {
        [JsonProperty("controlReqlinquished")]
        public bool ControlReqlinquished { get; set; }

        [JsonProperty("transition_state")]
        public string TransitionState { get; set; } = "";

        [JsonProperty("cstate_transitioning")]
        public bool CStateTransitioning { get; set; }

        [JsonProperty("cstate_willHardLand")]
        public bool CStateWillHardLand { get; set; }

        [JsonProperty("cstate_dead")]
        public bool CStateDead { get; set; }
    }

    internal sealed class PlayMakerTraceFsmVars
    {
        [JsonProperty("bools", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, bool>? Bools { get; set; }

        [JsonProperty("ints", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, int>? Ints { get; set; }

        [JsonProperty("floats", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, float>? Floats { get; set; }
    }
}
