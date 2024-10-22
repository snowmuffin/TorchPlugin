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

        // Custom formatters

        private static string Format(bool value) => value ? "Yes" : "No";

        // Custom parsers
        private string GetConnectionString()
        {
            var config = Plugin.Instance.Config;

            string server = config.DatabaseServer;
            string database = config.DatabaseName;
            string user = config.DatabaseUser;
            string password = config.DatabasePassword;

            return $"Server={server};Database={database};Uid={user};Pwd={password};";
        }
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

        // ReSharper disable once UnusedMember.Global

        [Command("cmd help", "Se_web: Help")]
        [Permission(MyPromoteLevel.None)]
        public void Help()
        {
            RespondWithHelp();
        }

        // ReSharper disable once UnusedMember.Global
        [Command("cmd info", "Se_web: Prints the current settings")]
        [Permission(MyPromoteLevel.None)]
        public void Info()
        {
            RespondWithInfo();
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

        // TODO: Subcommand
        // ReSharper disable once UnusedMember.Global
        [Command("cmd subcmd", "Se_web: TODO: Subcommand")]
        [Permission(MyPromoteLevel.Admin)]
        public void SubCmd(string name, string value)
        {
            // TODO: Process command parameters (for example name and value)

            RespondWithInfo();
        }

        // New command to give item to player's inventory
        [Command("cmd giveitem", "Se_web: Gives the specified item to the player")]
        [Permission(MyPromoteLevel.Admin)]
        public void GiveItem(string itemName, int quantity = 1)
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

            // Find the item definition
            var itemDefinition = MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Component), itemName)) ??
                                 MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Ingot), itemName)) ??
                                 MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Ore), itemName)) ??
                                 MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), itemName)) ??
                                 MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_PhysicalObject), itemName));

            if (itemDefinition == null)
            {
                Respond($"Item '{itemName}' not found.");
                return;
            }

            var amount = (VRage.MyFixedPoint)quantity;
            var content = (MyObjectBuilder_PhysicalObject)MyObjectBuilderSerializer.CreateNewObject(itemDefinition.Id);

            // Add the item to the inventory
            if (inventory.CanItemsBeAdded(amount, itemDefinition.Id))
            {
                inventory.AddItems(amount, content);
                Respond($"Added {quantity}x {itemDefinition.DisplayNameText} to your inventory.");
            }
            else
            {
                Respond("Not enough space in your inventory.");
            }
        }
    }
}