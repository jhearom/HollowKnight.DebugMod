using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Modding;
using UnityEngine;
using UnityEngine.SceneManagement;
using GlobalEnums;
using Object = UnityEngine.Object;

namespace DebugMod
{
    public class DebugMod : Mod<SaveSettings, GlobalSettings>
    {
        private static GameManager _gm;
        private static InputHandler _ih;
        private static HeroController _hc;
        private static GameObject _refKnight;
        private static PlayMakerFSM _refKnightSlash;
        private static CameraController _refCamera;
        private static PlayMakerFSM _refDreamNail;

        internal static GameManager GM => _gm != null ? _gm : (_gm = GameManager.instance);
        internal static InputHandler IH => _ih != null ? _ih : (_ih = GM.inputHandler);
        internal static HeroController HC => _hc != null ? _hc : (_hc = GM.hero_ctrl);
        internal static GameObject RefKnight => _refKnight != null ? _refKnight : (_refKnight = HC.gameObject);
        internal static PlayMakerFSM RefKnightSlash => _refKnightSlash != null ? _refKnightSlash : (_refKnightSlash = RefKnight.transform.Find("Attacks/Slash").GetComponent<PlayMakerFSM>());
        internal static CameraController RefCamera => _refCamera != null ? _refCamera : (_refCamera = GM.cameraCtrl);
        internal static PlayMakerFSM RefDreamNail => _refDreamNail != null ? _refDreamNail : (_refDreamNail = FSMUtility.LocateFSM(RefKnight, "Dream Nail"));

        internal static DebugMod instance;
        /*internal static GlobalSettings settings { get; set; } = new GlobalSettings();
        public void OnLoadGlobal(GlobalSettings s) => DebugMod.settings = s;
        public GlobalSettings OnSaveGlobal() => DebugMod.settings;*/
        internal static GlobalSettings settings;
        
        private static float _loadTime;
        private static float _unloadTime;
        private static bool _loadingChar;

        internal static bool infiniteHP;
        internal static bool infiniteSoul;
        internal static bool playerInvincible;
        internal static bool noclip;
        internal static Vector3 noclipPos;
        internal static bool cameraFollow;
        internal static SaveStateManager saveStateManager;
        internal static bool KeyBindLock;
        internal static bool TimeScaleActive;
        internal static float CurrentTimeScale;

        internal static Dictionary<string, Pair> bindMethods = new Dictionary<string, Pair>();

        public static readonly Dictionary<string, GameObject> PreloadedObjects = new Dictionary<string, GameObject>();
        public override List<(string, string)> GetPreloadNames()
        {
            return new List<(string, string)>
            {
                ("Tutorial_01", "_Enemies/Buzzer")
            };
        }
        
        internal static Dictionary<KeyCode, int> alphaKeyDict = new Dictionary<KeyCode, int>();

        private static readonly Dictionary<string, KeyCode> DefaultBinds = new Dictionary<string, KeyCode>
        {
            { "Toggle All UI", KeyCode.F1 },
            { "Toggle Info", KeyCode.F2 },
            { "Toggle Top Menu", KeyCode.F3 },
            { "Toggle Console", KeyCode.F4 },
            { "Alt. Info Switch", KeyCode.F6 },
            { "Force Camera Follow", KeyCode.F8 },
            { "Toggle Enemy Panel", KeyCode.F9 },
            { "Self Damage", KeyCode.F10 },
            { "Toggle Binds", KeyCode.BackQuote },
            { "Nail Damage +4", KeyCode.Equals },
            { "Nail Damage -4", KeyCode.Minus },
            { "Increase Timescale", KeyCode.KeypadPlus },
            { "Decrease Timescale", KeyCode.KeypadMinus },
            { "Toggle Hero Light", KeyCode.Home },
            { "Toggle Vignette", KeyCode.Insert },
            { "Zoom In", KeyCode.PageUp },
            { "Zoom Out", KeyCode.PageDown },
            { "Reset Camera Zoom", KeyCode.End },
            { "Toggle HUD", KeyCode.Delete },
            { "Hide Hero", KeyCode.Backspace },
        };

