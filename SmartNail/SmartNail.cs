using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Modding;
using Modding.Menu;
using ItemChanger;
using ItemChanger.Modules;
using HutongGames.PlayMaker;
using RandoPlus.NailUpgrades;

namespace SmartNail
{
    public class Smartnail : Mod,
                           IGlobalSettings<Settings>,
                           ILocalSettings<Smartnail.LocalSettings>,
                           IMenuMod
    {
        // Scenes where nail boosts should NOT be applied
        private static readonly HashSet<string> ExcludedScenes = new()
        {
            "Menu_Title",
            "Quit_To_Menu"
        };

        // ——————————————————————————————————————————————————————
        //     Settings / Menu
        // ——————————————————————————————————————————————————————

        public Settings GlobalSettings { get; set; } = new Settings();
        public LocalSettings LocalUserSettings { get; set; } = new LocalSettings();
        public bool ToggleButtonInsideMenu => false;
        public List<IMenuMod.MenuEntry> GetMenuData(IMenuMod.MenuEntry? toggleButtonEntry) => new()
           {
            new IMenuMod.MenuEntry {
                Name        = "Godhome Scenes",
                Description = "Toggle auto-boost for Gohome bosses",
                Values      = new[] { "Off", "On" },
                Saver       = idx => GlobalSettings.EnableGodhome     = idx == 1,
                Loader      = ()    => GlobalSettings.EnableGodhome     ? 1 : 0
            },
            new IMenuMod.MenuEntry {
                Name        = "Dream Bosses",
                Description = "Toggle auto-boost for dream bosses(GPZ,NKG,WD,LK,FC,ST)",
                Values      = new[] { "Off", "On" },
                Saver       = idx => GlobalSettings.EnableDreamBosses = idx == 1,
                Loader      = ()    => GlobalSettings.EnableDreamBosses ? 1 : 0
            }
        };

        public class LocalSettings
        {
            public int StoredNailLevel  = -1;
            public int StoredNailDamage = -1;
            public bool StoredHonedNail = false;
        }

        // ——————————————————————————————————————————————————————
        //     Internal state
        // ——————————————————————————————————————————————————————

        private int     _backedUpUnclaimedUpgrades = -1;
        private bool    _isNewGameFlag;
        private bool    _modulesRegistered;
        private Coroutine _monitorCoroutine;

        // ——————————————————————————————————————————————————————
        //     Initialization & Hooks
        // ——————————————————————————————————————————————————————

        public override string GetVersion() => "1.0.0";

        public void OnLoadGlobal(Settings s)        => GlobalSettings    = s ?? new Settings();
        public Settings OnSaveGlobal()              => GlobalSettings;
        public void OnLoadLocal(LocalSettings s)    => LocalUserSettings = s ?? new LocalSettings();
        public LocalSettings OnSaveLocal()          => LocalUserSettings;

        public override void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloaded)
        {
            Log("Smartnail: Initialize");

            // dependencies
            if (ModHooks.GetMod("ItemChangerMod") == null || ModHooks.GetMod("RandoPlus") == null)
            {
                Log("[Warning] Missing ItemChangerMod or RandoPlus; disabling Smartnail.");
                return;
            }

            // hooks
            Events.BeforeStartNewGame           += OnBeforeStartNewGame;
            Events.OnEnterGame                  += OnEnterGame;
            ModHooks.SavegameSaveHook           += OnSaveGame;
            On.GameManager.BeginSceneTransition += GameManager_BeginSceneTransition;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded            += OnSceneLoaded;
        }

        public void Unload()
        {
            Events.BeforeStartNewGame           -= OnBeforeStartNewGame;
            Events.OnEnterGame                  -= OnEnterGame;
            ModHooks.SavegameSaveHook           -= OnSaveGame;
            On.GameManager.BeginSceneTransition -= GameManager_BeginSceneTransition;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded            -= OnSceneLoaded;
        }

        // ——————————————————————————————————————————————————————
        //     Base-stat storage
        // ——————————————————————————————————————————————————————

        private void OnBeforeStartNewGame() => _isNewGameFlag = true;

        private void OnEnterGame()
        {
            if (_isNewGameFlag)
            {
                StoreBaseNailStats();
                _isNewGameFlag = false;
            }

            if (!_modulesRegistered && ItemChangerMod.Modules != null)
            {
                _modulesRegistered = true;
                Log("Smartnail: ItemChanger modules available.");
            }
        }

        private void OnSaveGame(int id)
        {
            if (PlayerData.instance == null) return;
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            // only snapshot base when NOT in boost / excluded
            if (!GlobalSettings.IsSceneEnabled(scene) && !ExcludedScenes.Contains(scene))
            {
                StoreBaseNailStats();
                Log("Smartnail: Stored base nail stats on save.");
            }
        }

        private void StoreBaseNailStats()
        {
            LocalUserSettings.StoredNailLevel  = PlayerData.instance.GetInt(nameof(PlayerData.nailSmithUpgrades));
            LocalUserSettings.StoredNailDamage = PlayerData.instance.GetInt(nameof(PlayerData.nailDamage));
            LocalUserSettings.StoredHonedNail  = PlayerData.instance.GetBool(nameof(PlayerData.honedNail));
            Log($"Smartnail: Base stats stored → Level={LocalUserSettings.StoredNailLevel}, Damage={LocalUserSettings.StoredNailDamage}, Honed={LocalUserSettings.StoredHonedNail}");
        }

        // ——————————————————————————————————————————————————————
        //     Scene-transition: restore on exit of boost
        // ——————————————————————————————————————————————————————

