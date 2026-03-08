using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace DebugMod.PlayMakerTrace
{
    internal sealed class PlayMakerDamagePipelineProbe : IPlayMakerTraceProbe
    {
        private sealed class HitContext
        {
            public long AttemptId { get; set; }
            public int PreHp { get; set; }
            public bool PreIsDead { get; set; }
            public bool PreInvincible { get; set; }
            public float PreEvasion { get; set; }
            public int PreInvincibleFromDirection { get; set; }
            public bool TakeDamageCalled { get; set; }
            public bool InvincibleCalled { get; set; }
            public bool NonFatalHitCalled { get; set; }
            public bool CheckInvincibleCalled { get; set; }
            public bool CheckInvincibleResult { get; set; }
            public bool BlockedByDirection { get; set; }
        }

        private readonly PlayMakerTraceRuntime _runtime;
        private readonly Func<PlayMakerTraceProfile?> _activeProfileAccessor;
        private readonly Dictionary<int, Stack<HitContext>> _hitContexts = new();
        private static readonly FieldInfo? _healthManagerInvincibleField = typeof(HealthManager).GetField("invincible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo? _healthManagerEvasionField = typeof(HealthManager).GetField("evasionByHitRemaining", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo? _healthManagerInvincibleFromDirectionField = typeof(HealthManager).GetField("invincibleFromDirection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo? _limitSendSentListField = typeof(LimitSendEvents).GetField("sentList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private bool _attached;
        private long _nextAttemptId;

        internal PlayMakerDamagePipelineProbe(PlayMakerTraceRuntime runtime, Func<PlayMakerTraceProfile?> activeProfileAccessor)
        {
            _runtime = runtime;
            _activeProfileAccessor = activeProfileAccessor;
        }

        public string ProbeType => "damage_pipeline";

        public void Attach()
        {
            if (_attached)
            {
                return;
            }

            On.HealthManager.Hit += OnHealthManagerHit;
            On.HealthManager.TakeDamage += OnHealthManagerTakeDamage;
            On.HealthManager.Invincible += OnHealthManagerInvincible;
            On.HealthManager.NonFatalHit += OnHealthManagerNonFatalHit;
            On.HealthManager.IsBlockingByDirection += OnHealthManagerIsBlockingByDirection;
            On.HealthManager.CheckInvincible += OnHealthManagerCheckInvincible;
            On.LimitSendEvents.Add += OnLimitSendEventsAdd;
            On.LimitSendEvents.OnEnable += OnLimitSendEventsOnEnable;

            _attached = true;
        }

        public void Detach()
        {
            if (!_attached)
            {
                return;
            }

            On.HealthManager.Hit -= OnHealthManagerHit;
            On.HealthManager.TakeDamage -= OnHealthManagerTakeDamage;
            On.HealthManager.Invincible -= OnHealthManagerInvincible;
            On.HealthManager.NonFatalHit -= OnHealthManagerNonFatalHit;
            On.HealthManager.IsBlockingByDirection -= OnHealthManagerIsBlockingByDirection;
            On.HealthManager.CheckInvincible -= OnHealthManagerCheckInvincible;
            On.LimitSendEvents.Add -= OnLimitSendEventsAdd;
            On.LimitSendEvents.OnEnable -= OnLimitSendEventsOnEnable;

            _hitContexts.Clear();
            _attached = false;
        }

        private void OnHealthManagerHit(On.HealthManager.orig_Hit orig, HealthManager self, HitInstance hitInstance)
        {
            PlayMakerTraceDamagePipelineConfig? config = GetConfig();
            if (!_runtime.IsProbeEnabled(ProbeType) || config == null)
            {
                orig(self, hitInstance);
                return;
            }

            HitContext context = new()
            {
                AttemptId = ++_nextAttemptId,
                PreHp = self.hp,
                PreIsDead = self.isDead,
                PreInvincible = ReadInvincible(self),
                PreEvasion = ReadEvasionByHitRemaining(self),
                PreInvincibleFromDirection = ReadInvincibleFromDirection(self)
            };

            PushContext(self, context);
            if (config.IncludeHitAttempt)
            {
                Emit(self, hitInstance, "hit_attempt", new
                {
                    attempt_id = context.AttemptId,
                    hp_pre = context.PreHp,
                    is_dead_pre = context.PreIsDead,
                    invincible_pre = context.PreInvincible,
                    evasion_pre = context.PreEvasion,
                    invincible_from_direction_pre = context.PreInvincibleFromDirection,
                    hit = SnapshotHit(hitInstance)
                });
            }

            try
            {
                orig(self, hitInstance);
            }
            finally
            {
                PopContext(self);
                Emit(self, hitInstance, "hit_result", new
                {
                    attempt_id = context.AttemptId,
                    outcome = ClassifyOutcome(context, hitInstance),
                    hp_pre = context.PreHp,
                    hp_post = self.hp,
                    is_dead_pre = context.PreIsDead,
                    is_dead_post = self.isDead,
                    invincible_pre = context.PreInvincible,
                    invincible_post = ReadInvincible(self),
                    evasion_pre = context.PreEvasion,
                    evasion_post = ReadEvasionByHitRemaining(self),
                    invincible_from_direction_pre = context.PreInvincibleFromDirection,
                    invincible_from_direction_post = ReadInvincibleFromDirection(self),
                    take_damage_called = context.TakeDamageCalled,
                    invincible_called = context.InvincibleCalled,
                    non_fatal_hit_called = context.NonFatalHitCalled,
                    check_invincible_called = context.CheckInvincibleCalled,
                    check_invincible_result = context.CheckInvincibleResult,
                    blocked_by_direction = context.BlockedByDirection
                });
            }
        }

        private void OnHealthManagerTakeDamage(On.HealthManager.orig_TakeDamage orig, HealthManager self, HitInstance hitInstance)
        {
            PlayMakerTraceDamagePipelineConfig? config = GetConfig();
            if (!_runtime.IsProbeEnabled(ProbeType) || config == null)
            {
                orig(self, hitInstance);
                return;
            }

            HitContext? context = PeekContext(self);
            if (context != null)
            {
                context.TakeDamageCalled = true;
            }

            int hpBefore = self.hp;
            if (config.IncludeTakeDamage)
            {
                Emit(self, hitInstance, "take_damage_entry", new
                {
                    attempt_id = context?.AttemptId,
                    hp_pre = hpBefore,
                    hit = SnapshotHit(hitInstance)
                });
            }

            orig(self, hitInstance);

            if (config.IncludeTakeDamage)
            {
                Emit(self, hitInstance, "take_damage_exit", new
                {
                    attempt_id = context?.AttemptId,
                    hp_pre = hpBefore,
                    hp_post = self.hp,
                    is_dead_post = self.isDead
                });
            }
        }

        private void OnHealthManagerInvincible(On.HealthManager.orig_Invincible orig, HealthManager self, HitInstance hitInstance)
        {
            PlayMakerTraceDamagePipelineConfig? config = GetConfig();
            if (!_runtime.IsProbeEnabled(ProbeType) || config == null)
            {
                orig(self, hitInstance);
                return;
            }

            HitContext? context = PeekContext(self);
            if (context != null)
            {
                context.InvincibleCalled = true;
            }

            float evasionBefore = ReadEvasionByHitRemaining(self);
            int invincibleFromDirectionBefore = ReadInvincibleFromDirection(self);

            if (config.IncludeInvincible)
            {
                Emit(self, hitInstance, "invincible_entry", new
                {
                    attempt_id = context?.AttemptId,
                    evasion_pre = evasionBefore,
                    invincible_from_direction_pre = invincibleFromDirectionBefore,
                    hit = SnapshotHit(hitInstance)
                });
            }

            orig(self, hitInstance);

            if (config.IncludeInvincible)
            {
                Emit(self, hitInstance, "invincible_exit", new
                {
                    attempt_id = context?.AttemptId,
                    evasion_post = ReadEvasionByHitRemaining(self),
                    invincible_from_direction_post = ReadInvincibleFromDirection(self)
                });
            }
        }

        private void OnHealthManagerNonFatalHit(On.HealthManager.orig_NonFatalHit orig, HealthManager self, bool ignoreEvasion)
        {
            PlayMakerTraceDamagePipelineConfig? config = GetConfig();
            if (!_runtime.IsProbeEnabled(ProbeType) || config == null)
            {
                orig(self, ignoreEvasion);
                return;
            }

            HitContext? context = PeekContext(self);
            if (context != null)
            {
                context.NonFatalHitCalled = true;
            }

            float evasionBefore = ReadEvasionByHitRemaining(self);
            orig(self, ignoreEvasion);

            if (config.IncludeNonFatalHit)
            {
                Emit(self, null, "non_fatal_hit", new
                {
                    attempt_id = context?.AttemptId,
                    ignore_evasion = ignoreEvasion,
                    evasion_pre = evasionBefore,
                    evasion_post = ReadEvasionByHitRemaining(self)
                });
            }
        }

        private bool OnHealthManagerIsBlockingByDirection(On.HealthManager.orig_IsBlockingByDirection orig, HealthManager self, int cardinalDirection, AttackTypes attackType)
        {
            PlayMakerTraceDamagePipelineConfig? config = GetConfig();
            if (!_runtime.IsProbeEnabled(ProbeType) || config == null)
            {
                return orig(self, cardinalDirection, attackType);
            }

            bool blocked = orig(self, cardinalDirection, attackType);
            HitContext? context = PeekContext(self);
            if (context != null && blocked)
            {
                context.BlockedByDirection = true;
            }

            if (config.IncludeGateChecks)
            {
                Emit(self, null, "gate_block_check", new
                {
                    attempt_id = context?.AttemptId,
                    cardinal_direction = cardinalDirection,
                    attack_type = attackType.ToString(),
                    blocked
                });
            }

            return blocked;
        }

        private bool OnHealthManagerCheckInvincible(On.HealthManager.orig_CheckInvincible orig, HealthManager self)
        {
            PlayMakerTraceDamagePipelineConfig? config = GetConfig();
            if (!_runtime.IsProbeEnabled(ProbeType) || config == null)
            {
                return orig(self);
            }

            bool result = orig(self);

            HitContext? context = PeekContext(self);
            if (context != null)
            {
                context.CheckInvincibleCalled = true;
                context.CheckInvincibleResult = result;
            }

            if (config.IncludeGateChecks)
            {
                Emit(self, null, "check_invincible", new
                {
                    attempt_id = context?.AttemptId,
                    result
                });
            }

            return result;
        }

        private bool OnLimitSendEventsAdd(On.LimitSendEvents.orig_Add orig, LimitSendEvents self, GameObject obj)
        {
            PlayMakerTraceDamagePipelineConfig? config = GetConfig();
            if (!_runtime.IsProbeEnabled(ProbeType) || config == null)
            {
                return orig(self, obj);
            }

            int beforeCount = GetSentListCount(self);
            bool added = orig(self, obj);
            int afterCount = GetSentListCount(self);

            if (config.IncludeLimitSendEvents)
            {
                _runtime.TryEmit(new PlayMakerTraceEventRequest
                {
                    ProbeType = ProbeType,
                    SceneName = PlayMakerTraceProbeUtil.ResolveSceneName(),
                    SourceObject = self.gameObject != null ? self.gameObject.name : "",
                    SourceInstanceId = self.gameObject != null ? self.gameObject.GetInstanceID() : null,
                    TargetObject = obj != null ? obj.name : "",
                    TargetInstanceId = obj != null ? obj.GetInstanceID() : null,
                    EventName = "limit_send_add",
                    Payload = new
                    {
                        added,
                        sent_list_count_pre = beforeCount,
                        sent_list_count_post = afterCount
                    }
                });
            }

            return added;
        }

        private void OnLimitSendEventsOnEnable(On.LimitSendEvents.orig_OnEnable orig, LimitSendEvents self)
        {
            PlayMakerTraceDamagePipelineConfig? config = GetConfig();
            if (!_runtime.IsProbeEnabled(ProbeType) || config == null)
            {
                orig(self);
                return;
            }

            int beforeCount = GetSentListCount(self);
            orig(self);
            int afterCount = GetSentListCount(self);

            if (config.IncludeLimitSendEvents)
            {
                _runtime.TryEmit(new PlayMakerTraceEventRequest
                {
                    ProbeType = ProbeType,
                    SceneName = PlayMakerTraceProbeUtil.ResolveSceneName(),
                    SourceObject = self.gameObject != null ? self.gameObject.name : "",
                    SourceInstanceId = self.gameObject != null ? self.gameObject.GetInstanceID() : null,
                    EventName = "limit_send_reset",
                    Payload = new
                    {
                        sent_list_count_pre = beforeCount,
                        sent_list_count_post = afterCount
                    }
                });
            }
        }

        private void Emit(HealthManager manager, HitInstance? hitInstance, string eventName, object payload)
        {
            GameObject? source = manager.gameObject;
            GameObject? hitSource = hitInstance.HasValue ? hitInstance.Value.Source : null;

            _runtime.TryEmit(new PlayMakerTraceEventRequest
            {
                ProbeType = ProbeType,
                SceneName = PlayMakerTraceProbeUtil.ResolveSceneName(),
                SourceObject = source != null ? source.name : "",
                SourceInstanceId = source != null ? source.GetInstanceID() : null,
                TargetObject = hitSource != null ? hitSource.name : null,
                TargetInstanceId = hitSource != null ? hitSource.GetInstanceID() : null,
                EventName = eventName,
                Payload = payload
            });
        }

        private static object SnapshotHit(HitInstance? hitInstance)
        {
            if (!hitInstance.HasValue)
            {
                return new { };
            }

            HitInstance hit = hitInstance.Value;
            return new
            {
                attack_type = hit.AttackType.ToString(),
                special_type = hit.SpecialType.ToString(),
                damage_dealt = hit.DamageDealt,
                multiplier = hit.Multiplier,
                magnitude_multiplier = hit.MagnitudeMultiplier,
                ignore_invulnerable = hit.IgnoreInvulnerable,
                direction = hit.Direction,
                move_angle = hit.MoveAngle,
                move_direction = hit.MoveDirection,
                circle_direction = hit.CircleDirection,
                source_name = hit.Source != null ? hit.Source.name : null,
                source_instance_id = hit.Source != null ? hit.Source.GetInstanceID() : (int?)null
            };
        }

        private static int GetSentListCount(LimitSendEvents self)
        {
            object? value = _limitSendSentListField?.GetValue(self);
            return value is ICollection collection ? collection.Count : 0;
        }

        private static string ClassifyOutcome(HitContext context, HitInstance hitInstance)
        {
            if (context.TakeDamageCalled)
            {
                return "take_damage";
            }

            if (context.PreIsDead)
            {
                return "dropped_isDead";
            }

            if (hitInstance.DamageDealt <= 0)
            {
                return "dropped_zero_damage";
            }

            if (context.BlockedByDirection)
            {
                return "blocked_by_direction";
            }

            if (context.PreEvasion > 0f)
            {
                return "dropped_evasion";
            }

            return "unknown";
        }

        private void PushContext(HealthManager manager, HitContext context)
        {
            int key = manager.GetInstanceID();
            if (!_hitContexts.TryGetValue(key, out Stack<HitContext>? stack))
            {
                stack = new Stack<HitContext>();
                _hitContexts[key] = stack;
            }

            stack.Push(context);
        }

        private void PopContext(HealthManager manager)
        {
            int key = manager.GetInstanceID();
            if (!_hitContexts.TryGetValue(key, out Stack<HitContext>? stack))
            {
                return;
            }

            if (stack.Count > 0)
            {
                stack.Pop();
            }

            if (stack.Count == 0)
            {
                _hitContexts.Remove(key);
            }
        }

        private HitContext? PeekContext(HealthManager manager)
        {
            int key = manager.GetInstanceID();
            if (!_hitContexts.TryGetValue(key, out Stack<HitContext>? stack) || stack.Count == 0)
            {
                return null;
            }

            return stack.Peek();
        }

        private PlayMakerTraceDamagePipelineConfig? GetConfig()
        {
            return _activeProfileAccessor()?.DamagePipeline;
        }

        private static bool ReadInvincible(HealthManager manager)
        {
            object? value = _healthManagerInvincibleField?.GetValue(manager);
            return value is bool flag && flag;
        }

        private static float ReadEvasionByHitRemaining(HealthManager manager)
        {
            object? value = _healthManagerEvasionField?.GetValue(manager);
            return value is float amount ? amount : 0f;
        }

        private static int ReadInvincibleFromDirection(HealthManager manager)
        {
            object? value = _healthManagerInvincibleFromDirectionField?.GetValue(manager);
            return value is int direction ? direction : 0;
        }
    }
}
