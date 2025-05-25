using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Modding;
using Modding.Menu;
using ItemChanger;
using ItemChanger.Modules;
using RandoPlus.NailUpgrades;
using HutongGames.PlayMaker;

namespace SmartNail
{
    public class Smartnail : Mod,
                               IGlobalSettings<Settings>,
                               ILocalSettings<Smartnail.LocalSettings>,
                               IMenuMod
    {
        
        private static readonly HashSet<string> ExcludedScenes = new()
        {
            "Menu_Title",
            "Quit_To_Menu"
        };

        
        private int _backedUpUnclaimedUpgrades = -1;
        private bool _isNewGameFlag;
        private bool _modulesRegistered;
        private int _lastSavedLevel  = -1;
        private int _lastSavedDamage = -1;
        private bool _lastSavedHoned = false;

        public class LocalSettings
        {
            public int  StoredNailLevel  = -1;
            public int  StoredNailDamage = -1;
            public bool StoredHonedNail  = false;
        }

        public Settings      GlobalSettings    { get; set; } = new Settings();
        public LocalSettings LocalUserSettings { get; set; } = new LocalSettings();

        public override string GetVersion() => "1.0.0";

        
        public void OnLoadGlobal(Settings s)     => GlobalSettings     = s ?? new Settings();
        public Settings OnSaveGlobal()           => GlobalSettings;
       
        public void OnLoadLocal(LocalSettings s) => LocalUserSettings  = s ?? new LocalSettings();
        public LocalSettings OnSaveLocal()       => LocalUserSettings;
     
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

        public override void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloaded)
        {
            Log("  Initialize");

           
            if (!(ModHooks.GetMod("ItemChangerMod") is Mod)) {
                Log("[Warning] ItemChangerMod missing. Disabling Smartnail.");
                return;
            }
            if (ModHooks.GetMod("RandoPlus") is not Mod) {
    Log("[Warning] RandoPlus not found. Disabling SmartNailMod.");
    return;
}

            
            bool combat = ModHooks.GetMod("CombatRandomizer") is Mod;
            bool curse  = ModHooks.GetMod("CurseRandomizer")  is Mod;
            if (combat || curse) {
                string det = (combat ? "Combat Randomizer" : "") +
                             (combat && curse ? " + " : "") +
                             (curse  ? "Curse Randomizer"  : "");
                Log($"[Warning] Detected {det}. Disabling Smartnail.");
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

        private void OnBeforeStartNewGame()
        {
            
            _isNewGameFlag = true;
        }

        private void OnEnterGame()
        {
            if (_isNewGameFlag) {
                StoreBaseNailStats();
                _isNewGameFlag = false;
            }
            if (!_modulesRegistered && ItemChangerMod.Modules != null) {
                _modulesRegistered = true;
                
            }
        }

        private void OnSaveGame(int id)
        {
            if (PlayerData.instance == null) return;
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (!GlobalSettings.IsSceneEnabled(scene) && !ExcludedScenes.Contains(scene)) {
                int lvl   = PlayerData.instance.GetInt(nameof(PlayerData.nailSmithUpgrades));
                int dmg   = PlayerData.instance.GetInt(nameof(PlayerData.nailDamage));
                bool honed= PlayerData.instance.GetBool(nameof(PlayerData.honedNail));
                if (lvl==_lastSavedLevel && dmg==_lastSavedDamage && honed==_lastSavedHoned) return;
                LogFine("  Saving nail stats");
                StoreBaseNailStats();
                _lastSavedLevel  = lvl;
                _lastSavedDamage = dmg;
                _lastSavedHoned  = honed;
            }
        }

        private void StoreBaseNailStats() {
            LocalUserSettings.StoredNailLevel  = PlayerData.instance.GetInt(nameof(PlayerData.nailSmithUpgrades));
            LocalUserSettings.StoredNailDamage = PlayerData.instance.GetInt(nameof(PlayerData.nailDamage));
            LocalUserSettings.StoredHonedNail  = PlayerData.instance.GetBool(nameof(PlayerData.honedNail));
            LogFine($" Stored stats: L={LocalUserSettings.StoredNailLevel} D={LocalUserSettings.StoredNailDamage} H={LocalUserSettings.StoredHonedNail}");
        }

        private void GameManager_BeginSceneTransition(On.GameManager.orig_BeginSceneTransition orig, GameManager self, GameManager.SceneLoadInfo info)
        {
            bool isBoost = GlobalSettings.IsSceneEnabled(info.SceneName);
            LogFine($" BeginTransition→{info.SceneName}, boost={isBoost}");

            var upMod = ItemChangerMod.Modules.Get<DelayedNailUpgradeModule>();
            if (!isBoost && upMod!=null && _backedUpUnclaimedUpgrades>=0) {
                upMod.UnclaimedUpgrades = _backedUpUnclaimedUpgrades;
                LogFine($" Restored {_backedUpUnclaimedUpgrades} unclaimed");
                _backedUpUnclaimedUpgrades=-1;
                PlayerData.instance.SetInt(nameof(PlayerData.nailSmithUpgrades), LocalUserSettings.StoredNailLevel);
                PlayerData.instance.SetInt(nameof(PlayerData.nailDamage),       LocalUserSettings.StoredNailDamage);
                PlayerData.instance.SetBool(nameof(PlayerData.honedNail),      LocalUserSettings.StoredHonedNail);
                PlayMakerFSM.BroadcastEvent("UPDATE NAIL DAMAGE");
            }
            orig(self, info);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (ExcludedScenes.Contains(scene.name) || PlayerData.instance==null) return;
            bool isBoost = GlobalSettings.IsSceneEnabled(scene.name);
            

            var upMod = ItemChangerMod.Modules.Get<DelayedNailUpgradeModule>();
            if (isBoost && upMod!=null) {
                if (_backedUpUnclaimedUpgrades<0) {
                    _backedUpUnclaimedUpgrades=upMod.UnclaimedUpgrades;
                    LogFine($" Backed up {_backedUpUnclaimedUpgrades} unclaimed");
                }
                int lvl=LocalUserSettings.StoredNailLevel+_backedUpUnclaimedUpgrades;
                int dmg=LocalUserSettings.StoredNailDamage+_backedUpUnclaimedUpgrades*4;
                PlayerData.instance.SetInt(nameof(PlayerData.nailSmithUpgrades), lvl);
                PlayerData.instance.SetInt(nameof(PlayerData.nailDamage),       dmg);
                PlayerData.instance.SetBool(nameof(PlayerData.honedNail),      true);
                PlayMakerFSM.BroadcastEvent("UPDATE NAIL DAMAGE");
                upMod.UnclaimedUpgrades=0;
                
            }
        }
    }
}
