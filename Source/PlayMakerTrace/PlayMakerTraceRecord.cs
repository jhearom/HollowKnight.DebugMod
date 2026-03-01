using System.Collections.Generic;
using Newtonsoft.Json;

namespace DebugMod.PlayMakerTrace
{
    internal sealed class PlayMakerTraceRecord
    {
        [JsonProperty("session_id")]
        public string SessionId { get; set; } = "";

        [JsonProperty("scene_name")]
        public string SceneName { get; set; } = "";

        [JsonProperty("game_object")]
        public string GameObject { get; set; } = "";

        [JsonProperty("fsm_name")]
        public string FsmName { get; set; } = "";

        [JsonProperty("from_state")]
        public string FromState { get; set; } = "";

        [JsonProperty("to_state")]
        public string ToState { get; set; } = "";

        [JsonProperty("event_name")]
        public string EventName { get; set; } = "";

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

        [JsonProperty("unscaled_time")]
        public float UnscaledTime { get; set; }

        [JsonProperty("fixed_frame_source")]
        public string FixedFrameSource { get; set; } = "proxy_fixedTime_over_fixedDeltaTime";

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
