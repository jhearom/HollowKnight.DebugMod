using System;
using System.Collections.Generic;
using System.Reflection;
using GlobalEnums;
using HutongGames.PlayMaker;
using UnityEngine;

namespace DebugMod
{
    internal sealed class UumuuCoreExperiment : MonoBehaviour
    {
        private const float ScanInterval = 0.1f;
        private const string LilJellyfishName = "Lil Jellyfish";
        private const string GasExplosionUumuuName = "Gas Explosion Uumuu";
        private const string CorpseJellyfishName = "Corpse Jellyfish";
        private const string MegaJellyfishName = "Mega Jellyfish";

        private static readonly Dictionary<string, string[]> WatchedFsms = new Dictionary<string, string[]>
        {
            { LilJellyfishName, new[] { "Lil Jelly" } },
            { CorpseJellyfishName, new[] { "corpse" } },
            { GasExplosionUumuuName, new[] { "Explosion Control", "damages_enemy", "Uumuu Event" } },
            { MegaJellyfishName, new[] { "Mega Jellyfish", "Bounds" } }
        };

        private static readonly FieldInfo EvasionByHitRemainingField = typeof(HealthManager).GetField("evasionByHitRemaining", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static UumuuCoreExperiment Instance { get; private set; }

        private readonly Dictionary<int, TrackedObjectState> _trackedObjects = new Dictionary<int, TrackedObjectState>();
        private readonly UumuuTraceLogger _traceLogger = new UumuuTraceLogger();

        private float _nextScanTime;
        private bool _coreCarryMode;

        internal bool CoreCarryModeEnabled => _coreCarryMode;

        internal bool TraceEnabled => _traceLogger.Enabled;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            UnityEngine.SceneManagement.SceneManager.sceneLoaded += SceneLoaded;

            On.HealthManager.Hit += HealthManager_Hit;
            On.HealthManager.Die += HealthManager_Die;
            On.HitTaker.Hit += HitTaker_Hit;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                UnityEngine.SceneManagement.SceneManager.sceneLoaded -= SceneLoaded;

                On.HealthManager.Hit -= HealthManager_Hit;
                On.HealthManager.Die -= HealthManager_Die;
                On.HitTaker.Hit -= HitTaker_Hit;

                RestoreAllCoreStates();
                _trackedObjects.Clear();
                Instance = null;
            }
        }

        private void Update()
        {
            if (!_coreCarryMode && !_traceLogger.Enabled)
            {
                return;
            }

            if (GameManager.instance == null || !GameManager.instance.IsGameplayScene())
            {
                return;
            }

            if (Time.unscaledTime < _nextScanTime)
            {
                return;
            }

            _nextScanTime = Time.unscaledTime + ScanInterval;
            RefreshTrackedObjects();
            UpdateFsmStateLogging();
        }

        internal void ToggleCoreCarryMode()
        {
            SetCoreCarryMode(!_coreCarryMode);
        }

        internal void SetCoreCarryMode(bool enabled)
        {
            if (_coreCarryMode == enabled)
            {
                return;
            }

            _coreCarryMode = enabled;

            if (_coreCarryMode)
            {
                RefreshTrackedObjects();
                ApplyCarryModeToTrackedCores();
                Console.AddLine("Uumuu core carry mode enabled");
            }
            else
            {
                RestoreAllCoreStates();
                Console.AddLine("Uumuu core carry mode disabled");
            }
        }

        internal void ExplodeTrackedCores()
        {
            RefreshTrackedObjects();

            int exploded = 0;
            int failed = 0;

            foreach (TrackedObjectState trackedObject in _trackedObjects.Values)
            {
                if (trackedObject.WatchedName != LilJellyfishName || trackedObject.GameObject == null || !trackedObject.GameObject.activeInHierarchy)
                {
                    continue;
                }

                PlayMakerFSM lilJellyFsm;
                if (!trackedObject.Fsms.TryGetValue("Lil Jelly", out lilJellyFsm) || lilJellyFsm == null)
                {
                    failed++;
                    continue;
                }

                lilJellyFsm.SendEvent("ZERO HP");
                exploded++;
                _traceLogger.Log("Explode core requested for " + DescribeObject(trackedObject.GameObject));
            }

            if (exploded == 0 && failed == 0)
            {
                Console.AddLine("No tracked Lil Jellyfish cores found");
                return;
            }

            Console.AddLine("Explode Cores requested for " + exploded + " core(s)" + (failed > 0 ? " (" + failed + " missing FSM)" : string.Empty));
        }