        static int alphaStart;
        static int alphaEnd;
        
        public override void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloadedObjects)
        {
            instance = this;

            instance.Log("Initializing");
            LogBuildIdentity();

            float startTime = Time.realtimeSinceStartup;
            instance.Log("Building MethodInfo dict...");

            bindMethods.Clear();
            foreach (MethodInfo method in typeof(BindableFunctions).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                object[] attributes = method.GetCustomAttributes(typeof(BindableMethod), false);

                if (attributes.Any())
                {
                    BindableMethod attr = (BindableMethod)attributes[0];
                    string name = attr.name;
                    string cat = attr.category;

                    bindMethods.Add(name, new Pair(cat, method));
                }
            }
            PreloadedObjects.Add("Enemy", preloadedObjects["Tutorial_01"]["_Enemies/Buzzer"]);
            instance.Log("Done! Time taken: " + (Time.realtimeSinceStartup - startTime) + "s. Found " + bindMethods.Count + " methods");

            settings = GlobalSettings;
            bool firstRun = settings.FirstRun;

            if (settings.binds == null)
            {
                settings.binds = new Dictionary<string, int>();
            }

            if (firstRun)
            {
                instance.Log("First run detected, setting default binds");

                settings.FirstRun = false;
                settings.binds.Clear();
            }

            RepairDefaultBinds(!firstRun);
            LogTrackedBinds();

            if (settings.NumPadForSaveStates)
            {
                alphaStart = (int)KeyCode.Keypad0;
                alphaEnd = (int)KeyCode.Keypad9;
            }
            else
            {
                alphaStart = (int)KeyCode.Alpha0;
                alphaEnd = (int)KeyCode.Alpha9;
            }

            int alphaInt = 0;
            alphaKeyDict.Clear();
                
            for (int i = alphaStart; i <= alphaEnd; i++)
            {
                KeyCode tmpKeyCode = (KeyCode)i;
                alphaKeyDict.Add(tmpKeyCode, alphaInt++);
            }
            

            UnityEngine.SceneManagement.SceneManager.sceneLoaded += LevelActivated;
            GameObject UIObj = new GameObject();
            UIObj.AddComponent<GUIController>();
            UIObj.AddComponent<UumuuCoreExperiment>();
            Object.DontDestroyOnLoad(UIObj);
            
            saveStateManager = new SaveStateManager();
            ModHooks.Instance.SavegameLoadHook += LoadCharacter;
            ModHooks.Instance.NewGameHook += NewCharacter;
            ModHooks.Instance.BeforeSceneLoadHook += OnLevelUnload;
            ModHooks.Instance.TakeHealthHook += PlayerDamaged;
            ModHooks.Instance.ApplicationQuitHook += SaveSettings;

            BossHandler.PopulateBossLists();
            GUIController.Instance.BuildMenus();

            KeyBindLock = false;
            TimeScaleActive = false;
            CurrentTimeScale = 1f;

            Console.AddLine("New session started " + DateTime.Now);
        }
        
        public override string GetVersion()
        {
            return "1.4.2 - 4";
        }

        //public override bool IsCurrent() => true;

        private void SaveSettings()
        {
            SaveGlobalSettings();
            instance.Log("Saved");
        }

        private int PlayerDamaged(int damageAmount) => infiniteHP ? 0 : damageAmount;

        private void NewCharacter() => LoadCharacter(0);

        private void LoadCharacter(int saveId)
        {
            Console.Reset();
            EnemiesPanel.Reset();
            DreamGate.Reset();

            playerInvincible = false;
            infiniteHP = false;
            infiniteSoul = false;
            noclip = false;

            _loadingChar = true;
        }

