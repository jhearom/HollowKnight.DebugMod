using System;
using System.Collections;

namespace DebugMod
{
    internal static class SteelSoulDeathRedirect
    {
        private static bool _ordinaryDeathRedirectActive;

        internal static bool IsEnabled => DebugMod.settings.SteelSoulNormalDeathRedirect;

        internal static void InitializeHooks()
        {
            On.HeroController.Die += HeroController_Die;
            On.GameManager.ReadyForRespawn += GameManager_ReadyForRespawn;
            On.SaveGameData.ctor += SaveGameData_ctor;
        }

        internal static void Reset()
        {
            _ordinaryDeathRedirectActive = false;
        }

        private static IEnumerator HeroController_Die(On.HeroController.orig_Die orig, HeroController self)
        {
            if (!ShouldRedirectOrdinarySteelSoulDeath())
            {
                yield return orig(self);
                yield break;
            }

            _ordinaryDeathRedirectActive = true;
            SetPermadeathMode(0);
            Console.AddLine("Steel Soul normal death redirect active");

            try
            {
                yield return orig(self);
            }
            finally
            {
                CleanupIfStillActive();
            }
        }

        private static void SaveGameData_ctor(On.SaveGameData.orig_ctor orig, SaveGameData self, PlayerData playerData, SceneData sceneData)
        {
            if (!_ordinaryDeathRedirectActive)
            {
                orig(self, playerData, sceneData);
                return;
            }

            SetPermadeathMode(1);

            try
            {
                orig(self, playerData, sceneData);
            }
            finally
            {
                if (_ordinaryDeathRedirectActive)
                {
                    SetPermadeathMode(0);
                }
            }
        }

        private static void GameManager_ReadyForRespawn(On.GameManager.orig_ReadyForRespawn orig, GameManager self, bool resetDeaths)
        {
            if (_ordinaryDeathRedirectActive)
            {
                SetPermadeathMode(1);
                _ordinaryDeathRedirectActive = false;
            }

            orig(self, resetDeaths);
        }

        private static bool ShouldRedirectOrdinarySteelSoulDeath()
        {
            return IsEnabled
                && PlayerData.instance != null
                && PlayerData.instance.permadeathMode == 1;
        }

        private static void CleanupIfStillActive()
        {
            if (!_ordinaryDeathRedirectActive)
            {
                return;
            }

            SetPermadeathMode(1);
            _ordinaryDeathRedirectActive = false;
        }

        private static void SetPermadeathMode(int value)
        {
            if (PlayerData.instance != null)
            {
                PlayerData.instance.permadeathMode = value;
            }
        }
    }
}
