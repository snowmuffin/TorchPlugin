using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        private static readonly HttpClient httpClient = new HttpClient();

        // Helper Methods
        private void Respond(string message) => Context?.Respond(message);
        private static string Format(bool value) => value ? "Yes" : "No";

        private bool IsValidItemName(string itemName) =>
            System.Text.RegularExpressions.Regex.IsMatch(itemName, @"^\w+$");

        private MyPhysicalItemDefinition FindItemDefinition(string itemName)
        {
            Type type = itemName.StartsWith("ingot_", StringComparison.OrdinalIgnoreCase) ? typeof(MyObjectBuilder_Ingot) :
                        itemName.StartsWith("ore_", StringComparison.OrdinalIgnoreCase) ? typeof(MyObjectBuilder_Ore) :
                        typeof(MyObjectBuilder_Component);

            if (type != null)
            {
                // "ingot_" 및 "ore_" 접두사 제거 후 첫 문자를 대문자로 변환
                string normalizedItemName = itemName.Replace("ingot_", "").Replace("ore_", "");
                if (!string.IsNullOrEmpty(normalizedItemName))
                {
                    normalizedItemName = char.ToUpper(normalizedItemName[0]) + normalizedItemName.Substring(1);
                }

                MyDefinitionId definitionId = new MyDefinitionId(type, normalizedItemName);
                if (MyDefinitionManager.Static.TryGetDefinition(definitionId, out MyDefinitionBase definition)
                    && definition is MyPhysicalItemDefinition physicalItemDefinition)
                {
                    return physicalItemDefinition;
                }
            }
            return null;
        }

        // Command Methods

        [Command("cmd help", "Se_web: Help")]
        [Permission(MyPromoteLevel.None)]
        public void Help() => RespondWithHelp();

        private void RespondWithHelp()
        {
            var commands = new List<string>
            {
                "!cmd help - Show this help message.",
                "!cmd info - Prints the current configuration settings.",
                "!cmd enable - Enables the plugin.",
                "!cmd disable - Disables the plugin.",
                "!cmd downloaditem <itemname> [quantity] - Downloads an item from online storage to your inventory.",
                "!cmd uploaditem <itemname> [quantity] - Uploads an item from your inventory to online storage.",
                "!cmd listitems - Lists items available in your inventory for upload."
            };
            Respond("Se_web commands:\n" + string.Join("\n", commands));
        }
        [Command("cmd userdata", "Fetches and displays the user's data from online storage")]
        [Permission(MyPromoteLevel.None)]
        public async Task GetUserData()
        {
            var player = Context.Player;
            if (player == null)
            {
                Respond("This command can only be used by a player.");
                return;
            }

            var steamId = player.SteamUserId;
            var requestPayload = new { steamId = steamId.ToString() };
            var message = new StringContent(JsonConvert.SerializeObject(requestPayload), Encoding.UTF8, "application/json");

            try
            {
                HttpResponseMessage response = await httpClient.PostAsync("http://localhost:3000/api/auth/getUserData", message);

                if (!response.IsSuccessStatusCode)
                {
                    Respond($"Failed to retrieve user data. Status Code: {response.StatusCode}, Reason: {response.ReasonPhrase}");
                    return;
                }

                var jsonResponse = JObject.Parse(await response.Content.ReadAsStringAsync());
                if (jsonResponse["status"].ToObject<int>() != 200)
                {
                    Respond("User data does not exist or authentication failed.");
                    return;
                }

                var userData = jsonResponse["userData"];
                Respond($"User Data for Steam ID {steamId}:\n" +
                        $"- Name: {userData["name"]}\n" +
                        $"- Level: {userData["level"]}\n" +
                        $"- sek_coin: {userData["sek_coin"]}\n" +
                        $"- Inventory Count: {userData["inventory_count"]}");
            }
            catch (Exception ex)
            {
                Respond($"An error occurred while retrieving user data: {ex.Message}");
            }
        }
        [Command("cmd info", "Se_web: Prints the current settings")]
        [Permission(MyPromoteLevel.None)]
        public void Info() => Respond($"{Plugin.PluginName} plugin is enabled: {Format(Config.Enabled)}");

        [Command("cmd enable", "Se_web: Enables the plugin")]
        [Permission(MyPromoteLevel.Admin)]
        public void Enable()
        {
            Config.Enabled = true;
            Info();
        }

        [Command("cmd disable", "Se_web: Disables the plugin")]
        [Permission(MyPromoteLevel.Admin)]
        public void Disable()
        {
            Config.Enabled = false;
            Info();
        }

        [Command("cmd downloaditem", "Downloads the specified item from online storage to your inventory")]
        [Permission(MyPromoteLevel.None)]
        public async Task DownloadItem(string itemName, int quantity = 1)
        {
            if (!ValidateCommand(itemName, quantity)) return;
            var player = Context.Player;
            var steamId = player.SteamUserId;

            var itemDefinition = FindItemDefinition(itemName);
            if (itemDefinition == null)
            {
                Respond($"Item '{itemName}' not found in game definitions.");
                return;
            }

            var inventory = player.Character?.GetInventory() as IMyInventory;
            if (inventory == null)
            {
                Respond("Could not access your inventory.");
                return;
            }

            var amount = (VRage.MyFixedPoint)quantity;
            if (!inventory.CanItemsBeAdded(amount, itemDefinition.Id))
            {
                Respond("Not enough space in your inventory.");
                return;
            }

            var downloadCall = new { steamid = steamId.ToString(), itemName, quantity };
            var message = new StringContent(JsonConvert.SerializeObject(downloadCall), Encoding.UTF8, "application/json");

            try
            {
                HttpResponseMessage response = await httpClient.PostAsync("http://localhost:3000/api/resources/download", message);

                if (!response.IsSuccessStatusCode)
                {
                    Respond($"Failed to download item. Status Code: {response.StatusCode}, Reason: {response.ReasonPhrase}");
                    return;
                }

                var jsonObject = JObject.Parse(await response.Content.ReadAsStringAsync());
                var responseMessage = (string)jsonObject["message"];

                if (!(bool)jsonObject["exist"])
                {
                    Respond(responseMessage);
                    return;
                }

                float availableQuantity = (float)jsonObject["quantity"];
                if (availableQuantity < quantity)
                {
                    Respond(responseMessage);
                    return;
                }

                var content = (MyObjectBuilder_PhysicalObject)MyObjectBuilderSerializer.CreateNewObject(itemDefinition.Id);
                inventory.AddItems(amount, content);
                Respond(responseMessage);
            }
            catch (Exception ex)
            {
                Respond($"An error occurred while accessing the database: {ex.Message}");
            }
        }


        [Command("cmd uploaditem", "Uploads the specified item from your inventory to online storage")]
        [Permission(MyPromoteLevel.None)]
        public async Task UploadItem(string itemName, int quantity = 1)
        {
            if (!ValidateCommand(itemName, quantity)) return;
            var player = Context.Player;
            var steamId = player.SteamUserId;

            var itemDefinition = FindItemDefinition(itemName);
            if (itemDefinition == null)
            {
                Respond($"Item '{itemName}' not found in game definitions.");
                return;
            }

            var inventory = player.Character?.GetInventory() as IMyInventory;
            if (inventory == null)
            {
                Respond("Could not access your inventory.");
                return;
            }

            var amount = (VRage.MyFixedPoint)quantity;
            var content = (MyObjectBuilder_PhysicalObject)MyObjectBuilderSerializer.CreateNewObject(itemDefinition.Id);

            if (!inventory.ContainItems(amount, content))
            {
                Respond($"You do not have {quantity}x '{itemName}' in your inventory.");
                return;
            }

            var uploadCall = new { steamid = steamId.ToString(), itemName, quantity };
            var message = new StringContent(JsonConvert.SerializeObject(uploadCall), Encoding.UTF8, "application/json");

            try
            {
                HttpResponseMessage response = await httpClient.PostAsync("http://localhost:3000/api/resources/upload", message);

                if (!response.IsSuccessStatusCode)
                {
                    Respond($"Failed to upload item. Status Code: {response.StatusCode}, Reason: {response.ReasonPhrase}");
                    return;
                }

                inventory.RemoveItemsOfType(amount, content);
                Respond($"Successfully uploaded {quantity}x '{itemName}' from your inventory to online storage.");
            }
            catch (Exception ex)
            {
                Respond($"An error occurred while accessing the database: {ex.Message}");
            }
        }

        [Command("cmd listitems", "Lists items available in your inventory for upload")]
        [Permission(MyPromoteLevel.None)]
        public void ListItems()
        {
            var player = Context.Player;
            if (player == null)
            {
                Respond("This command can only be run by a player.");
                return;
            }

            var inventory = player.Character?.GetInventory() as IMyInventory;
            if (inventory == null)
            {
                Respond("Could not access your inventory.");
                return;
            }

            var items = new List<string>();
            foreach (var item in inventory.GetItems())
            {
                var itemName = item.Content.SubtypeName;
                var itemType = item.Content.TypeId.ToString();
                var prefix = itemType.Contains("Ingot") ? "ingot_" : itemType.Contains("Ore") ? "ore_" : string.Empty;
                items.Add($"{prefix}{itemName} (x{item.Amount})");
            }

            Respond(items.Count == 0 ? "Your inventory is empty." : "Items available in your inventory:\n" + string.Join("\n", items));
        }

        // Validates the item name and quantity
        private bool ValidateCommand(string itemName, int quantity)
        {
            if (Context.Player == null)
            {
                Respond("This command can only be used by a player.");
                return false;
            }
            if (string.IsNullOrEmpty(itemName) || !IsValidItemName(itemName))
            {
                Respond("Invalid or missing item name.");
                return false;
            }
            if (quantity <= 0)
            {
                Respond("Quantity must be a positive integer.");
                return false;
            }
            return true;
        }
    }
}

