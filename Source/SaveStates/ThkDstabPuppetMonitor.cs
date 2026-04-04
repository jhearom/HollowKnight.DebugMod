using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GlobalEnums;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using Modding.Utils;
using UnityEngine;

namespace DebugMod
{
    internal sealed class ThkDstabPuppetMonitor : MonoBehaviour
    {
        private const string ThkBossName = "Hollow Knight Boss";
        private const string ThkControlFsmName = "Control";
        private const string ThkPhaseControlFsmName = "Phase Control";
        private const string ThkStunControlFsmName = "Stun Control";
        private const string ThkSetPhase3StateName = "Set Phase 3";
        private const string LogDirectoryName = "thk-dstab-puppet-trace";
        private const float LandThresholdEpsilon = 0.001f;
        private static readonly int TerrainLayerMask = 1 << (int)PhysLayers.TERRAIN;

        private static readonly HashSet<string> CorridorStates = new HashSet<string>(StringComparer.Ordinal)
        {
            "Stomp Down",
            "Stun Start",
            "Stun Fall",
            "Stun",
            "Stand Up",
            "Recover",
            "Idle Stance",
            "Choice P2",
            "Choice P3",
            "Puppet Antic",
            "Puppet Up",
            "Puppet Down",
            "PuppetSlam",
            "Puppet Loop",
            "Puppet ReUp",
            "Puppet End"
        };

        private static readonly HashSet<string> EntryMarkerStates = new HashSet<string>(StringComparer.Ordinal)
        {
            "Stomp Down",
            "Stun Start",
            "Stun Fall",
            "Stun",
            "Stand Up",
            "Choice P2",
            "Choice P3",
            "Puppet Antic",
            "Puppet Up",
            "Puppet Down",
            "PuppetSlam",
            "Puppet Loop",
            "Puppet ReUp",
            "Puppet End"
        };

        public static ThkDstabPuppetMonitor Instance { get; private set; }

        private PlayMakerFSM controlFsm;
        private PlayMakerFSM phaseControlFsm;
        private PlayMakerFSM stunControlFsm;
        private GameObject selfObject;
        private Rigidbody2D selfBody;
        private Collider2D selfCollider;
        private string currentState = string.Empty;
        private string previousState = string.Empty;
        private string currentStunControlState = string.Empty;
        private bool dstabLiveSegmentActive;
        private bool dstabAirStaggerSeen;
        private bool dstabStaggerOverrideArmed;
        private bool phase3PromotionIssued;
        private bool pendingPostPuppetAnticCleanupSnapshot;
        private int lastLoggedFrame = -1;
        private string currentLogPath = string.Empty;
        private StreamWriter logWriter;
        private readonly List<Collider2D> terrainColliders = new List<Collider2D>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            CloseLogWriter();
        }

        private void Update()
        {
            if (!RoomSpecific.IsThkSceneActive())
            {
                ResetForSceneExit();
                return;
            }

            EnsureControlFsm();
            PollStateFallback();
            if (!DebugMod.settings.ThkDstabPuppetTraceEnabled || controlFsm == null)
            {
                return;
            }

            if (pendingPostPuppetAnticCleanupSnapshot && string.Equals(currentState, "Puppet Antic", StringComparison.Ordinal))
            {
                pendingPostPuppetAnticCleanupSnapshot = false;
                lastLoggedFrame = Time.frameCount;
                WriteSnapshot("postPuppetAnticCleanup");
                return;
            }

            if (!ShouldWritePerFrameSnapshot() || lastLoggedFrame == Time.frameCount)
            {
                return;
            }

            lastLoggedFrame = Time.frameCount;
            WriteSnapshot("frame");
        }

