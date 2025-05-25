using System.Collections.Generic;
using Modding;

namespace SmartNail
{
    /// <summary>
    /// Global toggles for SmartNail scene groups.
    /// </summary>
    public class Settings
    {
        /// <summary>Enable Godhome (GG_…) boss scenes.</summary>
        public bool EnableGodhome     { get; set; } = true;

        /// <summary>Enable Dream boss scenes.</summary>
        public bool EnableDreamBosses { get; set; } = true;

        // Exact Godhome “GG_…” scenes to boost
        private static readonly HashSet<string> GodhomeScenes = new HashSet<string>
        {
            "GG_Vengefly", "GG_Vengefly_V", "GG_Gruz_Mother", "GG_Gruz_Mother_V", "GG_False_Knight",
            "GG_Mega_Moss_Chagrer", "GG_Hornet_1", "GG_Ghost_Gorb", "GG_Ghost_Gorb_V", "GG_Dung_Defender",
            "GG_Mage_Knight", "GG_Mage_Knight_V", "GG_Brooding_Mawlek", "GG_Brooding_Mawlek_V",
            "GG_Nailmasters", "GG_Mighty_Zote", "GG_Ghost_Xero", "GG_Ghost_Xero_V", "GG_Crystal_Guardian",
            "GG_Soul_Master", "GG_Obblobles", "GG_Mantis_Lords", "GG_Mantis_Lords_V", "GG_Ghost_Marmu",
            "GG_Ghost_Marmu_V", "GG_Flukemarm", "GG_Broken_Vessel", "GG_Ghost_Galien", "GG_Painter",
            "GG_Hive_Knight", "GG_Ghost_Hu", "GG_Collector", "GG_Collector_V", "GG_God_Tamer", "GG_Grimm",
            "GG_Watcher_Knights", "GG_Uumuu", "GG_Uumuu_V", "GG_Nosk", "GG_Nosk_V", "GG_Nosk_Hornet",
            "GG_Sly", "GG_Hornet_2", "GG_Crystal_Guardian_2", "GG_Lost_Kin", "GG_Ghost_No_Eyes",
            "GG_Ghost_No_Eyes_V", "GG_Triador_Lord", "GG_Whote_Defender", "GG_Soul_Tyrant",
            "GG_Ghost_Markoth", "GG_Ghost_Markoth_V", "GG_Grey_Prince_Zote", "GG_Failed_Champion",
            "GG_Grimm_Nightmare", "GG_Hollw_Knight", "GG_Radiacne"
        };

        // Exact Dream‐boss scenes to boost
        private static readonly HashSet<string> DreamBossScenes = new HashSet<string>
        {
            "Dream_01_False_Knight",
            "Dream_02_Mage_Lord", "Grimm_Nightmare", "Dream_Mighty_Zote", "Dream_03_Infected_Knight",
            "Dream_04_White_Defender"
        };

        /// <summary>
        /// Returns true if sceneName belongs to any enabled group.
        /// </summary>
        public bool IsSceneEnabled(string sceneName)
        {
            if (EnableGodhome && GodhomeScenes.Contains(sceneName))
                return true;
            if (EnableDreamBosses && DreamBossScenes.Contains(sceneName))
                return true;
            return false;
        }
    }
}
