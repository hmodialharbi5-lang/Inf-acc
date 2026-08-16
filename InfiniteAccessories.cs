using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using Microsoft.Xna.Framework;

namespace InfiniteAccessories
{
    [ApiVersion(2, 1)]
    public sealed class InfiniteAccessoriesPlugin : TerrariaPlugin
    {
        public override string Name => "Infinite Accessories";
        public override string Author => "OpenAI";
        public override Version Version => new Version(1, 0, 0);
        public override string Description =>
            "Server-side extra accessory storage with duplicate accessory effect application.";

        private readonly Dictionary<string, PlayerData> players =
            new(StringComparer.OrdinalIgnoreCase);

        private string DataDirectory =>
            Path.Combine(TShock.SavePath, "InfiniteAccessories");

        private const int DefaultMaxExtra = 20;
        private int MaxExtraAccessories = DefaultMaxExtra;

        public InfiniteAccessoriesPlugin(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            Directory.CreateDirectory(DataDirectory);

            ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);

            Commands.ChatCommands.Add(new Command(
                "infaccessories.use",
                InfAccCommand,
                "infacc")
            {
                HelpText = "Manage your server-side extra accessories."
            });

            TShock.Log.ConsoleInfo(
                "Infinite Accessories v1.0.0 loaded. Commands: /infacc help");
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
                TSPlayer ts = TShock.Players[i];

                if (ts?.Active != true || ts.IsLoggedIn != true)
                    continue;

                var data = GetData(ts);

                if (!data.Enabled || data.Accessories.Count == 0)
                    continue;

                ApplyExtraAccessories(ts.TPlayer, data);
            }
        }

        private void ApplyExtraAccessories(
            Player player,
            PlayerData data)
        {
            int applied = 0;

            foreach (int itemId in data.Accessories)
            {
                if (applied >= MaxExtraAccessories)
                    break;

                if (itemId <= 0 || itemId >= ItemLoaderItemCount())
                    continue;

                var item = new Item();
                item.SetDefaults(itemId);

                if (!item.accessory)
                    continue;

                player.UpdateAccessory(item, false);

                applied++;
            }
        }

        private static int ItemLoaderItemCount()
        {
            return Main.maxItemTypes;
        }

        private void InfAccCommand(CommandArgs args)
        {
            if (args.Parameters.Count == 0 ||
                args.Parameters[0].Equals(
                    "help",
                    StringComparison.OrdinalIgnoreCase))
            {
                args.Player.SendInfoMessage(
                    "/infacc on|off | add <item id> | remove <slot> | list | clear | max");

                args.Player.SendInfoMessage(
                    $"Extra accessory limit: {MaxExtraAccessories}");

                return;
            }

            var data = GetData(args.Player);
            string sub = args.Parameters[0].ToLowerInvariant();

            switch (sub)
            {
                case "on":
                    data.Enabled = true;
                    Save(data);

                    args.Player.SendSuccessMessage(
                        "Infinite Accessories enabled.");

                    break;

                case "off":
                    data.Enabled = false;
                    Save(data);

                    args.Player.SendSuccessMessage(
                        "Infinite Accessories disabled.");

                    break;

                case "add":
                    AddAccessory(args, data);
                    break;

                case "remove":
                    RemoveAccessory(args, data);
                    break;

                case "clear":
                    data.Accessories.Clear();
                    Save(data);

                    args.Player.SendSuccessMessage(
                        "Extra accessory storage cleared.");

                    break;

                case "list":
                    ListAccessories(args.Player, data);
                    break;

                case "max":
                    args.Player.SendInfoMessage(
                        $"Server extra accessory limit: {MaxExtraAccessories}");

                    break;

                default:
                    args.Player.SendErrorMessage(
                        "Unknown subcommand. Use /infacc help.");

                    break;
            }
        }

        private void AddAccessory(
            CommandArgs args,
            PlayerData data)
        {
            if (args.Parameters.Count < 2 ||
                !int.TryParse(
                    args.Parameters[1],
                    out int itemId))
            {
                args.Player.SendErrorMessage(
                    "Usage: /infacc add <item id>");

                args.Player.SendInfoMessage(
                    "Use Terraria item IDs. Example: /infacc add 554");

                return;
            }

            if (itemId <= 0 || itemId >= Main.maxItemTypes)
            {
                args.Player.SendErrorMessage(
                    "That item ID is outside the valid Terraria item range.");

                return;
            }

            var item = new Item();
            item.SetDefaults(itemId);

            if (!item.accessory)
            {
                args.Player.SendErrorMessage(
                    $"{item.Name} (ID {itemId}) is not an accessory.");

                return;
            }

            if (data.Accessories.Count >= MaxExtraAccessories)
            {
                args.Player.SendErrorMessage(
                    $"You reached the extra accessory limit ({MaxExtraAccessories}).");

                return;
            }

            data.Accessories.Add(itemId);
            Save(data);

            args.Player.SendSuccessMessage(
                $"Added {item.Name} (ID {itemId}) to extra accessories. " +
                $"You now have {data.Accessories.Count}/{MaxExtraAccessories} extra.");
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

            data.Accessories.RemoveAt(index);
           
