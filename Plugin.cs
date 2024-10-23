#define USE_HARMONY

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using HarmonyLib;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.ModAPI;
using Shared.Config;
using Shared.Logging;
using Shared.Patches;
using Shared.Plugin;
using Torch;
using Torch.API;
using Torch.API.Managers;
using Torch.API.Plugins;
using Torch.API.Session;
using Torch.Session;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace TorchPlugin
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class Plugin : TorchPluginBase, IWpfPlugin, ICommonPlugin
    {
        private static readonly HttpClient httpClient = new HttpClient();
        public const string PluginName = "Se_web";
        public static Plugin Instance { get; private set; }
        public long Tick { get; private set; }
        private const ushort MessageId = 5363;
        private ConcurrentDictionary<long, Logic> m_cachedBlocks = new ConcurrentDictionary<long, Logic>();

        private static readonly IPluginLogger Logger = new PluginLogger("Se_web");
        public IPluginLogger Log => Logger;


        public IPluginConfig Config => config?.Data;
        private PersistentConfig<PluginConfig> config;
        private static readonly string ConfigFileName = $"{PluginName}.cfg";

        // ReSharper disable once UnusedMember.Global
        public UserControl GetControl() => control ?? (control = new ConfigView());
        private ConfigView control;

        private TorchSessionManager sessionManager;
        private MySqlConnection connection;
        private bool initialized;
        private bool failed;
        private Queue<object> damageLogQueue = new Queue<object>();
        private readonly object queueLock = new object();


        // ReSharper disable once UnusedMember.Local
        private readonly Commands commands = new Commands();

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public override void Init(ITorchBase torch)
        {
            base.Init(torch);

#if DEBUG
            // Allow the debugger some time to connect once the plugin assembly is loaded
            Thread.Sleep(100);
#endif

            Instance = this;

            Log.Info("Init");
            ConnectToDatabase();
            var configPath = Path.Combine(StoragePath, ConfigFileName);
            config = PersistentConfig<PluginConfig>.Load(Log, configPath);

            var gameVersionNumber = MyPerGameSettings.BasicGameInfo.GameVersion ?? 0;
            var gameVersion = new StringBuilder(MyBuildNumbers.ConvertBuildNumberFromIntToString(gameVersionNumber)).ToString();
            Common.SetPlugin(this, gameVersion, StoragePath);

#if USE_HARMONY
            if (!PatchHelpers.HarmonyPatchAll(Log, new Harmony(Name)))
            {
                failed = true;
                return;
            }
#endif

            sessionManager = torch.Managers.GetManager<TorchSessionManager>();
            sessionManager.SessionStateChanged += SessionStateChanged;

            initialized = true;
        }
        public void ConnectToDatabase()
        {
            string connectionString = "Server=localhost;Database=mydatabase;Uid=root;Pwd=my-secret-pw;";
            connection = new MySqlConnection(connectionString);
            try
            {
                connection.Open();
                Log.Info("MySQL 데이터베이스에 연결되었습니다.");
                string cmdText = "\r\n            CREATE TABLE IF NOT EXISTS damage_logs (\r\n                steam_id BIGINT NOT NULL,\r\n                total_damage FLOAT NOT NULL,\r\n                PRIMARY KEY (steam_id)\r\n            );\r\n        ";
                using (MySqlCommand mySqlCommand = new MySqlCommand(cmdText, connection))
                {
                    mySqlCommand.ExecuteNonQuery();
                    Log.Info("테이블이 없을 경우 새로 생성되었습니다.");
                }
            }
            catch (MySqlException ex)
            {
                Log.Info("MySQL 연결 오류: " + ex.Message);
            }
        }
     

        private void SessionStateChanged(ITorchSession session, TorchSessionState newstate)
        {
            switch (newstate)
            {
                case TorchSessionState.Loading:
                    Log.Debug("Loading");
                    break;

                case TorchSessionState.Loaded:
                    Log.Debug("Loaded");
                    
                    break;

                case TorchSessionState.Unloading:
                    Log.Debug("Unloading");
                    break;

                case TorchSessionState.Unloaded:
                    Log.Debug("Unloaded");
                    break;
            }
        }

        public override void Dispose()
        {
            if (initialized)
            {
                Log.Debug("Disposing");

                sessionManager.SessionStateChanged -= SessionStateChanged;
                sessionManager = null;

                Log.Debug("Disposed");
            }

            Instance = null;

            base.Dispose();
        }

        public override void Update()
        {
        
            if (failed)
                return;

            try
            {
                CustomUpdate();
                Tick++;
               
            }
            catch (Exception e)
            {
                Log.Critical(e, "Update failed");
                failed = true;
            }
        }

        private void CustomUpdate()
        {
            // TODO: Put your update processing here. It is called on every simulation frame!
            PatchHelpers.PatchUpdates();
        }
    }
}