        internal void HandleControlStateChanged(PlayMakerFSM fsm, string stateName)
        {
            if (fsm == null)
            {
                return;
            }

            controlFsm = fsm;
            ResolveReferences();

            previousState = currentState;
            currentState = stateName ?? string.Empty;

            if (DebugMod.settings.ThkDstabPuppetTraceEnabled && EntryMarkerStates.Contains(currentState))
            {
                WriteMarker("entered " + currentState);
            }

            int phase = GetCurrentPhase();
            if (string.Equals(currentState, "Stomp Down", StringComparison.Ordinal))
            {
                dstabLiveSegmentActive = true;
                dstabStaggerOverrideArmed = DebugMod.settings.ThkDstabPuppetReproAssistEnabled;
                if (dstabStaggerOverrideArmed)
                {
                    WriteMarker("dstab stagger override armed");
                }
            }

            if (string.Equals(currentState, "Puppet Antic", StringComparison.Ordinal))
            {
                pendingPostPuppetAnticCleanupSnapshot = true;
            }

            if (string.Equals(previousState, "Stomp Down", StringComparison.Ordinal))
            {
                if (string.Equals(currentState, "Stun Start", StringComparison.Ordinal))
                {
                    if (dstabStaggerOverrideArmed)
                    {
                        dstabStaggerOverrideArmed = false;
                        WriteMarker("dstab stagger override consumed");
                    }

                    if (dstabLiveSegmentActive && phase == 2)
                    {
                        dstabAirStaggerSeen = true;
                        WriteMarker("dstab_air_stagger_seen = true");
                        TryPromoteToPhase3();
                    }

                    dstabLiveSegmentActive = false;
                }
                else
                {
                    ClearDstabStaggerOverrideWithoutHit();
                    dstabLiveSegmentActive = false;
                }
            }

            if (string.Equals(previousState, "Puppet Down", StringComparison.Ordinal) &&
                string.Equals(currentState, "PuppetSlam", StringComparison.Ordinal))
            {
                WriteMarker("LAND cause=" + GetPuppetLandCause());
            }

            if (DebugMod.settings.ThkDstabPuppetTraceEnabled &&
                (CorridorStates.Contains(previousState) || CorridorStates.Contains(currentState) || EntryMarkerStates.Contains(currentState)))
            {
                WriteSnapshot("state");
            }

            if (dstabAirStaggerSeen && phase == 2 && !phase3PromotionIssued)
            {
                TryPromoteToPhase3();
            }
        }

        internal void HandleStunControlStateChanged(PlayMakerFSM fsm, string stateName)
        {
            if (fsm == null)
            {
                return;
            }

            stunControlFsm = fsm;
            currentStunControlState = stateName ?? string.Empty;

            if (DebugMod.settings.ThkDstabPuppetTraceEnabled)
            {
                WriteMarker("stun_control -> " + currentStunControlState);
            }
        }

        internal void HandleAcceptedHit(HealthManager self, HitInstance hitInstance)
        {
            if (self == null || !DebugMod.settings.ThkDstabPuppetReproAssistEnabled)
            {
                return;
            }

            EnsureControlFsm();
            if (controlFsm == null ||
                !dstabLiveSegmentActive ||
                !dstabStaggerOverrideArmed ||
                dstabAirStaggerSeen)
            {
                return;
            }

            if (string.Equals(currentState, "Stun Start", StringComparison.Ordinal))
            {
                dstabStaggerOverrideArmed = false;
                WriteMarker("dstab stagger override consumed");
                return;
            }

            if (!string.Equals(currentState, "Stomp Down", StringComparison.Ordinal))
            {
                return;
            }

            dstabStaggerOverrideArmed = false;
            WriteMarker("dstab stagger override consumed");
            WriteSnapshot("forcedStagger");
            controlFsm.SendEvent("STUN");
        }

        internal bool TryHandleSelector(SendRandomEventV3 action)
        {
            if (action == null || !RoomSpecific.IsThkSceneActive())
            {
                return false;
            }

            string stateName = action.State != null ? action.State.Name : string.Empty;
            if (!string.Equals(stateName, "Choice P2", StringComparison.Ordinal) &&
                !string.Equals(stateName, "Choice P3", StringComparison.Ordinal))
            {
                return false;
            }

            EnsureControlFsm();
            if (controlFsm == null)
            {
                return false;
            }

            int phase = GetCurrentPhase();
            string forcedEvent = null;
            if (dstabAirStaggerSeen &&
                string.Equals(stateName, "Choice P3", StringComparison.Ordinal) &&
                phase == 3)
            {
                forcedEvent = "PUPPET";
            }
            else if (DebugMod.settings.ThkDstabPuppetReproAssistEnabled &&
                     !dstabAirStaggerSeen &&
                     string.Equals(stateName, "Choice P2", StringComparison.Ordinal) &&
                     phase == 2)
            {
                forcedEvent = "DSTAB";
            }

            if (forcedEvent == null)
            {
                return false;
            }

            if (string.Equals(forcedEvent, "PUPPET", StringComparison.Ordinal))
            {
                WriteMarker("forcing PUPPET");
            }
            else
            {
                WriteMarker("forcing " + forcedEvent);
            }

            controlFsm.SendEvent(forcedEvent);

            if (string.Equals(forcedEvent, "PUPPET", StringComparison.Ordinal))
            {
                dstabAirStaggerSeen = false;
                dstabLiveSegmentActive = false;
                dstabStaggerOverrideArmed = false;
                phase3PromotionIssued = false;
                WriteMarker("dstab_air_stagger_seen = false");
            }

            action.Finish();
            return true;
        }