        private void GameManager_BeginSceneTransition(
            On.GameManager.orig_BeginSceneTransition orig,
            GameManager self,
            GameManager.SceneLoadInfo info)
        {
            bool isBoost = GlobalSettings.IsSceneEnabled(info.SceneName);
            Log($"Smartnail: BeginSceneTransition → {info.SceneName}, Boost={isBoost}");

            var mod = ItemChangerMod.Modules?.Get<DelayedNailUpgradeModule>();
            if (mod != null && !isBoost && _backedUpUnclaimedUpgrades >= 0)
            {
                // restore unclaimed count
                mod.UnclaimedUpgrades = _backedUpUnclaimedUpgrades;
                _backedUpUnclaimedUpgrades = -1;

                // restore true base stats
                PlayerData.instance.SetInt  (nameof(PlayerData.nailSmithUpgrades), LocalUserSettings.StoredNailLevel);
                PlayerData.instance.SetInt  (nameof(PlayerData.nailDamage),       LocalUserSettings.StoredNailDamage);
                PlayerData.instance.SetBool (nameof(PlayerData.honedNail),      LocalUserSettings.StoredHonedNail);
                PlayMakerFSM.BroadcastEvent("UPDATE NAIL DAMAGE");

                Log("Smartnail: Restored base stats and unclaimed upgrades after boost.");

                // stop monitoring
                if (_monitorCoroutine != null)
                {
                    GameManager.instance.StopCoroutine(_monitorCoroutine);
                    _monitorCoroutine = null;
                }
            }

            orig(self, info);
        }

        // ——————————————————————————————————————————————————————
        //     Scene-loaded: snapshot & apply boost, then monitor
        // ——————————————————————————————————————————————————————

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (ExcludedScenes.Contains(scene.name) || PlayerData.instance == null)
                return;

            bool isBoost = GlobalSettings.IsSceneEnabled(scene.name);
            Log($"Smartnail: SceneLoaded → {scene.name}, Boost={isBoost}");

            if (isBoost)
            {
                // kill any existing monitor
                if (_monitorCoroutine != null)
                {
                    GameManager.instance.StopCoroutine(_monitorCoroutine);
                    _monitorCoroutine = null;
                }

                var mod = ItemChangerMod.Modules?.Get<DelayedNailUpgradeModule>();
                if (mod == null)
                {
                    Log("[Warning] DelayedNailUpgradeModule missing on scene load.");
                    return;
                }

                // **snapshot once** per boost sequence
                if (_backedUpUnclaimedUpgrades < 0)
                {
                    _backedUpUnclaimedUpgrades = mod.UnclaimedUpgrades;
                    mod.UnclaimedUpgrades = 0;  // zero it out so future pickups are “new”
                    Log($"Smartnail: Backed up {_backedUpUnclaimedUpgrades} unclaimed upgrades.");
                }

                // apply initial boost exactly once
                int lvl = LocalUserSettings.StoredNailLevel  + _backedUpUnclaimedUpgrades;
                int dmg = LocalUserSettings.StoredNailDamage + _backedUpUnclaimedUpgrades * mod.DamagePerNailUpgrade;
                PlayerData.instance.SetInt  (nameof(PlayerData.nailSmithUpgrades), lvl);
                PlayerData.instance.SetInt  (nameof(PlayerData.nailDamage),       dmg);
                PlayerData.instance.SetBool (nameof(PlayerData.honedNail),      true);
                PlayMakerFSM.BroadcastEvent("UPDATE NAIL DAMAGE");
                Log($"Smartnail: Applied initial boost → Level={lvl}, Damage={dmg}");

                // then start monitoring for genuinely new pickups
                _monitorCoroutine = GameManager.instance.StartCoroutine(BoostMonitorRoutine());
            }
            else
            {
                // leaving boost scene: stop monitor & clear backup
                if (_monitorCoroutine != null)
                {
                    GameManager.instance.StopCoroutine(_monitorCoroutine);
                    _monitorCoroutine = null;
                }

                _backedUpUnclaimedUpgrades = -1;
                Log("Smartnail: Cleared backup on leaving boost scene.");
            }
        }

        // ——————————————————————————————————————————————————————
        //     Coroutine: watch for new pickups in boost
        // ——————————————————————————————————————————————————————

        private IEnumerator BoostMonitorRoutine()
        {
            // small delay to let things settle
            yield return new WaitForSeconds(1f);

            var mod = ItemChangerMod.Modules?.Get<DelayedNailUpgradeModule>();
            if (mod == null)
            {
                Log("[Warning] DelayedNailUpgradeModule missing in boost monitor.");
                yield break;
            }

            while (true)
            {
                int now    = mod.UnclaimedUpgrades;
                int gained = now - _backedUpUnclaimedUpgrades;
                if (gained > 0)
                {
                    _backedUpUnclaimedUpgrades = now;
                    Log($"Smartnail: Detected +{gained} new upgrades in boost scene.");
                }

                // reapply boost (base + all unclaimed)
                int lvl = LocalUserSettings.StoredNailLevel  + _backedUpUnclaimedUpgrades;
                int dmg = LocalUserSettings.StoredNailDamage + _backedUpUnclaimedUpgrades * mod.DamagePerNailUpgrade;
                PlayerData.instance.SetInt  (nameof(PlayerData.nailSmithUpgrades), lvl);
                PlayerData.instance.SetInt  (nameof(PlayerData.nailDamage),       dmg);
                PlayerData.instance.SetBool (nameof(PlayerData.honedNail),      true);
                PlayMakerFSM.BroadcastEvent("UPDATE NAIL DAMAGE");

                yield return new WaitForSeconds(1.5f);
            }
        }
    }
}