        internal void ToggleTrace()
        {
            if (_traceLogger.Enabled)
            {
                _traceLogger.StopTrace(DebugMod.GetSceneName());
                Console.AddLine("Uumuu trace disabled");
                return;
            }

            _traceLogger.StartTrace(DebugMod.GetSceneName());
            _trackedObjects.Clear();
            RefreshTrackedObjects();
            UpdateFsmStateLogging();
            Console.AddLine("Uumuu trace enabled");
        }

        internal void DumpTrace()
        {
            _traceLogger.DumpTrace();
        }

        private void SceneLoaded(UnityEngine.SceneManagement.Scene sceneTo, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            string newSceneName = sceneTo.name;
            _trackedObjects.Clear();

            if (_traceLogger.Enabled)
            {
                _traceLogger.Log("Scene loaded " + newSceneName);
            }
        }

        private void RefreshTrackedObjects()
        {
            Dictionary<int, ScanGroup> scanGroups = new Dictionary<int, ScanGroup>();
            PlayMakerFSM[] playMakerFsms = FindObjectsOfType<PlayMakerFSM>();

            for (int i = 0; i < playMakerFsms.Length; i++)
            {
                PlayMakerFSM playMakerFsm = playMakerFsms[i];

                if (playMakerFsm == null || playMakerFsm.gameObject == null)
                {
                    continue;
                }

                string watchedName = GetWatchedName(playMakerFsm.gameObject.name);
                if (watchedName == null)
                {
                    continue;
                }

                int instanceId = playMakerFsm.gameObject.GetInstanceID();
                ScanGroup group;
                if (!scanGroups.TryGetValue(instanceId, out group))
                {
                    group = new ScanGroup(playMakerFsm.gameObject, watchedName);
                    scanGroups.Add(instanceId, group);
                }

                group.Fsms.Add(playMakerFsm);
            }

            List<int> trackedIds = new List<int>(_trackedObjects.Keys);
            for (int i = 0; i < trackedIds.Count; i++)
            {
                int trackedId = trackedIds[i];
                if (!scanGroups.ContainsKey(trackedId))
                {
                    RemoveTrackedObject(trackedId, "despawn");
                }
            }

            foreach (KeyValuePair<int, ScanGroup> pair in scanGroups)
            {
                TrackedObjectState trackedObject;
                if (!_trackedObjects.TryGetValue(pair.Key, out trackedObject))
                {
                    trackedObject = new TrackedObjectState(pair.Value.GameObject, pair.Value.WatchedName);
                    _trackedObjects.Add(pair.Key, trackedObject);
                    UpdateTrackedObject(trackedObject, pair.Value);

                    if (_coreCarryMode && trackedObject.WatchedName == LilJellyfishName)
                    {
                        ApplyCarryMode(trackedObject);
                    }

                    if (_traceLogger.Enabled)
                    {
                        _traceLogger.Log("Spawned " + DescribeObject(trackedObject.GameObject));
                    }
                }
                else
                {
                    UpdateTrackedObject(trackedObject, pair.Value);
                    if (_coreCarryMode && trackedObject.WatchedName == LilJellyfishName)
                    {
                        ApplyCarryMode(trackedObject);
                    }
                }
            }
        }

        private void UpdateTrackedObject(TrackedObjectState trackedObject, ScanGroup scanGroup)
        {
            trackedObject.GameObject = scanGroup.GameObject;
            trackedObject.WatchedName = scanGroup.WatchedName;
            trackedObject.Fsms.Clear();

            for (int i = 0; i < scanGroup.Fsms.Count; i++)
            {
                PlayMakerFSM playMakerFsm = scanGroup.Fsms[i];
                if (playMakerFsm != null && !trackedObject.Fsms.ContainsKey(playMakerFsm.FsmName))
                {
                    trackedObject.Fsms.Add(playMakerFsm.FsmName, playMakerFsm);
                }
            }

            if (trackedObject.WatchedName == LilJellyfishName)
            {
                if (trackedObject.RootCollider == null && trackedObject.GameObject != null)
                {
                    trackedObject.RootCollider = trackedObject.GameObject.GetComponent<CircleCollider2D>();
                    if (trackedObject.RootCollider != null)
                    {
                        trackedObject.OriginalColliderEnabled = trackedObject.RootCollider.enabled;
                    }
                }

                if (trackedObject.DamageHero == null && trackedObject.GameObject != null)
                {
                    trackedObject.DamageHero = trackedObject.GameObject.GetComponent<DamageHero>();
                    if (trackedObject.DamageHero != null)
                    {
                        trackedObject.OriginalDamageHeroEnabled = trackedObject.DamageHero.enabled;
                    }
                }
            }
        }