        internal string GetStatusSummary()
        {
            EnsureControlFsm();
            string phase = GetFsmInt("Phase");
            string stunControl = string.IsNullOrEmpty(currentStunControlState) ? "-" : currentStunControlState;
            string state = string.IsNullOrEmpty(currentState) ? "-" : currentState;
            string armed = dstabAirStaggerSeen ? "armed" : "idle";
            string stagger = dstabStaggerOverrideArmed ? "armed" : "idle";
            string trace = DebugMod.settings.ThkDstabPuppetTraceEnabled ? "on" : "off";
            string assist = DebugMod.settings.ThkDstabPuppetReproAssistEnabled ? "on" : "off";

            return $"THK assist={assist} trace={trace} state={state} phase={phase} stun={stunControl} force={armed} stagger={stagger}";
        }

        internal void OnSettingsChanged()
        {
            if (!DebugMod.settings.ThkDstabPuppetReproAssistEnabled)
            {
                dstabLiveSegmentActive = false;
                dstabAirStaggerSeen = false;
                dstabStaggerOverrideArmed = false;
                phase3PromotionIssued = false;
            }

            if (!DebugMod.settings.ThkDstabPuppetTraceEnabled)
            {
                pendingPostPuppetAnticCleanupSnapshot = false;
                CloseLogWriter();
                lastLoggedFrame = -1;
            }
        }

        private void EnsureControlFsm()
        {
            if (controlFsm != null && controlFsm.gameObject != null &&
                phaseControlFsm != null && phaseControlFsm.gameObject != null &&
                stunControlFsm != null && stunControlFsm.gameObject != null)
            {
                return;
            }

            GameObject thk = GameObject.Find(ThkBossName);
            if (thk == null)
            {
                controlFsm = null;
                phaseControlFsm = null;
                stunControlFsm = null;
                currentStunControlState = string.Empty;
                selfObject = null;
                selfBody = null;
                selfCollider = null;
                return;
            }

            controlFsm = thk.LocateMyFSM(ThkControlFsmName);
            phaseControlFsm = thk.LocateMyFSM(ThkPhaseControlFsmName);
            stunControlFsm = thk.LocateMyFSM(ThkStunControlFsmName);
            currentStunControlState = stunControlFsm != null ? stunControlFsm.ActiveStateName ?? string.Empty : string.Empty;
            ResolveReferences();
        }

        private void PollStateFallback()
        {
            if (controlFsm != null)
            {
                string activeControlState = controlFsm.ActiveStateName ?? string.Empty;
                if (!string.Equals(activeControlState, currentState, StringComparison.Ordinal))
                {
                    HandleControlStateChanged(controlFsm, activeControlState);
                }
            }

            if (stunControlFsm != null)
            {
                string activeStunControlState = stunControlFsm.ActiveStateName ?? string.Empty;
                if (!string.Equals(activeStunControlState, currentStunControlState, StringComparison.Ordinal))
                {
                    HandleStunControlStateChanged(stunControlFsm, activeStunControlState);
                }
            }
        }

        private void ResolveReferences()
        {
            selfObject = GetFsmGameObject("Self");
            if (selfObject == null && controlFsm != null)
            {
                selfObject = controlFsm.gameObject;
            }

            if (selfObject != null)
            {
                selfBody = selfObject.GetComponent<Rigidbody2D>();
                selfCollider = selfObject.GetComponent<Collider2D>();
            }
        }

        private void ResetForSceneExit()
        {
            if (!string.IsNullOrEmpty(currentState) || dstabAirStaggerSeen || logWriter != null)
            {
                WriteMarker("leaving THK scene");
            }

            currentState = string.Empty;
            previousState = string.Empty;
            currentStunControlState = string.Empty;
            dstabLiveSegmentActive = false;
            dstabAirStaggerSeen = false;
            dstabStaggerOverrideArmed = false;
            phase3PromotionIssued = false;
            pendingPostPuppetAnticCleanupSnapshot = false;
            lastLoggedFrame = -1;
            controlFsm = null;
            phaseControlFsm = null;
            stunControlFsm = null;
            selfObject = null;
            selfBody = null;
            selfCollider = null;
            terrainColliders.Clear();
            CloseLogWriter();
        }

