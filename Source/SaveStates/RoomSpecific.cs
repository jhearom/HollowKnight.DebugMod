using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using IL.HutongGames.PlayMaker.Actions;
using HutongGames;
using TeamCherry;
using UnityEngine;
using Modding.Utils;
using System.Drawing.Text;

namespace DebugMod
{
    public static class RoomSpecific
    {
        private const string ThkSceneName = "room_final_boss_core";
        private const string ThkGateRootName = "Gate";
        private const string ThkGateArtName = "Final_Boss_Gate0004";
        private const string ThkGateColliderName = "Collider";
        private const string ThkGateCameraLockName = "CameraLockArea B";
        private const string ThkBossControlName = "Boss Control";
        private const string ThkBattleStartFsmName = "Battle Start";
        private const string ThkRoarAnticStateName = "Roar Antic";
        private const string ThkBossName = "Hollow Knight Boss";

        private static readonly HashSet<string> ThkEngagedBattleStates = new HashSet<string>(StringComparer.Ordinal)
        {
            "Revisit",
            "Free Pause",
            "Struggle",
            "Break Antic",
            "Break",
            "Fall",
            "Land",
            "Roar Antic",
            "Roar",
            "Particle Burst",
            "Particle End",
            "Fight Start"
        };

        private static bool _hooksInitialized;

        //This class is intended to recreate some scenarios, with more accuracy than that of the savestate class. 
        //This should be eventually included to compatible with savestates, stored in the same location for easier access.
        internal static void InitializeHooks()
        {
            if (_hooksInitialized)
            {
                return;
            }

            On.HutongGames.PlayMaker.Fsm.SetState += OnSetState;
            _hooksInitialized = true;
        }