        private void UpdateFsmStateLogging()
        {
            if (!_traceLogger.Enabled)
            {
                return;
            }

            foreach (TrackedObjectState trackedObject in _trackedObjects.Values)
            {
                string[] watchedFsmNames;
                if (!WatchedFsms.TryGetValue(trackedObject.WatchedName, out watchedFsmNames))
                {
                    continue;
                }

                for (int i = 0; i < watchedFsmNames.Length; i++)
                {
                    string watchedFsmName = watchedFsmNames[i];
                    PlayMakerFSM playMakerFsm;
                    if (!trackedObject.Fsms.TryGetValue(watchedFsmName, out playMakerFsm) || playMakerFsm == null)
                    {
                        continue;
                    }

                    string activeStateName = playMakerFsm.ActiveStateName;
                    string previousState;
                    if (!trackedObject.LastFsmStates.TryGetValue(watchedFsmName, out previousState))
                    {
                        trackedObject.LastFsmStates[watchedFsmName] = activeStateName;
                        _traceLogger.Log("FSM " + trackedObject.WatchedName + " :: " + watchedFsmName + " -> " + activeStateName + " on " + DescribeObject(trackedObject.GameObject));
                        continue;
                    }

                    if (previousState != activeStateName)
                    {
                        trackedObject.LastFsmStates[watchedFsmName] = activeStateName;
                        _traceLogger.Log("FSM " + trackedObject.WatchedName + " :: " + watchedFsmName + " -> " + activeStateName + " on " + DescribeObject(trackedObject.GameObject));
                    }
                }
            }
        }

        private void ApplyCarryModeToTrackedCores()
        {
            foreach (TrackedObjectState trackedObject in _trackedObjects.Values)
            {
                if (trackedObject.WatchedName == LilJellyfishName)
                {
                    ApplyCarryMode(trackedObject);
                }
            }
        }

        private void ApplyCarryMode(TrackedObjectState trackedObject)
        {
            if (trackedObject.CarryModeApplied || trackedObject.GameObject == null)
            {
                return;
            }

            if (trackedObject.RootCollider == null)
            {
                trackedObject.RootCollider = trackedObject.GameObject.GetComponent<CircleCollider2D>();
                if (trackedObject.RootCollider != null && !trackedObject.OriginalColliderEnabled.HasValue)
                {
                    trackedObject.OriginalColliderEnabled = trackedObject.RootCollider.enabled;
                }
            }

            if (trackedObject.DamageHero == null)
            {
                trackedObject.DamageHero = trackedObject.GameObject.GetComponent<DamageHero>();
                if (trackedObject.DamageHero != null && !trackedObject.OriginalDamageHeroEnabled.HasValue)
                {
                    trackedObject.OriginalDamageHeroEnabled = trackedObject.DamageHero.enabled;
                }
            }

            if (trackedObject.RootCollider != null)
            {
                trackedObject.RootCollider.enabled = false;
            }

            if (trackedObject.DamageHero != null)
            {
                trackedObject.DamageHero.enabled = false;
            }

            trackedObject.CarryModeApplied = true;
        }

        private void RestoreAllCoreStates()
        {
            foreach (TrackedObjectState trackedObject in _trackedObjects.Values)
            {
                RestoreCarryMode(trackedObject);
            }
        }

        private void RestoreCarryMode(TrackedObjectState trackedObject)
        {
            if (!trackedObject.CarryModeApplied)
            {
                return;
            }

            if (trackedObject.RootCollider != null && trackedObject.OriginalColliderEnabled.HasValue)
            {
                trackedObject.RootCollider.enabled = trackedObject.OriginalColliderEnabled.Value;
            }

            if (trackedObject.DamageHero != null && trackedObject.OriginalDamageHeroEnabled.HasValue)
            {
                trackedObject.DamageHero.enabled = trackedObject.OriginalDamageHeroEnabled.Value;
            }

            trackedObject.CarryModeApplied = false;
        }

        private void RemoveTrackedObject(int instanceId, string reason)
        {
            TrackedObjectState trackedObject;
            if (!_trackedObjects.TryGetValue(instanceId, out trackedObject))
            {
                return;
            }

            if (trackedObject.CarryModeApplied)
            {
                RestoreCarryMode(trackedObject);
            }

            if (_traceLogger.Enabled && trackedObject.GameObject != null)
            {
                _traceLogger.Log(reason + " " + DescribeObject(trackedObject.GameObject));
            }

            _trackedObjects.Remove(instanceId);
        }

        private static string GetWatchedName(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                return null;
            }

            string normalizedName = NormalizeName(objectName);
            if (normalizedName == LilJellyfishName || normalizedName == GasExplosionUumuuName || normalizedName == CorpseJellyfishName || normalizedName == MegaJellyfishName)
            {
                return normalizedName;
            }

