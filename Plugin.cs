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
        public List<string> PointBlock = new List<string> { "MyObjectBuilder_Beacon", "MyObjectBuilder_InteriorTurret", "MyObjectBuilder_LargeMissileTurret", "MyObjectBuilder_SmallMissileLauncher", "MyObjectBuilder_SmallMissileLauncherReload", "MyObjectBuilder_LargeGatlingTurret", "MyObjectBuilder_SmallGatlingGun" };
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
        private void OnEntityDamaged(object target, ref MyDamageInformation info)
        {
            if (target == null || info.AttackerId == 0)
            {
                return;
            }
            try
            {
                if (info.IsDeformation)
                {
                    return;
                }
                IMyEntity entityById = MyAPIGateway.Entities.GetEntityById(info.AttackerId);
                if (entityById == null)
                {
                    Log.Info("OnEntityDamaged: Attacker entity is null, returning.");
                    return;
                }
                IMySlimBlock val = (IMySlimBlock)((target is IMySlimBlock) ? target : null);
                IMyCubeGrid val2 = (IMyCubeGrid)((target is IMyCubeGrid) ? target : null);
                IMyCubeBlock val3 = ((val != null) ? val.FatBlock : null);
                if (val3 == null || !IsSupportedBlock(val3))
                {
                    return;
                }
                long num = 0L;
                num = ((IMyCubeBlock)val3).OwnerId;
                if (num != 0)
                {
                    IMyFaction val4 = MyAPIGateway.Session.Factions.TryGetPlayerFaction(num);
                    Log.Info("OnEntityDamaged: Faction: " + ((val4 != null) ? val4.Name : null));
                    if (val4 == null || !val4.IsEveryoneNpc())
                    {
                        return;
                    }
                }
                ulong num2 = 0uL;
                long num3 = 0L;
                MyCharacter val5 = (MyCharacter)(object)((entityById is MyCharacter) ? entityById : null);
                MyCharacter val6 = val5;
                if (val6 != null)
                {
                    Log.Info("OnEntityDamaged: Attacker is character. Display Name: " + ((MyEntity)val6).DisplayNameText);
                    if (val6.ControllerInfo != null)
                    {
                        List<IMyPlayer> list = new List<IMyPlayer>();
                        MyAPIGateway.Players.GetPlayers(list, (Func<IMyPlayer, bool>)null);
                        foreach (IMyPlayer item in list)
                        {
                            if (item.Character == val6)
                            {
                                num2 = item.SteamUserId;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    IMyCubeGrid val7 = (IMyCubeGrid)(object)((entityById is IMyCubeGrid) ? entityById : null);
                    if (val7 != null)
                    {
                        num3 = ((val7.BigOwners.Count > 0) ? val7.BigOwners[0] : 0);
                        num2 = MyAPIGateway.Players.TryGetSteamId(num3);
                    }
                    else
                    {
                        IMyCubeBlock val8 = (IMyCubeBlock)(object)((entityById is IMyCubeBlock) ? entityById : null);
                        if (val8 != null)
                        {
                            num3 = ((IMyCubeBlock)val8).OwnerId;
                            num2 = MyAPIGateway.Players.TryGetSteamId(num3);
                        }
                        else
                        {
                            IMyGunBaseUser val9 = (IMyGunBaseUser)(object)((entityById is IMyGunBaseUser) ? entityById : null);
                            if (val9 == null)
                            {
                                return;
                            }
                            num3 = val9.OwnerId;
                            num2 = MyAPIGateway.Players.TryGetSteamId(num3);
                        }
                    }
                }
                
                double damageToApply = Math.Min(info.Amount, 5000)/100;
                var damageLog = new
                {
                    steam_id = num2.ToString(),   // 예시로, 공격자의 ID를 사용
                    total_damage = damageToApply   // 피해량
                };

                // 큐에 추가 (스레드 안전을 위해 lock 사용)
                lock (queueLock)
                {
                    damageLogQueue.Enqueue(damageLog);
                }
            }
            catch (Exception ex)
            {
                Log.Info("Exception: " + ex.Message);
                Log.Info("Stack Trace: " + ex.StackTrace);
                if (ex.InnerException != null)
                {
                    Log.Info("Inner Exception: " + ex.InnerException.Message);
                    Log.Info("Inner Stack Trace: " + ex.InnerException.StackTrace);
                }
            }
        }

        private bool IsSupportedBlock(IMyCubeBlock block)
        {
            string SubtypeName = block.BlockDefinition.SubtypeName;
            string SubtypeId = block.BlockDefinition.SubtypeId;


            string TypeIdString = block.BlockDefinition.TypeIdString;
            string TypeIdStringAttribute = block.BlockDefinition.TypeIdStringAttribute;
            string SubtypeIdAttribute = block.BlockDefinition.SubtypeIdAttribute;

            Log.Info($"SubtypeName:{SubtypeName}\nSubtypeId:{SubtypeId}\nTypeIdString:{TypeIdString}\nTypeIdStringAttribute:{TypeIdStringAttribute}\nSubtypeIdAttribute:{SubtypeIdAttribute}\n");

            if (!PointBlock.Contains(TypeIdString))
            {

                return false;
            }


            return true;
        }
        private async Task SendDamageLogsBatchAsync()
        {
            
            List<object> batch;
            lock (queueLock)
            {
                batch = new List<object>(damageLogQueue);
                damageLogQueue.Clear();
            }

            if (batch.Count == 0)
            {
                return;
            }

            try
            {
                string json = JsonConvert.SerializeObject(batch);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Nginx 서버를 통해 Node.js 서버로 비동기 POST 요청 보내기
                HttpResponseMessage response = await httpClient.PostAsync("http://localhost:3000/api/damage_logs", content);

                if (response.IsSuccessStatusCode)
                {
                    Log.Info($"SendDamageLogsBatchAsync: Damage logs batch successfully sent to Node.js server. Count: {batch.Count}");
                }
                else
                {
                    Log.Info($"SendDamageLogsBatchAsync: Failed to send damage logs batch. Status Code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Log.Info("Exception while sending damage logs batch: " + ex.Message);
                Log.Info("Stack Trace: " + ex.StackTrace);
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
                    MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, new BeforeDamageApplied(OnEntityDamaged));
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
                Task.Run(() => SendDamageLogsBatchAsync());
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