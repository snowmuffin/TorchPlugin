#define USE_HARMONY

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public class Plugin : TorchPluginBase, IWpfPlugin
    {
        private static readonly HttpClient httpClient = new HttpClient();
        public const string PluginName = "Se_web";
        public static Plugin Instance { get; private set; }
        public long Tick { get; private set; }
        private const ushort MessageId = 5363;
        private ConcurrentDictionary<long, Logic> m_cachedBlocks = new ConcurrentDictionary<long, Logic>();

        private static readonly IPluginLogger Logger = new PluginLogger("Se_web");
        public IPluginLogger Log => Logger;

        public PluginConfig Config => _config?.Data;
        private static readonly string ConfigFileName = $"{PluginName}.cfg";

        public UserControl GetControl() => _control ?? (_control = new ConfigView(this));
        private Persistent<PluginConfig> _config;
        private ConfigView _control;
        private TorchSessionManager sessionManager;
        private bool initialized;
        private bool failed;
        private readonly ConcurrentQueue<object> damageLogQueue = new ConcurrentQueue<object>();
        private readonly object queueLock = new object();

        private readonly Commands commands = new Commands();

        private readonly HashSet<string> PointBlock = new HashSet<string>
        {
            "MyObjectBuilder_Beacon",
            "MyObjectBuilder_InteriorTurret",
            "MyObjectBuilder_LargeMissileTurret",
            "MyObjectBuilder_SmallMissileLauncher",
            "MyObjectBuilder_SmallMissileLauncherReload",
            "MyObjectBuilder_LargeGatlingTurret",
            "MyObjectBuilder_SmallGatlingGun"
        };

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public override void Init(ITorchBase torch)
        {
            base.Init(torch);
            SetupConfig();

            sessionManager = torch.Managers.GetManager<TorchSessionManager>();
            sessionManager.SessionStateChanged += SessionStateChanged;

            initialized = true;
        }

        private void SetupConfig()
        {
            var configFile = Path.Combine(StoragePath, "Se_web.cfg");

            try
            {
                _config = Persistent<PluginConfig>.Load(configFile);
            }
            catch (Exception e)
            {
                Log.Info(e.Message);
            }

            if (_config?.Data == null)
            {
                Log.Info("Create Default Config, because none was found!");

                _config = new Persistent<PluginConfig>(configFile, new PluginConfig());
                Save();
            }
        }
        private void OnEntityDamaged(object target, ref MyDamageInformation info)
        {
            if (target == null || info.AttackerId == 0 || info.IsDeformation)
            {
                return;
            }

            try
            {
                IMyEntity entityById = MyAPIGateway.Entities.GetEntityById(info.AttackerId);
                if (entityById == null)
                {
                    return;
                }

                if (!(target is IMySlimBlock slimBlock) || slimBlock.FatBlock == null || !IsSupportedBlock(slimBlock.FatBlock))
                {
                    return;
                }

                long ownerId = slimBlock.FatBlock.OwnerId;
                if (ownerId != 0)
                {
                    IMyFaction faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerId);
                    if (faction == null || !faction.IsEveryoneNpc())
                    {
                        return;
                    }
                }

                ulong steamId = GetSteamIdFromEntity(entityById);
                if (steamId == 0)
                {
                    return;
                }

                double damageToApply = info.Amount / 100;
                var damageLog = new
                {
                    steam_id = steamId.ToString(),
                    damage = damageToApply,
                    server_id = _config.Data.ServerId
                };

                damageLogQueue.Enqueue(damageLog);
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        private ulong GetSteamIdFromEntity(IMyEntity entity)
        {
            switch (entity)
            {
                case MyCharacter character when character.ControllerInfo != null:
                    var players = new List<IMyPlayer>();
                    MyAPIGateway.Players.GetPlayers(players);
                    return players.FirstOrDefault(p => p.Character == character)?.SteamUserId ?? 0;

                case IMyCubeGrid grid:
                    long ownerId = grid.BigOwners.FirstOrDefault();
                    return MyAPIGateway.Players.TryGetSteamId(ownerId);

                case IMyCubeBlock block:
                    return MyAPIGateway.Players.TryGetSteamId(block.OwnerId);

                case IMyGunBaseUser gunBaseUser:
                    return MyAPIGateway.Players.TryGetSteamId(gunBaseUser.OwnerId);

                default:
                    return 0;
            }
        }

        private void LogException(Exception ex)
        {
            Log.Info("Exception: " + ex.Message);
            Log.Info("Stack Trace: " + ex.StackTrace);
            if (ex.InnerException != null)
            {
                Log.Info("Inner Exception: " + ex.InnerException.Message);
                Log.Info("Inner Stack Trace: " + ex.InnerException.StackTrace);
            }
        }
        public void Save()
        {
            try
            {
                _config.Save();
                Log.Info("Configuration Saved.");
            }
            catch (IOException e)
            {
                Log.Info(e, "Configuration failed to save");
            }
        }
        private bool IsSupportedBlock(IMyCubeBlock block)
        {
            return PointBlock.Contains(block.BlockDefinition.TypeIdString);
        }
        private async Task SendDamageLogsBatchAsync()
        {
            if (damageLogQueue.IsEmpty)
            {
                return;
            }

            var batch = new List<object>();
            while (damageLogQueue.TryDequeue(out var log))
            {
                batch.Add(log);
            }

            if (batch.Count == 0)
            {
                return;
            }

            try
            {
                string json = JsonConvert.SerializeObject(batch);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                string apiUrl = $"{Config.ApiBaseUrl}/damage_logs";

                HttpResponseMessage response = await httpClient.PostAsync(apiUrl, content);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Info($"Failed to send damage logs batch. Status Code: {response.StatusCode}");
                    foreach (var log in batch)
                    {
                        damageLogQueue.Enqueue(log); // Re-enqueue failed logs
                    }
                }
                else
                {
                    Log.Info($"Damage logs batch successfully sent. Count: {batch.Count}");
                }
            }
            catch (Exception ex)
            {
                Log.Info("Exception while sending damage logs batch: " + ex.Message);
                foreach (var log in batch)
                {
                    damageLogQueue.Enqueue(log); // Re-enqueue failed logs
                }
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
                    Log.Info("Loaded" + _config.Data.ServerId);
                    MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, new BeforeDamageApplied(OnEntityDamaged));

                    session.Managers.GetManager<IMultiplayerManagerBase>().PlayerJoined += OnPlayerJoined;
                    break;

                case TorchSessionState.Unloading:
                    Log.Debug("Unloading");

                    session.Managers.GetManager<IMultiplayerManagerBase>().PlayerJoined -= OnPlayerJoined;
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
            {
                return;
            }

            try
            {
                CustomUpdate();
                Tick++;
                _ = SendDamageLogsBatchAsync(); // Fire-and-forget without blocking
            }
            catch (Exception e)
            {
                Log.Critical(e, "Update failed");
                failed = true;
            }
        }
        private async Task OnPlayerJoinedAsync(IPlayer player)
        {
            Log.Info($"Player joined: {player.Name} (Steam ID: {player.SteamId})");
            var uploadCall = new
            {
                steamid = player.SteamId.ToString(),
                nickname = player.Name.ToString()
            };

            try
            {
                string json = JsonConvert.SerializeObject(uploadCall);
                var message = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await httpClient.PostAsync($"{((PluginConfig)Config).ApiBaseUrl}/user/updateuserdb", message);
                if (response.IsSuccessStatusCode)
                {
                    Log.Info($"Player registered: {player.Name} (Steam ID: {player.SteamId})");
                }
                else
                {
                    Log.Info($"Failed to register player: {player.Name} (Steam ID: {player.SteamId}), Status Code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Log.Info($"Exception during player registration: {ex.Message}");
            }
        }

        private void OnPlayerJoined(IPlayer player)
        {
            Log.Info($"player joined");
            Task.Run(() => OnPlayerJoinedAsync(player));
        }

        private void CustomUpdate()
        {
            PatchHelpers.PatchUpdates();
        }
    }
}