            return null;
        }

        private static string NormalizeName(string objectName)
        {
            if (objectName.EndsWith("(Clone)"))
            {
                return objectName.Substring(0, objectName.Length - "(Clone)".Length).TrimEnd();
            }

            return objectName;
        }

        private void HealthManager_Hit(On.HealthManager.orig_Hit orig, HealthManager self, HitInstance hitInstance)
        {
            if (_traceLogger.Enabled)
            {
                int hpBefore = self.hp;
                bool isDeadBefore = self.isDead;
                float evasionBefore = GetEvasionByHitRemaining(self);

                orig(self, hitInstance);

                int hpAfter = self.hp;
                bool isDeadAfter = self.isDead;
                float evasionAfter = GetEvasionByHitRemaining(self);
                bool accepted = hpAfter != hpBefore || isDeadAfter != isDeadBefore;

                _traceLogger.Log("HealthManager.Hit target=" + DescribeObject(self.gameObject) +
                                 " dmg=" + hitInstance.DamageDealt +
                                 " attack=" + hitInstance.AttackType +
                                 " hp=" + hpBefore + "->" + hpAfter +
                                 " dead=" + isDeadBefore + "->" + isDeadAfter +
                                 " accepted=" + accepted +
                                 " invincible=" + self.IsInvincible +
                                 " evasion=" + evasionBefore.ToString("0.000") + "->" + evasionAfter.ToString("0.000"));
                return;
            }

            orig(self, hitInstance);
        }

        private void HealthManager_Die(On.HealthManager.orig_Die orig, HealthManager self, float? attackDirection, AttackTypes attackType, bool ignoreEvasion)
        {
            if (_traceLogger.Enabled)
            {
                _traceLogger.Log("HealthManager.Die target=" + DescribeObject(self.gameObject) +
                                 " attackType=" + attackType +
                                 " attackDirection=" + (attackDirection.HasValue ? attackDirection.Value.ToString("0.000") : "null") +
                                 " ignoreEvasion=" + ignoreEvasion +
                                 " hp=" + self.hp +
                                 " dead=" + self.isDead);
            }

            orig(self, attackDirection, attackType, ignoreEvasion);
        }

        private void HitTaker_Hit(On.HitTaker.orig_Hit orig, GameObject targetGameObject, HitInstance damageInstance, int recursionDepth)
        {
            if (_traceLogger.Enabled)
            {
                _traceLogger.Log("HitTaker.Hit target=" + DescribeObject(targetGameObject) +
                                 " chain=" + GetParentChain(targetGameObject != null ? targetGameObject.transform : null) +
                                 " recursionDepth=" + recursionDepth +
                                 " dmg=" + damageInstance.DamageDealt +
                                 " attack=" + damageInstance.AttackType +
                                 " source=" + DescribeObject(damageInstance.Source));
            }

            orig(targetGameObject, damageInstance, recursionDepth);
        }

        private static float GetEvasionByHitRemaining(HealthManager self)
        {
            if (EvasionByHitRemainingField == null)
            {
                return -1f;
            }

            object value = EvasionByHitRemainingField.GetValue(self);
            return value is float ? (float)value : -1f;
        }

        private static string DescribeObject(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return "<null>";
            }

            return NormalizeName(gameObject.name) + "#" + gameObject.GetInstanceID() + " scene=" + gameObject.scene.name + " path=" + GetTransformPath(gameObject.transform);
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            string path = transform.name;
            Transform current = transform.parent;

            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        private static string GetParentChain(Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            List<string> names = new List<string>();
            Transform current = transform;

            while (current != null && names.Count < 6)
            {
                names.Add(current.name);
                current = current.parent;
            }

            return string.Join(" <- ", names.ToArray());
        }

        private sealed class ScanGroup
        {
            internal readonly GameObject GameObject;
            internal readonly string WatchedName;
            internal readonly List<PlayMakerFSM> Fsms = new List<PlayMakerFSM>();

            internal ScanGroup(GameObject gameObject, string watchedName)
            {
                GameObject = gameObject;
                WatchedName = watchedName;
            }
        }

        private sealed class TrackedObjectState
        {
            internal GameObject GameObject;
            internal string WatchedName;
            internal readonly Dictionary<string, PlayMakerFSM> Fsms = new Dictionary<string, PlayMakerFSM>();
            internal readonly Dictionary<string, string> LastFsmStates = new Dictionary<string, string>();
            internal CircleCollider2D RootCollider;
            internal DamageHero DamageHero;
            internal bool? OriginalColliderEnabled;
            internal bool? OriginalDamageHeroEnabled;
            internal bool CarryModeApplied;

            internal TrackedObjectState(GameObject gameObject, string watchedName)
            {
                GameObject = gameObject;
                WatchedName = watchedName;
            }
        }
    }
}