        private void WriteSnapshot(string kind)
        {
            if (!DebugMod.settings.ThkDstabPuppetTraceEnabled)
            {
                return;
            }

            EnsureControlFsm();
            if (controlFsm == null || selfObject == null)
            {
                return;
            }

            float selfYValue;
            bool hasSelfY = TryGetFsmFloatValue("Self Y", out selfYValue);
            float puppetSlamYValue;
            bool hasPuppetSlamY = TryGetFsmFloatValue("PuppetSlam Y", out puppetSlamYValue);

            StringBuilder sb = new StringBuilder();
            sb.Append("kind=").Append(kind);
            sb.Append(" frame=").Append(Time.frameCount);
            sb.Append(" time=").Append(Time.time.ToString("F3"));
            sb.Append(" state=").Append(currentState);
            sb.Append(" phase=").Append(GetFsmInt("Phase"));
            sb.Append(" stunControl=").Append(string.IsNullOrEmpty(currentStunControlState) ? "na" : currentStunControlState);

            Vector3 position = selfObject.transform.position;
            Vector2 velocity = selfBody != null ? selfBody.velocity : Vector2.zero;
            sb.Append(" rootY=").Append(position.y.ToString("F3"));
            sb.Append(" velY=").Append(velocity.y.ToString("F3"));
            sb.Append(" selfY=").Append(hasSelfY ? selfYValue.ToString("F3") : "na");
            sb.Append(" puppetSlamY=").Append(hasPuppetSlamY ? puppetSlamYValue.ToString("F3") : "na");
            sb.Append(" deltaY=").Append(hasSelfY && hasPuppetSlamY ? (selfYValue - puppetSlamYValue).ToString("F3") : "na");
            if (string.Equals(currentState, "Puppet Down", StringComparison.Ordinal))
            {
                AppendFocusedColliderStatus(sb, "body", selfObject);
                AppendFocusedColliderStatus(sb, "puppetDown", GetFsmGameObject("Collider PuppetDown"));
            }

            WriteLine(sb.ToString());
        }

        private void WriteMarker(string message)
        {
            if (!DebugMod.settings.ThkDstabPuppetTraceEnabled)
            {
                return;
            }

            WriteLine("kind=marker frame=" + Time.frameCount + " time=" + Time.time.ToString("F3") + " " + message);
        }

        private void WriteLine(string line)
        {
            try
            {
                EnsureLogWriter();
                if (logWriter == null)
                {
                    return;
                }

                logWriter.WriteLine(line);
            }
            catch (Exception e)
            {
                DebugMod.instance.LogError("[THK TRACE] Failed to write trace line:\n" + e);
                CloseLogWriter();
            }
        }

