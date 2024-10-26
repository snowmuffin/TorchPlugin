using System;
using System.Collections.Generic;
using MySql.Data.MySqlClient;
using Sandbox.Definitions;
using Shared.Config;
using Shared.Plugin;
using Torch.Commands;
using Torch.Commands.Permissions;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;

namespace TorchPlugin
{
    public class Commands : CommandModule
    {
        // Fields
        private static IPluginConfig Config => Common.Config;
        private const string ConnectionString = "Server=localhost;Database=mydatabase;Uid=root;Pwd=my-secret-pw;";

        // Helper Methods
        private void Respond(string message)
        {
            Context?.Respond(message);
        }

        private static string Format(bool value) => value ? "Yes" : "No";

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

        private bool IsValidItemName(string itemName)
        {
            // Ensure the item name contains only letters, numbers, and underscores
            return System.Text.RegularExpressions.Regex.IsMatch(itemName, @"^\w+$");
        }

        private MyPhysicalItemDefinition FindItemDefinition(string itemName)
        {
            return MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Component), itemName)) ??
                   MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Ingot), itemName)) ??
                   MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Ore), itemName)) ??
                   MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), itemName)) ??
                   MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_PhysicalObject), itemName));
        }

        // Command Methods

        [Command("cmd help", "Se_web: Help")]
        [Permission(MyPromoteLevel.None)]
        public void Help()
        {
            RespondWithHelp();
        }

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
            Respond("  !cmd downloaditem <itemname> [quantity]");
            Respond("    Downloads the specified item from online storage to your inventory");
            Respond("  !cmd mydata");
            Respond("    Shows your damage data");
            Respond("  !cmd top [limit]");
            Respond("    Shows the top players based on damage");
        }

        [Command("cmd info", "Se_web: Prints the current settings")]
        [Permission(MyPromoteLevel.None)]
        public void Info()
        {
            RespondWithInfo();
        }

        private void RespondWithInfo()
        {
            var config = Plugin.Instance.Config;
            Respond($"{Plugin.PluginName} plugin is enabled: {Format(config.Enabled)}");
            // TODO: Respond with your plugin settings
            // For example:
            // Respond($"custom_setting: {Format(config.CustomSetting)}");
        }

        [Command("cmd enable", "Se_web: Enables the plugin")]
        [Permission(MyPromoteLevel.Admin)]
        public void Enable()
        {
            Config.Enabled = true;
            RespondWithInfo();
        }

        [Command("cmd disable", "Se_web: Disables the plugin")]
        [Permission(MyPromoteLevel.Admin)]
        public void Disable()
        {
            Config.Enabled = false;
            RespondWithInfo();
        }

        [Command("cmd downloaditem", "Downloads the specified item from online storage to your inventory")]
        [Permission(MyPromoteLevel.None)] // Set permission level as needed
        public void DownloadItem(string itemName, int quantity = 1)
        {
            if (Context.Player == null)
            {
                Respond("This command can only be used by a player.");
                return;
            }

            if (string.IsNullOrEmpty(itemName))
            {
                Respond("You must specify an item name.");
                return;
            }

            if (!IsValidItemName(itemName))
            {
                Respond("Invalid item name.");
                return;
            }

            if (quantity <= 0)
            {
                Respond("Quantity must be a positive integer.");
                return;
            }

            var player = Context.Player;
            var inventory = player.Character?.GetInventory() as IMyInventory;

            if (inventory == null)
            {
                Respond("Could not access your inventory.");
                return;
            }

            // Retrieve player's Steam ID
            var steamId = player.SteamUserId;

            try
            {
                using (var connection = new MySqlConnection(ConnectionString))
                {
                    connection.Open();

                    // Prepare the query to get the item quantity from online_storage
                    string query = $"SELECT `{itemName}` FROM online_storage WHERE steam_id = @steamId";

                    using (var cmd = new MySqlCommand(query, connection))
                    {
                        cmd.Parameters.AddWithValue("@steamId", steamId);

                        var result = cmd.ExecuteScalar();

                        if (result == null)
                        {
                            Respond("You have no items in your online storage.");
                            return;
                        }

                        float availableQuantity = Convert.ToSingle(result);

                        if (availableQuantity <= 0)
                        {
                            Respond($"You do not have any '{itemName}' in your online storage.");
                            return;
                        }

                        if (availableQuantity < quantity)
                        {
                            Respond($"You only have {availableQuantity}x '{itemName}' in your online storage.");
                            return;
                        }

                        // Find the item definition
                        var itemDefinition = FindItemDefinition(itemName);

                        if (itemDefinition == null)
                        {
                            Respond($"Item '{itemName}' not found in game definitions.");
                            return;
                        }

                        var amount = (VRage.MyFixedPoint)quantity;
                        var content = (MyObjectBuilder_PhysicalObject)MyObjectBuilderSerializer.CreateNewObject(itemDefinition.Id);

                        // Add the item to the inventory
                        if (inventory.CanItemsBeAdded(amount, itemDefinition.Id))
                        {
                            inventory.AddItems(amount, content);

                            // Update the online_storage table to decrement the item quantity
                            string updateQuery = $"UPDATE online_storage SET `{itemName}` = `{itemName}` - @quantity WHERE steam_id = @steamId";

                            using (var updateCmd = new MySqlCommand(updateQuery, connection))
                            {
                                updateCmd.Parameters.AddWithValue("@quantity", quantity);
                                updateCmd.Parameters.AddWithValue("@steamId", steamId);

                                int rowsAffected = updateCmd.ExecuteNonQuery();

                                if (rowsAffected > 0)
                                {
                                    Respond($"Downloaded {quantity}x {itemDefinition.DisplayNameText} to your inventory.");
                                }
                                else
                                {
                                    Respond("Failed to update your online storage.");
                                }
                            }
                        }
                        else
                        {
                            Respond("Not enough space in your inventory.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Respond("An error occurred while accessing the database.");
                // Optionally log the exception for debugging purposes
                // Log.Error($"DownloadItem Exception: {ex}");
            }
        }

        [Command("cmd mydata", "Se_web: Shows the player's damage data")]
        [Permission(MyPromoteLevel.None)]
        public void MyData()
        {
            var player = Context.Player;
            if (player == null)
            {
                Respond("This command can only be run by a player.");
                return;
            }

            ulong steamId = player.SteamUserId;
            RespondWithPlayerData(steamId);
        }

        private void RespondWithPlayerData(ulong steamId)
        {
            try
            {
                using (var connection = new MySqlConnection(ConnectionString))
                {
                    connection.Open();

                    string query = "SELECT total_damage FROM damage_logs WHERE steam_id = @steamId";
                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@steamId", steamId);

                        var result = command.ExecuteScalar();

                        if (result != null)
                        {
                            float totalDamage = Convert.ToSingle(result);
                            Respond($"Steam ID: {steamId}, Total Damage: {totalDamage}");
                        }
                        else
                        {
                            Respond($"No data found for Steam ID: {steamId}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Respond($"Error retrieving data: {ex.Message}");
                // Optionally log the exception for debugging purposes
            }
        }

        [Command("cmd top", "Se_web: Shows the top players based on damage")]
        [Permission(MyPromoteLevel.None)]
        public void TopPlayers(int limit = 10)
        {
            RespondWithTopPlayers(limit);
        }

        private void RespondWithTopPlayers(int limit)
        {
            try
            {
                ulong currentPlayerSteamId = Context.Player?.SteamUserId ?? 0;
                using (var connection = new MySqlConnection(ConnectionString))
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
                                // 현재 플레이어와 동일한 Steam ID인지 확인
                                if (steamId == currentPlayerSteamId)
                                {
                                    // 본인인 경우 색상 강조 (HTML 태그를 이용하여 강조)
                                    topPlayers.Add($"{rank}. *** Damage: {totalDamage} ***");
                                }
                                else
                                {
                                    topPlayers.Add($"{rank}. Damage: {totalDamage}");
                                }
                                rank++;
                            }

                            if (topPlayers.Count > 0)
                            {
                                Respond("Top Players:");
                                foreach (var playerInfo in topPlayers)
                                {
                                    Respond(playerInfo);
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
                // Optionally log the exception for debugging purposes
            }
        }
    }
}
