using BaboonAPI.Hooks.Initializer;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Globalization;
using System.IO;
using TootTallyCore.Utils.Assets;
using TootTallyCore.Utils.TootTallyModules;
using TootTallyLeaderboard.Replays;
using TootTallySettings;

namespace TootTallyLeaderboard
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency("TootTallyAccounts", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("TootTallyGameModifiers", BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin, ITootTallyModule
    {
        public static Plugin Instance;

        private const string CONFIG_NAME = "TootTally.cfg";
        public Options option;
        private Harmony _harmony;
        public ConfigEntry<bool> ModuleConfigEnabled { get; set; }
        public bool IsConfigInitialized { get; set; }

        //Change this name to whatever you want
        public string Name { get => "TootTally Leaderboard"; set => Name = value; }

        public bool ShouldUpdateSession;

        public static TootTallySettingPage settingPage;

        public static void LogInfo(string msg) => Instance.Logger.LogInfo(msg);
        public static void LogError(string msg) => Instance.Logger.LogError(msg);
        public static void LogWarning(string msg) => Instance.Logger.LogWarning(msg);
        public static void LogDebug(string msg)
        {
            if (TootTallyCore.Plugin.Instance.DebugMode.Value)
                Instance.Logger.LogDebug(msg);
        }

        private void Awake()
        {
            if (Instance != null) return;
            Instance = this;
            _harmony = new Harmony(Info.Metadata.GUID);

            GameInitializationEvent.Register(Info, TryInitialize);
        }

        private void TryInitialize()
        {
            // Bind to the TTModules Config for TootTally
            ModuleConfigEnabled = TootTallyCore.Plugin.Instance.Config.Bind("Modules", "TootTally Leaderboard", true, "Leaderboard and Replay features for TootTally");
            TootTallyModuleManager.AddModule(this);
        }

        public void LoadModule()
        {
            string configPath = Path.Combine(Paths.BepInExRootPath, "config/");
            ConfigFile config = new ConfigFile(configPath + CONFIG_NAME, true);
            option = new Options()
            {
                ShowLeaderboard = Config.Bind("General", "Show Leaderboard", true, "Show TootTally Leaderboard on Song Select."),
                SaveAutoTootedReplays = Config.Bind("General", "Save AutoToot replays", false, "Locally save auto tooted replays. Replays still won't submit to server."),
                ShowCoolS = Config.Bind("General", "Show Cool S", false, "Show special graphic when getting SS and SSS on a song."),
                SubmitScores = Config.Bind("General", "Suibmit Scores", true, "Submit your scores to the Toottally leaderboard."),
                SessionDate = Config.Bind("General", "Session Date", DateTime.Now.ToString(CultureInfo.InvariantCulture), "The last time that the session started recording."),
                SessionStartTT = Config.Bind("General", "TT Session Start", 0f, "The amount of TT you started the session with."),
                ShowcaseMode = Config.Bind("General", "Replay Showcase Mode", false, "Hides the replay HUD and mouse cursor when viewing a replay."),
                LoadLocalReplays = Config.Bind("General", "Load Local Replays", false, "Only load local replays instead of the online leaderboard.")
            };

            TootTallySettings.Plugin.MainTootTallySettingPage.AddToggle("Show Leaderboard", option.ShowLeaderboard);
            TootTallySettings.Plugin.MainTootTallySettingPage.AddToggle("Save AutoToot Replays", option.SaveAutoTootedReplays);
            TootTallySettings.Plugin.MainTootTallySettingPage.AddToggle("Show Cool S", option.ShowCoolS);
            TootTallySettings.Plugin.MainTootTallySettingPage.AddToggle("Submit Scores", option.SubmitScores);
            TootTallySettings.Plugin.MainTootTallySettingPage.AddToggle("Replay Showcase Mode", option.ShowcaseMode);
            TootTallySettings.Plugin.MainTootTallySettingPage.AddToggle("Load Local Replays", option.LoadLocalReplays, OnToggleLocalReplaysLoadReplays);
            AssetManager.LoadAssets(Path.Combine(Path.GetDirectoryName(Instance.Info.Location), "Assets"));

            ShouldUpdateSession = DateTime.TryParse(Instance.option.SessionDate.Value, out DateTime lastSessionDatetime)
                ? lastSessionDatetime.Date.CompareTo(DateTime.Now.Date) != 0
                : true; // Just force the session update if we can't parse it. It's fine.
            //Will make this async before making it active
            _harmony.PatchAll(typeof(LeaderboardFactory));
            _harmony.PatchAll(typeof(ReplaySystemManager));
            _harmony.PatchAll(typeof(GlobalLeaderboardManager));
            LogInfo($"Module loaded!");
        }

        public static void OnToggleLocalReplaysLoadReplays(bool value)
        {
            if (value)
                CachedReplays.LoadCachedReplays();
        }

        public void UnloadModule()
        {
            _harmony.UnpatchSelf();
            settingPage.Remove();
            LogInfo($"Module unloaded!");
        }

        public class Options
        {
            public ConfigEntry<bool> ShowLeaderboard { get; set; }
            public ConfigEntry<bool> SaveAutoTootedReplays { get; set; }
            public ConfigEntry<bool> ShowCoolS { get; set; }
            public ConfigEntry<bool> SubmitScores { get; set; }
            public ConfigEntry<string> SessionDate { get; set; }
            public ConfigEntry<float> SessionStartTT { get; set; }
            public ConfigEntry<bool> ShowcaseMode { get; set; }
            public ConfigEntry<bool> LoadLocalReplays { get; set; }
        }
    }
}