        private void EnsureLogWriter()
        {
            if (logWriter != null)
            {
                return;
            }

            string outputDir = Path.Combine(DebugMod.settings.ModBaseDirectory, LogDirectoryName);
            Directory.CreateDirectory(outputDir);
            currentLogPath = Path.Combine(
                outputDir,
                "thk-dstab-puppet-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".log");
            logWriter = new StreamWriter(currentLogPath, false, new UTF8Encoding(false));
            logWriter.AutoFlush = true;
            logWriter.WriteLine("# THK DSTAB -> PUPPET trace");
            logWriter.WriteLine("# " + DateTime.Now.ToString("O"));
            Console.AddLine("THK trace writing to " + currentLogPath);
        }

        private void CloseLogWriter()
        {
            if (logWriter == null)
            {
                return;
            }

            try
            {
                logWriter.Dispose();
            }
            catch (Exception e)
            {
                DebugMod.instance.LogWarn("[THK TRACE] Failed to close writer:\n" + e);
            }
            finally
            {
                logWriter = null;
            }
        }

        private string GetFsmInt(string variableName)
        {
            if (controlFsm == null)
            {
                return "na";
            }

            FsmInt fsmInt = controlFsm.FsmVariables.FindFsmInt(variableName);
            return fsmInt != null ? fsmInt.Value.ToString() : "na";
        }

        private bool TryGetFsmFloatValue(string variableName, out float value)
        {
            value = 0f;
            if (controlFsm == null)
            {
                return false;
            }

            FsmFloat fsmFloat = controlFsm.FsmVariables.FindFsmFloat(variableName);
            if (fsmFloat == null)
            {
                return false;
            }

            value = fsmFloat.Value;
            return true;
        }

        private int GetCurrentPhase()
        {
            if (controlFsm == null)
            {
                return -1;
            }

            FsmInt fsmInt = controlFsm.FsmVariables.FindFsmInt("Phase");
            return fsmInt != null ? fsmInt.Value : -1;
        }

        private GameObject GetFsmGameObject(string variableName)
        {
            if (controlFsm == null)
            {
                return null;
            }

            FsmGameObject fsmGameObject = controlFsm.FsmVariables.FindFsmGameObject(variableName);
            return fsmGameObject != null ? fsmGameObject.Value : null;
        }

        private string GetPuppetLandCause()
        {
            float selfYValue;
            float puppetSlamYValue;
            if (TryGetFsmFloatValue("Self Y", out selfYValue) &&
                TryGetFsmFloatValue("PuppetSlam Y", out puppetSlamYValue) &&
                selfYValue <= puppetSlamYValue + LandThresholdEpsilon)
            {
                return "Y_THRESHOLD";
            }

            return "TIMEOUT";
        }

        private bool ShouldWritePerFrameSnapshot()
        {
            return string.Equals(currentState, "Puppet Down", StringComparison.Ordinal);
        }

        private void AppendFocusedColliderStatus(StringBuilder sb, string label, GameObject? target)
        {
            Collider2D collider = ResolvePrimaryCollider(target);
            sb.Append(" ").Append(label).Append(".colliderEnabled=").Append(collider != null && collider.enabled ? "1" : "0");
            sb.Append(" ").Append(label).Append(".touchingGround=").Append(collider != null && collider.IsTouchingLayers() ? "1" : "0");
            sb.Append(" ").Append(label).Append(".bottom=").Append(collider != null ? collider.bounds.min.y.ToString("F3") : "na");
            sb.Append(" ").Append(label).Append(".top=").Append(collider != null ? collider.bounds.max.y.ToString("F3") : "na");

            float terrainDistance;
            bool terrainTouching;
            if (TryGetNearestTerrainDistance(collider, out terrainDistance, out terrainTouching))
            {
                sb.Append(" ").Append(label).Append(".terrainTouching=").Append(terrainTouching ? "1" : "0");
                sb.Append(" ").Append(label).Append(".terrainDistance=").Append(terrainDistance.ToString("F3"));
            }
            else
            {
                sb.Append(" ").Append(label).Append(".terrainTouching=na");
                sb.Append(" ").Append(label).Append(".terrainDistance=na");
            }
        }

        private void ClearDstabStaggerOverrideWithoutHit()
        {
            if (!dstabStaggerOverrideArmed)
            {
                return;
            }

            dstabStaggerOverrideArmed = false;
            WriteMarker("dstab stagger override cleared without hit");
        }

        private void TryPromoteToPhase3()
        {
            EnsureControlFsm();
            if (phaseControlFsm == null || phase3PromotionIssued || GetCurrentPhase() != 2)
            {
                return;
            }

            phase3PromotionIssued = true;
            WriteMarker("forcing phase 3");
            phaseControlFsm.SetState(ThkSetPhase3StateName);
        }

        private Collider2D? ResolvePrimaryCollider(GameObject? target)
        {
            Collider2D collider = target != null ? target.GetComponent<Collider2D>() : null;
            if (collider == null && target != null)
            {
                collider = target.GetComponentInChildren<Collider2D>(true);
            }

            return collider;
        }

        private bool TryGetNearestTerrainDistance(Collider2D? collider, out float terrainDistance, out bool terrainTouching)
        {
            terrainDistance = 0f;
            terrainTouching = false;
            if (collider == null || !collider.enabled || collider.gameObject == null || !collider.gameObject.activeInHierarchy)
            {
                return false;
            }

            RefreshTerrainColliders();

            bool found = false;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < terrainColliders.Count; i++)
            {
                Collider2D terrainCollider = terrainColliders[i];
                if (terrainCollider == null ||
                    terrainCollider == collider ||
                    terrainCollider.gameObject == null ||
                    !terrainCollider.enabled ||
                    !terrainCollider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                ColliderDistance2D separation = collider.Distance(terrainCollider);
                if (!separation.isValid)
                {
                    continue;
                }

                if (!found || separation.distance < bestDistance)
                {
                    found = true;
                    bestDistance = separation.distance;
                }
            }

            if (!found)
            {
                return false;
            }

            terrainDistance = bestDistance;
            terrainTouching = bestDistance <= LandThresholdEpsilon;
            return true;
        }

        private void RefreshTerrainColliders()
        {
            terrainColliders.Clear();
            Collider2D[] colliders = Resources.FindObjectsOfTypeAll<Collider2D>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null ||
                    collider.gameObject == null ||
                    collider.gameObject.layer != (int)PhysLayers.TERRAIN ||
                    !collider.enabled ||
                    !collider.gameObject.activeInHierarchy ||
                    !collider.gameObject.scene.IsValid())
                {
                    continue;
                }

                if ((TerrainLayerMask & (1 << collider.gameObject.layer)) == 0)
                {
                    continue;
                }

                terrainColliders.Add(collider);
            }
        }
    }
}
