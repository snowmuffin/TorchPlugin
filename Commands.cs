using MySql.Data.MySqlClient;
using Sandbox.Definitions;
using Shared.Config;
using Shared.Plugin;
using System;
using System.Collections.Generic;
using Torch.Commands;
using Torch.Commands.Permissions;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

namespace TorchPlugin
{
    public class Commands : CommandModule
    {
        private static IPluginConfig Config => Common.Config;

        private void Respond(string message)
        {
            Context?.Respond(message);
        }

        // TODO: Replace cmd with the name of your chat command
        // TODO: Implement subcommands as needed
        private void RespondWithHelp()
        {
            Respond("Se_web commands:");
            Respond("  !cmd help");
            Respond("  !cmd info");
            Respond("    Prints the current configuration settings.");
            Respond("  !cmd enable");
            Respond("    Enables the plugin");
            Respond("  !cmd disable");
            Respond("    Disables the plugin");
            Respond("  !cmd subcmd <name> <value>");
            Respond("    TODO Your subcommand");
        }

        private void RespondWithInfo()
        {
            var config = Plugin.Instance.Config;
            Respond($"{Plugin.PluginName} plugin is enabled: {Format(config.Enabled)}");
            // TODO: Respond with your plugin settings
            // For example:
            //Respond($"custom_setting: {Format(config.CustomSetting)}");
        }
        public string connectionString = "Server=localhost;Database=mydatabase;Uid=root;Pwd=my-secret-pw;";
        // Custom formatters
        private void RespondWithPlayerData(ulong steamId)
        {
            try
            {
                using (var connection = new MySqlConnection(Plugin.Instance.ConnectionString))
                {
                    connection.Open();

                    string query = "SELECT total_damage FROM damage_logs WHERE steam_id = @steamid";
                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@steamid", steamId);

                        using (var reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                float totalDamage = reader.GetFloat("total_damage");
                                Respond($"Steam ID: {steamId}, Total Damage: {totalDamage}");
                            }
                            else
                            {
                                Respond($"No data found for Steam ID: {steamId}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Respond($"Error retrieving data: {ex.Message}");
            }
        }
 
        [Command("cmd mydata", "Se_web: Shows the player's damage data")]
        [Permission(MyPromoteLevel.None)]
        public void MyData()
        {
            // 플레이어의 Steam ID 가져오기
            var player = Context.Player;
            if (player == null)
            {
                Respond("Command can only be run by a player.");
                return;
            }

            ulong steamId = player.SteamUserId;
            RespondWithPlayerData(steamId);
        }
        private void RespondWithTopPlayers(int limit = 10)
        {
            try
            {
                using (var connection = new MySqlConnection(Plugin.Instance.ConnectionString))
                {
                    connection.Open();

                    string query = $"SELECT steam_id, total_damage FROM damage_logs ORDER BY total_damage DESC LIMIT @limit";
                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@limit", limit);

                        using (var reader = command.ExecuteReader())
                        {
                            List<string> topPlayers = new List<string>();
                            int rank = 1;

                            while (reader.Read())
                            {
                                ulong steamId = reader.GetUInt64("steam_id");
                                float totalDamage = reader.GetFloat("total_damage");
                                topPlayers.Add($"{rank}. Steam ID: {steamId}, Damage: {totalDamage}");
                                rank++;
                            }

                            if (topPlayers.Count > 0)
                            {
                                Respond("Top Players:");
                                foreach (var player in topPlayers)
                                {
                                    Respond(player);
                                }
                            }
                            else
                            {
                                Respond("No data found.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Respond($"Error retrieving top players: {ex.Message}");
            }
        }
        private static string Format(bool value) => value ? "Yes" : "No";

        // Custom parsers

        private static bool TryParseBool(string text, out bool result)
        {
            switch (text.ToLower())
            {
                case "1":
                case "on":
                case "yes":
                case "y":
                case "true":
                case "t":
                    result = true;
                    return true;

                case "0":
                case "off":
                case "no":
                case "n":
                case "false":
                case "f":
                    result = false;
                    return true;
            }

            result = false;
            return false;
        }

        [Command("cmd top", "Se_web: Shows the top players based on damage")]
        [Permission(MyPromoteLevel.None)]
        public void TopPlayers(int limit = 10)
        {
            RespondWithTopPlayers(limit);
        }
        // ReSharper disable once UnusedMember.Global

        [Command("cmd help", "Se_web: Help")]
        [Permission(MyPromoteLevel.None)]
        public void Help()
        {
            RespondWithHelp();
        }



        // ReSharper disable once UnusedMember.Global
        [Command("cmd enable", "Se_web: Enables the plugin")]
        [Permission(MyPromoteLevel.Admin)]
        public void Enable()
        {
            Config.Enabled = true;
            RespondWithInfo();
        }

        // ReSharper disable once UnusedMember.Global
        [Command("cmd disable", "Se_web: Disables the plugin")]
        [Permission(MyPromoteLevel.Admin)]
        public void Disable()
        {
            Config.Enabled = false;
            RespondWithInfo();
        }

    
      
    }
}