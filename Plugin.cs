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
        // Constants
        public const string PluginName = "Se_web";
        private const ushort MessageId = 5363;
        private const string ConfigFileName = PluginName + ".cfg";
        private const string ConnectionString = "Server=localhost;Database=mydatabase;Uid=root;Pwd=my-secret-pw;";

        // Static Fields
        public static Plugin Instance { get; private set; }
        private static readonly HttpClient HttpClient = new HttpClient();
        private static readonly IPluginLogger Logger = new PluginLogger(PluginName);

        // Properties
        public IPluginLogger Log
        {
            get { return Logger; }
        }

        public IPluginConfig Config
        {
            get { return _config != null ? _config.Data : null; }
        }

        public long Tick { get; private set; }

        // Private Fields
        private PersistentConfig<PluginConfig> _config;
        private ConfigView _control;
        private TorchSessionManager _sessionManager;
        private MySqlConnection _connection;
        private bool _initialized;
        private bool _failed;

        private readonly object _queueLock = new object();
        private readonly Queue<object> _damageLogQueue = new Queue<object>();
        private readonly List<string> _pointBlockTypes = new List<string>
        {
            "MyObjectBuilder_Beacon",
            "MyObjectBuilder_InteriorTurret",
            "MyObjectBuilder_LargeMissileTurret",
            "MyObjectBuilder_SmallMissileLauncher",
            "MyObjectBuilder_SmallMissileLauncherReload",
            "MyObjectBuilder_LargeGatlingTurret",
            "MyObjectBuilder_SmallGatlingGun"
        };

        // Commands Instance
        private readonly Commands _commands = new Commands();

        // IWpfPlugin Implementation
        public UserControl GetControl()
        {
            return _control ?? (_control = new ConfigView());
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public override void Init(ITorchBase torch)
        {
            base.Init(torch);

#if DEBUG
            // Allow the debugger some time to connect once the plugin assembly is loaded
            Thread.Sleep(100);
#endif

            Instance = this;
            Log.Info("Initializing Plugin...");

            ConnectToDatabase();

            var configPath = Path.Combine(StoragePath, ConfigFileName);
            _config = PersistentConfig<PluginConfig>.Load(Log, configPath);

            var gameVersionNumber = MyPerGameSettings.BasicGameInfo.GameVersion.HasValue ? MyPerGameSettings.BasicGameInfo.GameVersion.Value : 0;
            var gameVersion = MyBuildNumbers.ConvertBuildNumberFromIntToString(gameVersionNumber).ToString();
            Common.SetPlugin(this, gameVersion, StoragePath);

#if USE_HARMONY
            if (!PatchHelpers.HarmonyPatchAll(Log, new Harmony(Name)))
            {
                _failed = true;
                return;
            }
#endif

            _sessionManager = torch.Managers.GetManager<TorchSessionManager>();
            _sessionManager.SessionStateChanged += SessionStateChanged;

            _initialized = true;
            Log.Info("Plugin Initialized Successfully.");
        }

        public override void Dispose()
        {
            if (_initialized)
            {
                Log.Info("Disposing Plugin...");

                _sessionManager.SessionStateChanged -= SessionStateChanged;
                _sessionManager = null;

                if (_connection != null)
                {
                    _connection.Close();
                    _connection = null;
                }

                _initialized = false;
                Log.Info("Plugin Disposed.");
            }

            Instance = null;
            base.Dispose();
        }

        public override void Update()
        {
            if (_failed)
                return;

            try
            {
                CustomUpdate();
                Tick++;

                // Asynchronously send damage logs
                Task.Run(() => SendDamageLogsBatchAsync());
            }
            catch (Exception e)
            {
                Log.Critical(e, "Update failed");
                _failed = true;
            }
        }

        #region Database Connection

        private void ConnectToDatabase()
        {
            try
            {
                _connection = new MySqlConnection(ConnectionString);
                _connection.Open();
                Log.Info("Connected to MySQL database.");

                // Create table if it doesn't exist
                string createTableQuery = @"
                    CREATE TABLE IF NOT EXISTS damage_logs (
                        steam_id BIGINT NOT NULL,
                        total_damage FLOAT NOT NULL,
                        PRIMARY KEY (steam_id)
                    );
                ";

                using (var command = new MySqlCommand(createTableQuery, _connection))
                {
                    command.ExecuteNonQuery();
                    Log.Info("Table 'damage_logs' is ready.");
                }
            }
            catch (MySqlException ex)
            {
                Log.Error("MySQL connection error: " + ex.Message);
                _failed = true;
            }
        }

        #endregion

        #region Session Events

        private void SessionStateChanged(ITorchSession session, TorchSessionState newState)
        {
            switch (newState)
            {
                case TorchSessionState.Loading:
                    Log.Debug("Session Loading");
                    break;

                case TorchSessionState.Loaded:
                    Log.Debug("Session Loaded");
                    MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, OnEntityDamaged);
                    break;

                case TorchSessionState.Unloading:
                    Log.Debug("Session Unloading");
                    break;

                case TorchSessionState.Unloaded:
                    Log.Debug("Session Unloaded");
                    break;
            }
        }

        #endregion

        #region Damage Handling

        private void OnEntityDamaged(object target, ref MyDamageInformation info)
        {
            if (target == null || info.AttackerId == 0)
                return;

            try
            {
                if (info.IsDeformation)
                    return;

                var attackerEntity = MyAPIGateway.Entities.GetEntityById(info.AttackerId);
                if (attackerEntity == null)
                {
                    Log.Info("Attacker entity is null, skipping.");
                    return;
                }

                var slimBlock = target as IMySlimBlock;
                var cubeBlock = slimBlock != null ? slimBlock.FatBlock : null;

                if (cubeBlock == null || !IsSupportedBlock(cubeBlock))
                    return;

                // Check if the damaged block belongs to an NPC faction
                long ownerId = cubeBlock.OwnerId;
                if (ownerId != 0)
                {
                    var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerId);
                    if (faction == null || !faction.IsEveryoneNpc())
                        return;
                }

                ulong steamId = GetAttackerSteamId(attackerEntity);
                if (steamId == 0)
                    return;

                // Calculate the damage to apply (capped at 50)
                double damageToApply = Math.Min(info.Amount, 5000) / 100;

                // Create damage log entry
                var damageLog = new
                {
                    steam_id = steamId.ToString(),
                    total_damage = damageToApply
                };

                // Enqueue damage log entry (thread-safe)
                lock (_queueLock)
                {
                    _damageLogQueue.Enqueue(damageLog);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Exception in OnEntityDamaged: " + ex.Message);
                Log.Error("Stack Trace: " + ex.StackTrace);
            }
        }

        private bool IsSupportedBlock(IMyCubeBlock block)
        {
            string typeIdString = block.BlockDefinition.TypeIdString;
            Log.Debug("Checking block type: " + typeIdString);

            return _pointBlockTypes.Contains(typeIdString);
        }

        private ulong GetAttackerSteamId(IMyEntity attackerEntity)
        {
            ulong steamId = 0;
            long ownerId = 0;

            if (attackerEntity is MyCharacter character)
            {
                Log.Debug("Attacker is character: " + character.DisplayNameText);
                var player = MyAPIGateway.Players.GetPlayerControllingEntity(character);
                if (player != null)
                    steamId = player.SteamUserId;
            }
            else if (attackerEntity is IMyCubeGrid grid)
            {
                ownerId = grid.BigOwners.Count > 0 ? grid.BigOwners[0] : 0;
                steamId = MyAPIGateway.Players.TryGetSteamId(ownerId);
            }
            else if (attackerEntity is IMyCubeBlock cubeBlock)
            {
                ownerId = cubeBlock.OwnerId;
                steamId = MyAPIGateway.Players.TryGetSteamId(ownerId);
            }
            else if (attackerEntity is IMyGunBaseUser gunUser)
            {
                ownerId = gunUser.OwnerId;
                steamId = MyAPIGateway.Players.TryGetSteamId(ownerId);
            }
            else
            {
                Log.Debug("Attacker entity type not recognized.");
            }

            return steamId;
        }

        private async Task SendDamageLogsBatchAsync()
        {
            List<object> batch;
            lock (_queueLock)
            {
                batch = new List<object>(_damageLogQueue);
                _damageLogQueue.Clear();
            }

            if (batch.Count == 0)
                return;

            try
            {
                string json = JsonConvert.SerializeObject(batch);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Send POST request to the server
                HttpResponseMessage response = await HttpClient.PostAsync("http://localhost:3000/api/damage_logs", content);

                if (response.IsSuccessStatusCode)
                {
                    Log.Info("Damage logs batch sent successfully. Count: " + batch.Count);
                }
                else
                {
                    Log.Info("Failed to send damage logs batch. Status Code: " + response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Exception while sending damage logs batch: " + ex.Message);
                Log.Error("Stack Trace: " + ex.StackTrace);
            }
        }

        #endregion

        #region Custom Update

        private void CustomUpdate()
        {
            // Custom update logic (called every simulation frame)
            PatchHelpers.PatchUpdates();
        }

        #endregion
    }
}
