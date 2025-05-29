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
        // Scenes to ignore boosting
        private static readonly HashSet<string> ExcludedScenes = new()
        {
            "Menu_Title",
            "Quit_To_Menu"
        };

        // — Settings & Menu —
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
            public int  StoredNailLevel  = -1;
            public int  StoredNailDamage = -1;
            public bool StoredHonedNail  = false;
        }

        // — Internal State —
        private int      _backedUpUnclaimedUpgrades = -1;
        private bool     _isNewGameFlag;
        private bool     _modulesRegistered;
        private Coroutine _monitorCoroutine;

        // — Initialization & Hooks —
        public override string GetVersion() => "1.3.0";
        public void OnLoadGlobal(Settings s)     => GlobalSettings    = s ?? new Settings();
        public Settings OnSaveGlobal()           => GlobalSettings;
        public void OnLoadLocal(LocalSettings s) => LocalUserSettings = s ?? new LocalSettings();
        public LocalSettings OnSaveLocal()       => LocalUserSettings;

        public override void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloaded)
        {
            Log("Initialize");

            if (ModHooks.GetMod("ItemChangerMod") == null || ModHooks.GetMod("RandoPlus") == null)
            {
                Log("[Warning] Missing ItemChangerMod or RandoPlus; disabling Smartnail.");
                return;
            }

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

        // — Base-stat Storage —
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
               
            }
        }

        private void OnSaveGame(int id)
        {
            if (PlayerData.instance == null) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            if (!GlobalSettings.IsSceneEnabled(scene) && !ExcludedScenes.Contains(scene))
            {
                StoreBaseNailStats();
                Log(" Stored base nail stats on save.");
            }
        }

        private void StoreBaseNailStats()
        {
            LocalUserSettings.StoredNailLevel  = PlayerData.instance.GetInt(nameof(PlayerData.nailSmithUpgrades));
            LocalUserSettings.StoredNailDamage = PlayerData.instance.GetInt(nameof(PlayerData.nailDamage));
            LocalUserSettings.StoredHonedNail  = PlayerData.instance.GetBool(nameof(PlayerData.honedNail));
            //this could potentially be useful but its unlikely so i'm leaving it like that
            //LogFine($": Base stats stored → Level={LocalUserSettings.StoredNailLevel}, Damage={LocalUserSettings.StoredNailDamage}, Honed={LocalUserSettings.StoredHonedNail}");
        }

        // — Scene Transition: Restore on Exit of Boost Chain —
        private void GameManager_BeginSceneTransition(
            On.GameManager.orig_BeginSceneTransition orig,
            GameManager self,
            GameManager.SceneLoadInfo info)
        {
            orig(self, info);

            bool isBoost = GlobalSettings.IsSceneEnabled(info.SceneName);
            LogFine($"Smartnail: BeginSceneTransition → {info.SceneName}, Boost={isBoost}");

            var mod = ItemChangerMod.Modules?.Get<DelayedNailUpgradeModule>();
            if (mod != null && !isBoost && _backedUpUnclaimedUpgrades >= 0)
            {
                mod.UnclaimedUpgrades = _backedUpUnclaimedUpgrades;
                _backedUpUnclaimedUpgrades = -1;

                PlayerData.instance.SetInt(nameof(PlayerData.nailSmithUpgrades), LocalUserSettings.StoredNailLevel);
                PlayerData.instance.SetInt(nameof(PlayerData.nailDamage),       LocalUserSettings.StoredNailDamage);
                PlayerData.instance.SetBool(nameof(PlayerData.honedNail),       LocalUserSettings.StoredHonedNail);
                PlayMakerFSM.BroadcastEvent("UPDATE NAIL DAMAGE");

                LogFine("Restored base stats and unclaimed upgrades after boost.");

                if (_monitorCoroutine != null)
                {
                    GameManager.instance.StopCoroutine(_monitorCoroutine);
                    _monitorCoroutine = null;
                }
            }
        }

        // — Scene Loaded: Delayed Snapshot & Apply Boost, Then Monitor —
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (ExcludedScenes.Contains(scene.name) || PlayerData.instance == null)
                return;

            bool isBoost = GlobalSettings.IsSceneEnabled(scene.name);
            LogFine($" SceneLoaded → {scene.name}, Boost={isBoost}");

            if (_monitorCoroutine != null)
            {
                GameManager.instance.StopCoroutine(_monitorCoroutine);
                _monitorCoroutine = null;
            }

            if (isBoost)
            {
                _monitorCoroutine = GameManager.instance.StartCoroutine(DelayedBoostApplication());
            }
            else
            {
                _backedUpUnclaimedUpgrades = -1;
                LogFine(" Cleared backup on leaving boost scene.");
            }
        }

        private IEnumerator DelayedBoostApplication()
        {
            yield return null;
            yield return new WaitForSeconds(0.1f);

            var mod = ItemChangerMod.Modules?.Get<DelayedNailUpgradeModule>();
            if (mod == null)
            {
                Modding.Logger.Log("[Warning] DelayedNailUpgradeModule missing after delay.");
                yield break;
            }

            if (_backedUpUnclaimedUpgrades < 0)
            {
                _backedUpUnclaimedUpgrades = mod.UnclaimedUpgrades;
                mod.UnclaimedUpgrades = 0;
                LogFine($" Backed up {_backedUpUnclaimedUpgrades} unclaimed upgrades.");
            }

            int lvl = LocalUserSettings.StoredNailLevel  + _backedUpUnclaimedUpgrades;
            int dmg = LocalUserSettings.StoredNailDamage + _backedUpUnclaimedUpgrades * mod.DamagePerNailUpgrade;
            PlayerData.instance.SetInt(nameof(PlayerData.nailSmithUpgrades), lvl);
            PlayerData.instance.SetInt(nameof(PlayerData.nailDamage),       dmg);
            PlayerData.instance.SetBool(nameof(PlayerData.honedNail),      true);
            PlayMakerFSM.BroadcastEvent("UPDATE NAIL DAMAGE");

            LogFine($" Applied initial boost → Level={lvl}, Damage={dmg}");

            _monitorCoroutine = GameManager.instance.StartCoroutine(BoostMonitorRoutine());
        }

        // monitors for new upgrades mid-fight
        private IEnumerator BoostMonitorRoutine()
        {
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
                    LogFine($" Detected +{gained} new upgrades mid-scene.");
                }

                int lvl = LocalUserSettings.StoredNailLevel  + _backedUpUnclaimedUpgrades;
                int dmg = LocalUserSettings.StoredNailDamage + _backedUpUnclaimedUpgrades * mod.DamagePerNailUpgrade;
                PlayerData.instance.SetInt(nameof(PlayerData.nailSmithUpgrades), lvl);
                PlayerData.instance.SetInt(nameof(PlayerData.nailDamage),       dmg);
                PlayerData.instance.SetBool(nameof(PlayerData.honedNail),      true);
                PlayMakerFSM.BroadcastEvent("UPDATE NAIL DAMAGE");

                yield return new WaitForSeconds(1.5f);
            }
        }
    }
}
