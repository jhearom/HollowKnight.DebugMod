using System;
using UnityEngine;

namespace DebugMod.PlayMakerTrace
{
    internal sealed class PlayMakerColliderContactProbe : IPlayMakerTraceProbe
    {
        private readonly PlayMakerTraceRuntime _runtime;
        private readonly Func<PlayMakerTraceProfile?> _activeProfileAccessor;
        private bool _attached;

        internal PlayMakerColliderContactProbe(PlayMakerTraceRuntime runtime, Func<PlayMakerTraceProfile?> activeProfileAccessor)
        {
            _runtime = runtime;
            _activeProfileAccessor = activeProfileAccessor;
        }

        public string ProbeType => "collider_contact";

        public void Attach()
        {
            if (_attached)
            {
                return;
            }

            On.PlayMakerUnity2DProxy.OnTriggerEnter2D += OnTriggerEnter2D;
            On.PlayMakerUnity2DProxy.OnTriggerStay2D += OnTriggerStay2D;
            On.PlayMakerUnity2DProxy.OnTriggerExit2D += OnTriggerExit2D;

            On.PlayMakerUnity2DProxy.OnCollisionEnter2D += OnCollisionEnter2D;
            On.PlayMakerUnity2DProxy.OnCollisionStay2D += OnCollisionStay2D;
            On.PlayMakerUnity2DProxy.OnCollisionExit2D += OnCollisionExit2D;
            _attached = true;
        }

        public void Detach()
        {
            if (!_attached)
            {
                return;
            }

            On.PlayMakerUnity2DProxy.OnTriggerEnter2D -= OnTriggerEnter2D;
            On.PlayMakerUnity2DProxy.OnTriggerStay2D -= OnTriggerStay2D;
            On.PlayMakerUnity2DProxy.OnTriggerExit2D -= OnTriggerExit2D;

            On.PlayMakerUnity2DProxy.OnCollisionEnter2D -= OnCollisionEnter2D;
            On.PlayMakerUnity2DProxy.OnCollisionStay2D -= OnCollisionStay2D;
            On.PlayMakerUnity2DProxy.OnCollisionExit2D -= OnCollisionExit2D;
            _attached = false;
        }

        private void OnTriggerEnter2D(On.PlayMakerUnity2DProxy.orig_OnTriggerEnter2D orig, PlayMakerUnity2DProxy self, Collider2D coll)
        {
            orig(self, coll);
            TryEmitTrigger(self, coll, "trigger_enter", cfg => cfg.IncludeTriggerEnter);
        }

        private void OnTriggerStay2D(On.PlayMakerUnity2DProxy.orig_OnTriggerStay2D orig, PlayMakerUnity2DProxy self, Collider2D coll)
        {
            orig(self, coll);
            TryEmitTrigger(self, coll, "trigger_stay", cfg => cfg.IncludeTriggerStay);
        }

        private void OnTriggerExit2D(On.PlayMakerUnity2DProxy.orig_OnTriggerExit2D orig, PlayMakerUnity2DProxy self, Collider2D coll)
        {
            orig(self, coll);
            TryEmitTrigger(self, coll, "trigger_exit", cfg => cfg.IncludeTriggerExit);
        }

        private void OnCollisionEnter2D(On.PlayMakerUnity2DProxy.orig_OnCollisionEnter2D orig, PlayMakerUnity2DProxy self, Collision2D coll)
        {
            orig(self, coll);
            TryEmitCollision(self, coll, "collision_enter");
        }

        private void OnCollisionStay2D(On.PlayMakerUnity2DProxy.orig_OnCollisionStay2D orig, PlayMakerUnity2DProxy self, Collision2D coll)
        {
            orig(self, coll);
            TryEmitCollision(self, coll, "collision_stay");
        }

        private void OnCollisionExit2D(On.PlayMakerUnity2DProxy.orig_OnCollisionExit2D orig, PlayMakerUnity2DProxy self, Collision2D coll)
        {
            orig(self, coll);
            TryEmitCollision(self, coll, "collision_exit");
        }

