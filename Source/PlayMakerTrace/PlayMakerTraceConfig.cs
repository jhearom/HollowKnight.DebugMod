using System.Collections.Generic;
using Newtonsoft.Json;

namespace DebugMod.PlayMakerTrace
{
    internal sealed class PlayMakerTraceConfig
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; } = 2;

        [JsonProperty("maxRows")]
        public int MaxRows { get; set; } = 50000;

        [JsonProperty("activeProfile")]
        public string ActiveProfile { get; set; } = "default_fsm";

        [JsonProperty("profiles")]
        public Dictionary<string, PlayMakerTraceProfile> Profiles { get; set; } = new();

        [JsonProperty("output")]
        public PlayMakerTraceOutputConfig Output { get; set; } = new();

        [JsonProperty("commandScript")]
        public PlayMakerTraceCommandScriptConfig CommandScript { get; set; } = new();

        public static PlayMakerTraceConfig CreateDefault() => new()
        {
            Profiles = new Dictionary<string, PlayMakerTraceProfile>
            {
                { "default_fsm", PlayMakerTraceProfile.CreateDefaultFsm() },
                { "combat_minimal", PlayMakerTraceProfile.CreateCombatMinimal() },
                { "shriek_hitgate", PlayMakerTraceProfile.CreateShriekHitgate() }
            }
        };
    }

    internal sealed class PlayMakerTraceProfile
    {
        [JsonProperty("enabledProbes")]
        public List<string> EnabledProbes { get; set; } = new();

        [JsonProperty("filters")]
        public PlayMakerTraceFilterConfig Filters { get; set; } = new();

        [JsonProperty("fsmTransition")]
        public PlayMakerTraceFsmTransitionConfig FsmTransition { get; set; } = new();

        [JsonProperty("damagePipeline")]
        public PlayMakerTraceDamagePipelineConfig DamagePipeline { get; set; } = new();

        [JsonProperty("colliderContact")]
        public PlayMakerTraceColliderContactConfig ColliderContact { get; set; } = new();

        [JsonProperty("componentToggle")]
        public PlayMakerTraceComponentToggleConfig ComponentToggle { get; set; } = new();

        [JsonProperty("fieldWatches")]
        public List<PlayMakerTraceFieldWatchConfig> FieldWatches { get; set; } = new();

        [JsonProperty("fieldWatchProbe")]
        public PlayMakerTraceFieldWatchProbeConfig FieldWatchProbe { get; set; } = new();

        internal static PlayMakerTraceProfile CreateDefaultFsm() => new()
        {
            EnabledProbes = new List<string> { "fsm_transition" },
            Filters = new PlayMakerTraceFilterConfig(),
            FsmTransition = new PlayMakerTraceFsmTransitionConfig
            {
                IncludeHeroSnapshot = true,
                IncludeFsmVars = true
            }
        };

        internal static PlayMakerTraceProfile CreateCombatMinimal() => new()
        {
            EnabledProbes = new List<string> { "fsm_transition", "damage_pipeline" },
            Filters = new PlayMakerTraceFilterConfig(),
            FsmTransition = new PlayMakerTraceFsmTransitionConfig
            {
                IncludeHeroSnapshot = false,
                IncludeFsmVars = false
            },
            DamagePipeline = new PlayMakerTraceDamagePipelineConfig
            {
                IncludeTakeDamage = true,
                IncludeInvincible = false,
                IncludeNonFatalHit = false,
                IncludeLimitSendEvents = false
            }
        };

        internal static PlayMakerTraceProfile CreateShriekHitgate() => new()
        {
            EnabledProbes = new List<string> { "fsm_transition", "damage_pipeline", "collider_contact", "field_watch" },
            Filters = new PlayMakerTraceFilterConfig
            {
                ProbeFilter = new PlayMakerTracePatternFilter(),
                ComponentTypeFilter = new PlayMakerTracePatternFilter()
            },
            FsmTransition = new PlayMakerTraceFsmTransitionConfig
            {
                IncludeHeroSnapshot = true,
                IncludeFsmVars = true,
                FsmFloatAllowlist = new List<string> { "evasionByHitRemaining" }
            },
            DamagePipeline = new PlayMakerTraceDamagePipelineConfig
            {
                IncludeHitAttempt = true,
                IncludeGateChecks = true,
                IncludeTakeDamage = true,
                IncludeInvincible = true,
                IncludeNonFatalHit = true,
                IncludeLimitSendEvents = true
            },
            ColliderContact = new PlayMakerTraceColliderContactConfig
            {
                IncludeTriggerEnter = true,
                IncludeTriggerStay = true,
                IncludeTriggerExit = true,
                IncludeCollisionEvents = false
            },
            FieldWatches = new List<PlayMakerTraceFieldWatchConfig>
            {
                new()
                {
                    TypeName = "HealthManager",
                    FieldName = "evasionByHitRemaining"
                },
                new()
                {
                    TypeName = "HealthManager",
                    FieldName = "hp"
                },
                new()
                {
                    TypeName = "HealthManager",
                    FieldName = "isDead"
                },
                new()
                {
                    TypeName = "HealthManager",
                    FieldName = "invincible"
                },
                new()
                {
                    TypeName = "HealthManager",
                    FieldName = "invincibleFromDirection"
                },
                new()
                {
                    TypeName = "LimitSendEvents",
                    FieldName = "sentList"
                }
            },
            FieldWatchProbe = new PlayMakerTraceFieldWatchProbeConfig
            {
                EmitOnDamageEvents = true,
                EmitOnLimitSendEvents = true,
                CadenceSeconds = 0f,
                MaxObjectsPerSample = 32,
                SerializeCollectionsAsCount = true,
                MaxCollectionPreviewItems = 5
            }
        };
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

        [JsonProperty("probeFilter")]
        public PlayMakerTracePatternFilter ProbeFilter { get; set; } = new();

        [JsonProperty("componentTypeFilter")]
        public PlayMakerTracePatternFilter ComponentTypeFilter { get; set; } = new();
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

    internal sealed class PlayMakerTraceFsmTransitionConfig
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

    internal sealed class PlayMakerTraceFieldWatchConfig
    {
        [JsonProperty("typeName")]
        public string TypeName { get; set; } = "";

        [JsonProperty("fieldName")]
        public string FieldName { get; set; } = "";
    }

    internal sealed class PlayMakerTraceDamagePipelineConfig
    {
        [JsonProperty("includeHitAttempt")]
        public bool IncludeHitAttempt { get; set; } = true;

        [JsonProperty("includeGateChecks")]
        public bool IncludeGateChecks { get; set; } = true;

        [JsonProperty("includeTakeDamage")]
        public bool IncludeTakeDamage { get; set; } = true;

        [JsonProperty("includeInvincible")]
        public bool IncludeInvincible { get; set; } = true;

        [JsonProperty("includeNonFatalHit")]
        public bool IncludeNonFatalHit { get; set; } = true;

        [JsonProperty("includeLimitSendEvents")]
        public bool IncludeLimitSendEvents { get; set; } = true;
    }

    internal sealed class PlayMakerTraceColliderContactConfig
    {
        [JsonProperty("includeTriggerEnter")]
        public bool IncludeTriggerEnter { get; set; } = true;

        [JsonProperty("includeTriggerStay")]
        public bool IncludeTriggerStay { get; set; }

        [JsonProperty("includeTriggerExit")]
        public bool IncludeTriggerExit { get; set; } = true;

        [JsonProperty("includeCollisionEvents")]
        public bool IncludeCollisionEvents { get; set; }

        [JsonProperty("sourceObjectFilter")]
        public PlayMakerTracePatternFilter SourceObjectFilter { get; set; } = new();

        [JsonProperty("targetObjectFilter")]
        public PlayMakerTracePatternFilter TargetObjectFilter { get; set; } = new();
    }

    internal sealed class PlayMakerTraceComponentToggleConfig
    {
        [JsonProperty("componentTypeAllowlist")]
        public List<string> ComponentTypeAllowlist { get; set; } = new();

        [JsonProperty("pollIntervalSeconds")]
        public float PollIntervalSeconds { get; set; } = 0.25f;

        [JsonProperty("maxComponentsPerSample")]
        public int MaxComponentsPerSample { get; set; } = 256;

        [JsonProperty("includeInactiveGameObjects")]
        public bool IncludeInactiveGameObjects { get; set; } = true;
    }

    internal sealed class PlayMakerTraceFieldWatchProbeConfig
    {
        [JsonProperty("emitOnDamageEvents")]
        public bool EmitOnDamageEvents { get; set; } = true;

        [JsonProperty("emitOnLimitSendEvents")]
        public bool EmitOnLimitSendEvents { get; set; } = true;

        [JsonProperty("cadenceSeconds")]
        public float CadenceSeconds { get; set; }

        [JsonProperty("maxObjectsPerSample")]
        public int MaxObjectsPerSample { get; set; } = 32;

        [JsonProperty("serializeCollectionsAsCount")]
        public bool SerializeCollectionsAsCount { get; set; } = true;

        [JsonProperty("maxCollectionPreviewItems")]
        public int MaxCollectionPreviewItems { get; set; } = 5;
    }

    internal sealed class PlayMakerTraceOutputConfig
    {
        [JsonProperty("outputDirOverride")]
        public string OutputDirOverride { get; set; } = "";

        [JsonProperty("filePrefix")]
        public string FilePrefix { get; set; } = "pmtrace";
    }

    internal sealed class PlayMakerTraceCommandScriptConfig
    {
        [JsonProperty("autoRunOnReload")]
        public bool AutoRunOnReload { get; set; }
    }
}