        private void LevelActivated(Scene sceneTo, LoadSceneMode mode)
        {
            string sceneName = sceneTo.name;
            
            if (_loadingChar)
            {
                TimeSpan timeSpan = TimeSpan.FromSeconds(PlayerData.instance.playTime);
                string text = string.Format("{0:00}.{1:00}", Math.Floor(timeSpan.TotalHours), timeSpan.Minutes);
                int profileID = PlayerData.instance.profileID;
                string saveFilename = "no";// Platform.Current.getsavefilename(profileID);
                DateTime lastWriteTime = File.GetLastWriteTime(Application.persistentDataPath + saveFilename);
                Console.AddLine("New savegame loaded. Profile playtime " + text + " Completion: " + PlayerData.instance.completionPercentage + " Save slot: " + profileID + " Game Version: " + PlayerData.instance.version + " Last Written: " + lastWriteTime);

                _loadingChar = false;
            }

            if (GM.IsGameplayScene())
            {
                _loadTime = Time.realtimeSinceStartup;
                Console.AddLine("New scene loaded: " + sceneName);
                EnemiesPanel.Reset();
                PlayerDeathWatcher.Reset();
                BossHandler.LookForBoss(sceneName);
            }
        }

        private string OnLevelUnload(string toScene)
        {
            _unloadTime = Time.realtimeSinceStartup;

            return toScene;
        }

        public static bool GrimmTroupe()
        {
            return ModHooks.Instance.version.gameVersion.minor >= 2;
        }

        public static string GetSceneName()
        {
            if (GM == null)
            {
                instance.LogWarn("GameManager reference is null in GetSceneName");
                return "";
            }

            string sceneName = GM.GetSceneNameString();
            return sceneName;
        }

        public static float GetLoadTime()
        {
            return (float)Math.Round(_loadTime - _unloadTime, 2);
        }

        public static void Teleport(string scenename, Vector3 pos)
        {
            HC.transform.position = pos;

            HC.EnterWithoutInput(false);
            HC.proxyFSM.SendEvent("HeroCtrl-LeavingScene");
            HC.transform.SetParent(null);

            GM.NoLongerFirstGame();
            GM.SaveLevelState();
            GM.SetState(GameState.EXITING_LEVEL);
            GM.entryGateName = "dreamGate";
            RefCamera.FreezeInPlace();

            HC.ResetState();

            GM.LoadScene(scenename);
        }

        private static void RepairDefaultBinds(bool preserveExisting)
        {
            MigrateBindName("Toggle Menu", "Toggle Top Menu");
            MigrateBindName("Full/Min Info Switch", "Alt. Info Switch");

            foreach (KeyValuePair<string, KeyCode> entry in DefaultBinds)
            {
                if (!preserveExisting || !settings.binds.ContainsKey(entry.Key))
                {
                    settings.binds[entry.Key] = (int)entry.Value;
                }
            }
        }

        private static void MigrateBindName(string oldName, string newName)
        {
            if (!settings.binds.TryGetValue(oldName, out int keyCode))
            {
                return;
            }

            if (!settings.binds.ContainsKey(newName))
            {
                settings.binds[newName] = keyCode;
                instance.Log("Migrated legacy bind \"" + oldName + "\" -> \"" + newName + "\"");
            }

            settings.binds.Remove(oldName);
        }

        private static void LogTrackedBinds()
        {
            LogTrackedBind("Toggle All UI");
            LogTrackedBind("Toggle Top Menu");
            LogTrackedBind("Toggle Console");
            LogTrackedBind("Toggle Binds");
        }

        private static void LogTrackedBind(string bindName)
        {
            if (settings.binds.TryGetValue(bindName, out int keyCode))
            {
                instance.Log("Bind " + bindName + " = " + ((KeyCode)keyCode));
            }
            else
            {
                instance.LogWarn("Bind missing: " + bindName);
            }
        }

        private static void LogBuildIdentity()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string assemblyPath = assembly.Location;
            instance.Log("Build UTC: " + BuildInfo.BuildUtc);

            if (string.IsNullOrEmpty(assemblyPath))
            {
                instance.Log("Build identity: assembly location unavailable");
                return;
            }

            string fileName = Path.GetFileName(assemblyPath);
            DateTime lastWriteUtc = File.GetLastWriteTimeUtc(assemblyPath);
            instance.Log("Build identity: " + fileName + " @ " + lastWriteUtc.ToString("yyyy-MM-ddTHH:mm:ssZ") + " from " + assemblyPath);
        }
    }
}