        private void TryEmitTrigger(PlayMakerUnity2DProxy self, Collider2D coll, string eventName, Func<PlayMakerTraceColliderContactConfig, bool> includeSelector)
        {
            if (!_runtime.IsProbeEnabled(ProbeType))
            {
                return;
            }

            PlayMakerTraceColliderContactConfig? config = _activeProfileAccessor()?.ColliderContact;
            if (config == null || !includeSelector(config))
            {
                return;
            }

            GameObject? sourceObject = self.gameObject;
            GameObject? targetObject = coll != null ? coll.gameObject : null;
            string sourceName = sourceObject != null ? sourceObject.name : "";
            string targetName = targetObject != null ? targetObject.name : "";

            if (!PlayMakerTraceProbeUtil.MatchesPattern(config.SourceObjectFilter, sourceName) ||
                !PlayMakerTraceProbeUtil.MatchesPattern(config.TargetObjectFilter, targetName))
            {
                return;
            }

            Collider2D? sourceCollider = sourceObject != null ? sourceObject.GetComponent<Collider2D>() : null;
            _runtime.TryEmit(new PlayMakerTraceEventRequest
            {
                ProbeType = ProbeType,
                SceneName = PlayMakerTraceProbeUtil.ResolveSceneName(),
                SourceObject = sourceName,
                SourceInstanceId = sourceObject != null ? sourceObject.GetInstanceID() : null,
                TargetObject = targetName,
                TargetInstanceId = targetObject != null ? targetObject.GetInstanceID() : null,
                EventName = eventName,
                ComponentType = coll != null ? coll.GetType().FullName : null,
                Payload = new
                {
                    source_collider_type = sourceCollider != null ? sourceCollider.GetType().FullName : null,
                    source_is_trigger = sourceCollider != null && sourceCollider.isTrigger,
                    target_collider_type = coll != null ? coll.GetType().FullName : null,
                    target_is_trigger = coll != null && coll.isTrigger,
                    target_enabled = coll != null && coll.enabled,
                    target_tag = targetObject != null ? targetObject.tag : null,
                    target_layer = targetObject != null ? targetObject.layer : -1,
                    target_rigidbody = coll != null && coll.attachedRigidbody != null
                        ? new
                        {
                            name = coll.attachedRigidbody.name,
                            instance_id = coll.attachedRigidbody.GetInstanceID()
                        }
                        : null
                }
            });
        }

        private void TryEmitCollision(PlayMakerUnity2DProxy self, Collision2D coll, string eventName)
        {
            if (!_runtime.IsProbeEnabled(ProbeType))
            {
                return;
            }

            PlayMakerTraceColliderContactConfig? config = _activeProfileAccessor()?.ColliderContact;
            if (config == null || !config.IncludeCollisionEvents)
            {
                return;
            }

            GameObject? sourceObject = self.gameObject;
            GameObject? targetObject = coll != null && coll.collider != null ? coll.collider.gameObject : null;
            string sourceName = sourceObject != null ? sourceObject.name : "";
            string targetName = targetObject != null ? targetObject.name : "";

            if (!PlayMakerTraceProbeUtil.MatchesPattern(config.SourceObjectFilter, sourceName) ||
                !PlayMakerTraceProbeUtil.MatchesPattern(config.TargetObjectFilter, targetName))
            {
                return;
            }

            _runtime.TryEmit(new PlayMakerTraceEventRequest
            {
                ProbeType = ProbeType,
                SceneName = PlayMakerTraceProbeUtil.ResolveSceneName(),
                SourceObject = sourceName,
                SourceInstanceId = sourceObject != null ? sourceObject.GetInstanceID() : null,
                TargetObject = targetName,
                TargetInstanceId = targetObject != null ? targetObject.GetInstanceID() : null,
                EventName = eventName,
                ComponentType = coll != null && coll.collider != null ? coll.collider.GetType().FullName : null,
                Payload = new
                {
                    contacts = coll != null ? coll.contactCount : 0
                }
            });
        }
    }
}