        #region Rooms
        private static void EnterSpiderTownTrap(int index) //Deepnest_Spider_Town
        {
            string goName = "RestBench Spider";
            string websFsmName = "Fade";
            string benchFsmName = "Bench Control Spider";
            PlayMakerFSM websFSM = FindFsmGlobally(goName, websFsmName);
            PlayMakerFSM benchFSM = FindFsmGlobally(goName, benchFsmName);
            benchFSM.SetState("Start Rest");
            benchFSM.SendEvent("WAIT");
            benchFSM.SendEvent("FINISHED");
            benchFSM.SendEvent("STRUGGLE");
            websFSM.SendEvent("FIRST STRUGGLE");
            websFSM.SendEvent("FINISHED");
            websFSM.SendEvent("FINISHED");
            websFSM.SendEvent("FINISHED");
            websFSM.SendEvent("LAND");
            websFSM.SendEvent("FINISHED");
            if (index == 2)
            {
                websFSM.SendEvent("FINISHED");
            }
        }
        private static void BreakTHKChains(int index)
        {
            if (index == 1)
            {
                string fsmName = "Control";
                string goName1 = "hollow_knight_chain_base";
                string goName2 = "hollow_knight_chain_base 2";
                string goName3 = "hollow_knight_chain_base 3";
                string goName4 = "hollow_knight_chain_base 4";
                PlayMakerFSM fsm1 = FindFsmGlobally(goName1, fsmName);
                PlayMakerFSM fsm2 = FindFsmGlobally(goName2, fsmName);
                PlayMakerFSM fsm3 = FindFsmGlobally(goName3, fsmName);
                PlayMakerFSM fsm4 = FindFsmGlobally(goName4, fsmName);
                fsm1.SetState("Break");
                fsm2.SetState("Break");
                fsm3.SetState("Break");
                fsm4.SetState("Break");
            }

            //alternative method of radiance reload that doesn't softlock on game pause, sets shade correctly, and a couple other minor benefits related to timing
            if (index == 2)
            {
                PlayMakerFSM controlFSM = FindFsmGlobally("Boss Control", "Battle Start");

                controlFSM.SetState("Init");
                controlFSM.SendEvent("Revisit");
                controlFSM.SetState("Fight Start");

                string thkName = "Hollow Knight Boss";

                GameObject thk = GameObject.Find(thkName);
                thk.SetActiveChildren(true);

                GameObject dream = GameObject.Find("Dream Enter");

                PlayMakerFSM thkFSM = thk.LocateMyFSM("Control");
                PlayMakerFSM dreamControlFSM = FindFsmGlobally("Dream Enter", "Control");

                thk.SetActiveChildren(false);
                dream.SetActive(true);
                thkFSM.SetState("Long Roar End");
                thkFSM.SendEvent("Hornet Start");

                dreamControlFSM.SetState("Take Control");
            }

        } //Room_Final_Boss
        private static void ObtainDreamNail(int index)
        {
            string goName = "Witch Control";
            string fsmName = "Control";
            PlayMakerFSM fsm = FindFsmGlobally(goName, fsmName);
            fsm.SetState("Pause");
            fsm.SendEvent("FINISHED");
            fsm.SendEvent("DREAM WAKE");
            fsm.SendEvent("FINISHED");
            fsm.SendEvent("FINISHED");
            fsm.SendEvent("ZONE 1");
            fsm.SendEvent("ZONE 2");
            fsm.SendEvent("ZONE 3");
            fsm.SendEvent("FINISHED");
            DebugMod.HC.transform.position = new Vector3(263.1f, 52.406f);
        }
        private static void FastSoulMaster(int index)
        {
            if (index == 1)
            {
                //start phase 1
                DebugMod.HC.transform.position = new Vector3(19.5810f, 29.41113f); //make sure youre at the right spot ig?
                string goName = "Mage Lord"; //soul master gameobject
                string fsmName = "Mage Lord";//soul master fsm
                PlayMakerFSM fsm = FindFsmGlobally(goName, fsmName);
                fsm.SetState("Init");
            }
            else if (index == 2)
            {
                //start phase 1
                DebugMod.HC.transform.position = new Vector3(19.5810f, 29.41113f); //make sure youre at the right spot ig?
                string goName = "Mage Lord"; //soul master gameobject
                string fsmName = "Mage Lord";//soul master fsm
                string quakegoname = "Quake Fake Parent";
                string quakefsmname = "Appear";
                PlayMakerFSM fsm = FindFsmGlobally(goName, fsmName);
                PlayMakerFSM quakeFakeFSM = FindFsmGlobally(quakegoname, quakefsmname);
                fsm.SetState("Init"); //to close gate and avoid save shenanigans
                GameObject.Destroy(GameObject.Find("Mage Lord"));
                quakeFakeFSM.SendEvent("QUAKE FAKE APPEAR");
            }
        }

        #endregion

        internal static void ReconcileThkGateStateAfterSavestateLoad(string scene)
        {
            if (!IsThkScene(scene) || IsThkGateCloseLayerActive() || !ShouldForceThkGateCloseOnLoad())
            {
                return;
            }

            TryEnsureThkGateCloseLayer("savestate_load");
        }

