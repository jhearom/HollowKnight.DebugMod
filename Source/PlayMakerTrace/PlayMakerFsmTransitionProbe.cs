using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;

namespace DebugMod.PlayMakerTrace
{
    internal sealed class PlayMakerFsmTransitionProbe : IPlayMakerTraceProbe
    {
        private const string UnknownEventName = "__unknown_event__";
        private const string DirectSetStateEventName = "__direct_set_state__";

        private readonly PlayMakerTraceRuntime _runtime;
        private readonly Func<PlayMakerTraceProfile?> _activeProfileAccessor;

        private bool _attached;
        private int _transitionHookDepth;

        internal PlayMakerFsmTransitionProbe(PlayMakerTraceRuntime runtime, Func<PlayMakerTraceProfile?> activeProfileAccessor)
        {
            _runtime = runtime;
            _activeProfileAccessor = activeProfileAccessor;
        }

        public string ProbeType => "fsm_transition";

        public void Attach()
        {
            if (_attached)
            {
                return;
            }

            On.HutongGames.PlayMaker.Fsm.DoTransition += OnDoTransition;
            On.HutongGames.PlayMaker.Fsm.SetState += OnSetState;
            _attached = true;
        }

        public void Detach()
        {
            if (!_attached)
            {
                return;
            }

            On.HutongGames.PlayMaker.Fsm.DoTransition -= OnDoTransition;
            On.HutongGames.PlayMaker.Fsm.SetState -= OnSetState;
            _attached = false;
            _transitionHookDepth = 0;
        }

        private bool OnDoTransition(On.HutongGames.PlayMaker.Fsm.orig_DoTransition orig, Fsm self, FsmTransition transition, bool isGlobal)
        {
            string fromState = self?.ActiveStateName ?? "";
            string toState = transition?.ToState ?? transition?.ToFsmState?.Name ?? "";
            string eventName = transition?.EventName ?? transition?.FsmEvent?.Name ?? "";
            if (string.IsNullOrWhiteSpace(eventName))
            {
                eventName = UnknownEventName;
            }

            TryEmit(self, fromState, toState, eventName);

            _transitionHookDepth++;
            try
            {
                return orig(self, transition, isGlobal);
            }
            finally
            {
                _transitionHookDepth--;
            }
        }

        private void OnSetState(On.HutongGames.PlayMaker.Fsm.orig_SetState orig, Fsm self, string stateName)
        {
            if (_transitionHookDepth == 0)
            {
                string fromState = self?.ActiveStateName ?? "";
                TryEmit(self, fromState, stateName ?? "", DirectSetStateEventName);
            }

            orig(self, stateName);
        }

        private void TryEmit(Fsm? fsm, string fromState, string toState, string eventName)
        {
            string sceneName = GameManager.instance != null ? GameManager.instance.sceneName : "";
            string gameObjectName = fsm?.GameObjectName ?? fsm?.GameObject?.name ?? "";
            string fsmName = fsm?.Name ?? "";
            int? sourceInstanceId = fsm?.GameObject != null ? fsm.GameObject.GetInstanceID() : null;

            PlayMakerTraceFsmPayload? payload = BuildPayload(fsm);

            _runtime.TryEmit(new PlayMakerTraceEventRequest
            {
                ProbeType = ProbeType,
                SceneName = sceneName,
                SourceObject = gameObjectName,
                SourceFsm = fsmName,
                SourceInstanceId = sourceInstanceId,
                EventName = eventName,
                FromState = fromState,
                ToState = toState,
                Payload = payload
            });
        }

        private PlayMakerTraceFsmPayload? BuildPayload(Fsm? fsm)
        {
            PlayMakerTraceProfile? profile = _activeProfileAccessor();
            if (profile == null)
            {
                return null;
            }

            PlayMakerTraceFsmPayload payload = new();
            bool hasAny = false;

            if (profile.FsmTransition.IncludeHeroSnapshot)
            {
                if (PopulateHeroSnapshot(payload))
                {
                    hasAny = true;
                }
            }

            if (profile.FsmTransition.IncludeFsmVars && fsm != null)
            {
                PlayMakerTraceFsmVars? vars = BuildFsmVarSnapshot(fsm, profile.FsmTransition);
                if (vars != null)
                {
                    payload.FsmVars = vars;
                    hasAny = true;
                }
            }

            return hasAny ? payload : null;
        }

        private static bool PopulateHeroSnapshot(PlayMakerTraceFsmPayload payload)
        {
            HeroController? hero = HeroController.instance;
            if (hero == null)
            {
                return false;
            }

            payload.HeroState = hero.hero_state.ToString();
            payload.HeroFlags = new PlayMakerTraceHeroFlags
            {
                ControlReqlinquished = hero.controlReqlinquished,
                TransitionState = hero.transitionState.ToString(),
                CStateTransitioning = hero.cState.transitioning,
                CStateWillHardLand = hero.cState.willHardLand,
                CStateDead = hero.cState.dead
            };

            return true;
        }

        private static PlayMakerTraceFsmVars? BuildFsmVarSnapshot(Fsm fsm, PlayMakerTraceFsmTransitionConfig config)
        {
            FsmVariables variables = fsm.Variables;
            if (variables == null)
            {
                return null;
            }

            Dictionary<string, bool>? bools = ReadBoolVars(variables, config.FsmBoolAllowlist);
            Dictionary<string, int>? ints = ReadIntVars(variables, config.FsmIntAllowlist);
            Dictionary<string, float>? floats = ReadFloatVars(variables, config.FsmFloatAllowlist);

            if (bools == null && ints == null && floats == null)
            {
                return null;
            }

            return new PlayMakerTraceFsmVars
            {
                Bools = bools,
                Ints = ints,
                Floats = floats
            };
        }

        private static Dictionary<string, bool>? ReadBoolVars(FsmVariables variables, List<string> allowlist)
        {
            Dictionary<string, bool> output = new(StringComparer.Ordinal);
            foreach (string variableName in allowlist)
            {
                if (string.IsNullOrWhiteSpace(variableName))
                {
                    continue;
                }

                FsmBool? value = variables.GetFsmBool(variableName);
                if (value != null)
                {
                    output[variableName] = value.Value;
                }
            }

            return output.Count > 0 ? output : null;
        }

        private static Dictionary<string, int>? ReadIntVars(FsmVariables variables, List<string> allowlist)
        {
            Dictionary<string, int> output = new(StringComparer.Ordinal);
            foreach (string variableName in allowlist)
            {
                if (string.IsNullOrWhiteSpace(variableName))
                {
                    continue;
                }

                FsmInt? value = variables.GetFsmInt(variableName);
                if (value != null)
                {
                    output[variableName] = value.Value;
                }
            }

            return output.Count > 0 ? output : null;
        }

        private static Dictionary<string, float>? ReadFloatVars(FsmVariables variables, List<string> allowlist)
        {
            Dictionary<string, float> output = new(StringComparer.Ordinal);
            foreach (string variableName in allowlist)
            {
                if (string.IsNullOrWhiteSpace(variableName))
                {
                    continue;
                }

                FsmFloat? value = variables.GetFsmFloat(variableName);
                if (value != null)
                {
                    output[variableName] = value.Value;
                }
            }

            return output.Count > 0 ? output : null;
        }
    }
}
