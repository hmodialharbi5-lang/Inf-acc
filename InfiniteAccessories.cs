using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace InfiniteAccessories
{
    [ApiVersion(2, 1)]
    public sealed class InfiniteAccessoriesPlugin : TerrariaPlugin
    {
        public override string Name => "Infinite Accessories";
        public override string Author => "OpenAI";
        public override Version Version => new Version(1, 1, 0);
        public override string Description =>
            "Server-side extra accessories with duplicate accessory support.";

        private readonly Dictionary<string, PlayerData> players =
            new(StringComparer.OrdinalIgnoreCase);

        private string DataDirectory =>
            Path.Combine(TShock.SavePath, "InfiniteAccessories");

        private const int DefaultMaxExtraAccessories = 20;

        public InfiniteAccessoriesPlugin(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            Directory.CreateDirectory(DataDirectory);

            ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);

            Commands.ChatCommands.Add(
                new Command(
                    "infaccessories.use",
                    InfAccCommand,
                    "infacc")
                {
                    HelpText = "Manage your extra accessories."
                });

            TShock.Log.ConsoleInfo(
                "Infinite Accessories v1.1.0 loaded.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var data in players.Values)
                    Save(data);

                ServerApi.Hooks.GameUpdate.Deregister(
                    this,
                    OnGameUpdate);

                Commands.ChatCommands.RemoveAll(c =>
                    c.Name.Equals(
                        "infacc",
                        StringComparison.OrdinalIgnoreCase));
            }

            base.Dispose(disposing);
        }

        private void OnGameUpdate(EventArgs args)
        {
            for (int i = 0; i < Main.maxPlayers; i++)
            {
                TSPlayer tsPlayer = TShock.Players[i];

                if (tsPlayer == null ||
                    !tsPlayer.Active ||
                    !tsPlayer.IsLoggedIn)
                    continue;

                PlayerData data = GetData(tsPlayer);

                if (!data.Enabled ||
                    data.Accessories.Count == 0)
                    continue;

                ApplyExtraAccessories(
                    tsPlayer.TPlayer,
                    data);
            }
        }

        private void ApplyExtraAccessories(
            Player player,
            PlayerData data)
        {
            int maximum =
                Math.Min(
                    data.Accessories.Count,
                    DefaultMaxExtraAccessories);

            for (int i = 0; i < maximum; i++)
            {
                int itemId = data.Accessories[i];

                if (itemId <= 0 ||
                    itemId >= Main.maxItemTypes)
                    continue;

                Item accessory = new Item();
                accessory.SetDefaults(itemId);

                if (!accessory.accessory)
                    continue;

                /*
                 * Use Terraria's own accessory update routine.
                 *
                 * This means the accessory itself decides what
                 * stats/effects it applies instead of us maintaining
                 * a giant hard-coded accessory list.
                 */
                player.UpdateAccessory(
                    accessory,
                    false);
            }
        }

        private void InfAccCommand(CommandArgs args)
        {
            if (args.Parameters.Count == 0 ||
                args.Parameters[0].Equals(
                    "help",
                    StringComparison.OrdinalIgnoreCase))
            {
                SendHelp(args.Player);
                return;
            }

            PlayerData data = GetData(args.Player);

            string command =
                args.Parameters[0].ToLowerInvariant();

            switch (command)
            {
                case "on":
                    data.Enabled = true;
                    Save(data);

                    args.Player.SendSuccessMessage(
                        "Infinite Accessories is now ON.");

                    break;

                case "off":
                    data.Enabled = false;
                    Save(data);

                    args.Player.SendSuccessMessage(
                        "Infinite Accessories is now OFF.");

                    break;

                case "add":
                    AddHeldAccessory(args, data);
                    break;

                case "remove":
                    RemoveAccessory(args, data);
                    break;

                case "list":
                    ListAccessories(args.Player, data);
                    break;

                case "clear":
                    ClearAccessories(args.Player, data);
                    break;

                case "max":
                    args.Player.SendInfoMessage(
                        $"Maximum extra accessories: {DefaultMaxExtraAccessories}");

                    break;

                default:
                    args.Player.SendErrorMessage(
                        "Unknown command. Use /infacc help.");

                    break;
            }
        }

        private void SendHelp(TSPlayer player)
        {
            player.SendInfoMessage(
                "Infinite Accessories commands:");

            player.SendInfoMessage(
                "/infacc on - Enable extra accessory effects.");

            player.SendInfoMessage(
                "/infacc off - Disable extra accessory effects.");

            player.SendInfoMessage(
                "/infacc add - Add the accessory you are holding.");

            player.SendInfoMessage(
                "/infacc list - Show your extra accessories.");

            player.SendInfoMessage(
                "/infacc remove <slot> - Remove an extra accessory.");

            player.SendInfoMessage(
                "/infacc clear - Remove all extra accessories.");

            player.SendInfoMessage(
                "/infacc max - Show the maximum.");

            player.SendInfoMessage(
                "Duplicates are allowed.");
        }

        private void AddHeldAccessory(
            CommandArgs args,
            PlayerData data)
        {
            Player player = args.Player.TPlayer;

            int selectedSlot = player.selectedItem;

            if (selectedSlot < 0 ||
                selectedSlot >= player.inventory.Length)
            {
                args.Player.SendErrorMessage(
                    "Could not find the item you are holding.");

                return;
            }

            Item heldItem = player.inventory[selectedSlot];

            if (heldItem == null ||
                heldItem.IsAir ||
                heldItem.type <= 0)
            {
                args.Player.SendErrorMessage(
                    "Hold an accessory in your hand first.");

                return;
            }

            if (!heldItem.accessory)
            {
                args.Player.SendErrorMessage(
                    $"{heldItem.Name} is not an accessory.");

                return;
            }

            if (data.Accessories.Count >=
                DefaultMaxExtraAccessories)
            {
                args.Player.SendErrorMessage(
                    $"You reached the maximum of {DefaultMaxExtraAccessories} extra accessories.");

                return;
            }

            int itemId = heldItem.type;
            string itemName = heldItem.Name;

            /*
             * Take ONE copy from the item the player is holding.
             *
             * This prevents /infacc add from creating free
             * accessories out of nowhere.
             */
            heldItem.stack--;

            if (heldItem.stack <= 0)
            {
                player.inventory[selectedSlot] = new Item();
            }
            else
            {
                player.inventory[selectedSlot] = heldItem;
            }

            data.Accessories.Add(itemId);

            Save(data);

            args.Player.SendSuccessMessage(
                $"Added {itemName} to your extra accessories.");

            args.Player.SendInfoMessage(
                $"Extra accessories: {data.Accessories.Count}/{DefaultMaxExtraAccessories}");
        }

        private void RemoveAccessory(
            CommandArgs args,
            PlayerData data)
        {
            if (args.Parameters.Count < 2 ||
                !int.TryParse(
                    args.Parameters[1],
                    out int slot))
            {
                args.Player.SendErrorMessage(
                    "Usage: /infacc remove <slot>");

                return;
            }

            int index = slot - 1;

            if (index < 0 ||
                index >= data.Accessories.Count)
            {
                args.Player.SendErrorMessage(
                    "That extra accessory slot does not exist.");

                return;
            }

            int itemId = data.Accessories[index];

            if (itemId <= 0 ||
                itemId >= Main.maxItemTypes)
            {
                args.Player.SendErrorMessage(
                    "The stored accessory is invalid.");

                data.Accessories.RemoveAt(index);
                Save(data);

                return;
            }

            Item item = new Item();
            item.SetDefaults(itemId);

            string itemName = item.Name;

            /*
             * Give the removed accessory back to the player.
             */
            args.Player.GiveItem(
                itemId,
                1,
                0);

            data.Accessories.RemoveAt(index);

            Save(data);

            args.Player.SendSuccessMessage(
                $"Removed {itemName} and returned it to you.");
        }

        private void ListAccessories(
            TSPlayer player,
            PlayerData data)
        {
            if (data.Accessories.Count == 0)
            {
                player.SendInfoMessage(