        //TODO: Add functionality for checking ALL room specifics :(
        internal static (string value, int index) SaveRoomSpecific(string scene)
        {
            scene = scene.ToLower();
            if (ColoSaveState.coloScenes.Contains(scene)) return (ColoSaveState.SaveColoScene(scene), 0);
            if (BossSequenceController.IsInSequence) return (PanthSaveState.SavePanthScene(scene));
            //insert other room specifics here
            return ("0", 0);
        }
        internal static void DoRoomSpecific(string scene, string options, int specialIndex)//index currently used for panth functionality (options is the sequencer, index is boss index, this cant be done by iteration because bench rooms repeat)
        {
            // caps in scene names change across versions
            int legacyOptions = 0;
            scene = scene.ToLower();
            if (ColoSaveState.coloScenes.Contains(scene))
            {
                Console.AddLine("Starting Colo Wave Room Specific");
                ColoSaveState.LoadColoScene(scene, options);
                return;
            }
            //TODO: Fix SetupNewBossScene() in PanthSaveState.cs so we can call LoadPanthScene here
            
            if (PanthSaveState.panthSequences.Contains(options))
            {
                //Console.AddLine("Loading Pantheon Sequencer");
                //PanthSaveState.LoadPanthScene(options, specialIndex);
                return;
            }
            
            try 
            {
                legacyOptions = int.Parse(options); 
            }
            catch (Exception e)
            {
                Console.AddLine("Invalid Room Specific: \n" + e);
            }
            switch (scene)
            {
                case "deepnest_spider_town":
                    EnterSpiderTownTrap(legacyOptions);
                    break;
                case "room_final_boss_core":
                    BreakTHKChains(legacyOptions);
                    break;
                case "dream_nailcollection":
                    ObtainDreamNail(legacyOptions);
                    break;
                case "ruins1_24":
                    FastSoulMaster(legacyOptions);
                    break;
                default:
                    Console.AddLine("No Room Specific Function Found In: " + scene);
                    break;
            }
            }
        private static PlayMakerFSM FindFsmGlobally(string gameObjectName, string fsmName)
        {
            return GameObject.Find(gameObjectName).LocateMyFSM(fsmName);
        }

        private static void OnSetState(On.HutongGames.PlayMaker.Fsm.orig_SetState orig, HutongGames.PlayMaker.Fsm self, string stateName)
        {
            orig(self, stateName);

            if (_hooksInitialized &&
                stateName == ThkRoarAnticStateName &&
                self != null &&
                self.Name == ThkBattleStartFsmName &&
                self.GameObjectName == ThkBossControlName &&
                IsThkScene(GameManager.instance?.sceneName))
            {
                TryEnsureThkGateCloseLayer("battle_start_roar_antic");
            }
        }

        private static bool IsThkScene(string? sceneName)
        {
            return string.Equals(sceneName, ThkSceneName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldForceThkGateCloseOnLoad()
        {
            GameObject thk = GameObject.Find(ThkBossName);
            if (thk != null && thk.activeInHierarchy)
            {
                return true;
            }

            PlayMakerFSM? battleStartFsm = GameObject.Find(ThkBossControlName)?.LocateMyFSM(ThkBattleStartFsmName);
            return battleStartFsm != null && ThkEngagedBattleStates.Contains(battleStartFsm.ActiveStateName);
        }

        private static bool IsThkGateCloseLayerActive()
        {
            GameObject gateRoot = GameObject.Find(ThkGateRootName);
            if (gateRoot == null)
            {
                return false;
            }

            return IsChildActive(gateRoot.transform, ThkGateArtName) &&
                   IsChildActive(gateRoot.transform, ThkGateColliderName) &&
                   IsChildActive(gateRoot.transform, ThkGateCameraLockName);
        }

        private static void TryEnsureThkGateCloseLayer(string source)
        {
            GameObject gateRoot = GameObject.Find(ThkGateRootName);
            if (gateRoot == null)
            {
                return;
            }

            bool changed = false;
            changed |= SetChildActive(gateRoot.transform, ThkGateArtName, true);
            changed |= SetChildActive(gateRoot.transform, ThkGateColliderName, true);
            changed |= SetChildActive(gateRoot.transform, ThkGateCameraLockName, true);

            if (changed)
            {
                string message = $"THK gate-close layer restored via {source}";
                Console.AddLine(message);
                DebugMod.instance.Log("[THK] " + message);
            }
        }

        private static bool IsChildActive(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            return child != null && child.gameObject.activeSelf;
        }

        private static bool SetChildActive(Transform parent, string childName, bool active)
        {
            Transform child = parent.Find(childName);
            if (child == null)
            {
                return false;
            }

            bool changed = child.gameObject.activeSelf != active;
            child.gameObject.SetActive(active);
            return changed;
        }
    }
}
