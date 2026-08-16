using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;

namespace InfiniteAccessories
{
    [ApiVersion(2, 1)]
    public sealed class InfiniteAccessoriesPlugin : TerrariaPlugin
    {
        public override string Name => "Infinite Accessories";
        public override string Author => "OpenAI";
        public override Version Version => new Version(1, 2, 0);
        public override string Description => "Server-side extra accessory storage with duplicate support.";

        private readonly Dictionary<string, PlayerData> players =
            new Dictionary<string, PlayerData>(StringComparer.OrdinalIgnoreCase);

        private const int MaxExtraAccessories = 20;

        private static readonly MethodInfo? UpdateAccessoryMethod =
            typeof(Player).GetMethod(
                "UpdateAccessory",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(Item), typeof(bool) },
                null);

        private string DataDirectory =>
            Path.Combine(TShock.SavePath, "InfiniteAccessories");

        public InfiniteAccessoriesPlugin(Main game) : base(game) { }

        public override void Initialize()
        {
            Directory.CreateDirectory(DataDirectory);
            ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);

            Commands.ChatCommands.Add(
                new Command("infaccessories.use", InfAccCommand, "infacc")
                {
                    HelpText = "Manage your extra accessories."
                });

            TShock.Log.ConsoleInfo("Infinite Accessories v1.2.0 loaded.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (PlayerData data in players.Values)
                    Save(data);

                ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);

                Commands.ChatCommands.RemoveAll(
                    c => c.Name.Equals("infacc", StringComparison.OrdinalIgnoreCase));
            }

            base.Dispose(disposing);
        }

        private void OnGameUpdate(EventArgs args)
        {
            for (int i = 0; i < TShock.Players.Length; i++)
            {
                TSPlayer ts = TShock.Players[i];

                if (ts == null || !ts.Active || !ts.IsLoggedIn)
                    continue;

                PlayerData data = GetData(ts);

                if (!data.Enabled || data.Accessories.Count == 0)
                    continue;

                ApplyExtraAccessories(ts.TPlayer, data);
            }
        }

        private void ApplyExtraAccessories(Player player, PlayerData data)
        {
            if (UpdateAccessoryMethod == null)
                return;

            int count = 0;

            foreach (int itemId in data.Accessories)
            {
                if (count >= MaxExtraAccessories)
                    break;

                if (itemId <= 0 || itemId >= ItemID.Count)
                    continue;

                Item accessory = new Item();
                accessory.SetDefaults(itemId);

                if (!accessory.accessory)
                    continue;

                try
                {
                    UpdateAccessoryMethod.Invoke(
                        player,
                        new object[] { accessory, false });

                    count++;
                }
                catch (Exception ex)
                {
                    TShock.Log.Error(
                        "Infinite Accessories: failed to apply " +
                        accessory.Name + ": " + ex);
                }
            }
        }

        private void InfAccCommand(CommandArgs args)
        {
            if (args.Parameters.Count == 0 ||
                args.Parameters[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                SendHelp(args.Player);
                return;
            }

            PlayerData data = GetData(args.Player);
            string command = args.Parameters[0].ToLowerInvariant();

            switch (command)
            {
                case "on":
                    data.Enabled = true;
                    Save(data);
                    args.Player.SendSuccessMessage("Infinite Accessories is now ON.");
                    break;

                case "off":
                    data.Enabled = false;
                    Save(data);
                    args.Player.SendSuccessMessage("Infinite Accessories is now OFF.");
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
                        "Maximum extra accessories: " + MaxExtraAccessories);
                    break;

                default:
                    args.Player.SendErrorMessage(
                        "Unknown command. Use /infacc help.");
                    break;
            }
        }

        private void SendHelp(TSPlayer player)
        {
            player.SendInfoMessage("=== Infinite Accessories ===");
            player.SendInfoMessage("/infacc on");
            player.SendInfoMessage("/infacc off");
            player.SendInfoMessage("/infacc add");
            player.SendInfoMessage("/infacc list");
            player.SendInfoMessage("/infacc remove <slot>");
            player.SendInfoMessage("/infacc clear");
            player.SendInfoMessage("/infacc max");
            player.SendInfoMessage("Hold an accessory before using /infacc add.");
            player.SendInfoMessage("Duplicates are allowed.");
        }

        private void AddHeldAccessory(CommandArgs args, PlayerData data)
        {
            Player player = args.Player.TPlayer;
            int slot = player.selectedItem;

            if (slot < 0 || slot >= player.inventory.Length)
            {
                args.Player.SendErrorMessage("Your selected inventory slot is invalid.");
                return;
            }

            Item held = player.inventory[slot];

            if (held == null || held.IsAir || held.type <= 0)
            {
                args.Player.SendErrorMessage("Hold an accessory first.");
                return;
            }

            if (!held.accessory)
            {
                args.Player.SendErrorMessage(
                    "The item you are holding is not an accessory.");
                return;
            }

            if (data.Accessories.Count >= MaxExtraAccessories)
            {
                args.Player.SendErrorMessage(
                    "You reached the maximum of " + MaxExtraAccessories + " extra accessories.");
                return;
            }

            string name = held.Name;
            int id = held.type;

            held.stack--;

            if (held.stack <= 0)
                held.TurnToAir();

            data.Accessories.Add(id);
            Save(data);

            args.Player.SendSuccessMessage(
                "Added " + name + " to your extra accessories.");

            args.Player.SendInfoMessage(
                "Extra accessories: " +
                data.Accessories.Count + "/" + MaxExtraAccessories);
        }

        private void RemoveAccessory(CommandArgs args, PlayerData data)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Usage: /infacc remove <slot>");
                return;
            }

            if (!int.TryParse(args.Parameters[1], out int slot))
            {
                args.Player.SendErrorMessage("Slot must be a number.");
                return;
            }

            int index = slot - 1;

            if (index < 0 || index >= data.Accessories.Count)
            {
                args.Player.SendErrorMessage("That slot does not exist.");
                return;
            }

            int id = data.Accessories[index];

            if (id <= 0 || id >= ItemID.Count)
            {
                data.Accessories.RemoveAt(index);
                Save(data);
                args.Player.SendErrorMessage("The stored accessory was invalid.");
                return;
            }

            Item item = new Item();
            item.SetDefaults(id);

            args.Player.GiveItem(id, 1, 0);

            data.Accessories.RemoveAt(index);
            Save(data);

            args.Player.SendSuccessMessage(
                "Removed " + item.Name + " and returned it to you.");
        }

        private void ListAccessories(TSPlayer player, PlayerData data)
        {
            if (data.Accessories.Count == 0)
            {
                player.SendInfoMessage("You have no extra accessories.");
                return;
            }

            player.SendInfoMessage(
                "Extra accessories: " +
                data.Accessories.Count + "/" + MaxExtraAccessories);

            player.SendInfoMessage("Enabled: " + data.Enabled);

            for (int i = 0; i < data.Accessories.Count; i++)
            {
                int id = data.Accessories[i];

                if (id <= 0 || id >= ItemID.Count)
                {
                    player.SendInfoMessage((i + 1) + ". Invalid accessory");
                    continue;
                }

                Item item = new Item();
                item.SetDefaults(id);

                player.SendInfoMessage((i + 1) + ". " + item.Name);
            }
        }

        private void ClearAccessories(TSPlayer player, PlayerData data)
        {
            if (data.Accessories.Count == 0)
            {
                player.SendInfoMessage("You have no extra accessories.");
                return;
            }

            int amount = data.Accessories.Count;

            foreach (int id in data.Accessories)
            {
                if (id > 0 && id < ItemID.Count)
                    player.GiveItem(id, 1, 0);
            }

            data.Accessories.Clear();
            Save(data);

            player.SendSuccessMessage(
                "Cleared " + amount + " extra accessories.");
        }

        private PlayerData GetData(TSPlayer player)
        {
            string key = string.IsNullOrWhiteSpace(player.UUID)
                ? player.Name
                : player.UUID;

            if (!players.TryGetValue(key, out PlayerData? data))
            {
                data = Load(key);
                data.Key = key;
                data.PlayerName = player.Name;
                players[key] = data;
            }
            else
            {
                data.PlayerName = player.Name;
            }

            return data;
        }

        private PlayerData Load(string key)
        {
            string path = Path.Combine(
                DataDirectory,
                Sanitize(key) + ".json");

            try
            {
                if (File.Exists(path))
                {
                    PlayerData? data =
                        JsonSerializer.Deserialize<PlayerData>(
                            File.ReadAllText(path));

                    if (data != null)
                    {
                        data.Accessories ??= new List<int>();
                        return data;
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error(
                    "Infinite Accessories load error: " + ex);
            }

            return new PlayerData
            {
                Key = key,
                PlayerName = "",
                Enabled = false,
                Accessories = new List<int>()
            };
        }

        private void Save(PlayerData data)
        {
            if (string.IsNullOrWhiteSpace(data.Key))
                return;

            try
            {
                Directory.CreateDirectory(DataDirectory);

                string path = Path.Combine(
                    DataDirectory,
                    Sanitize(data.Key) + ".json");

                string json = JsonSerializer.Serialize(
                    data,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                TShock.Log.Error(
                    "Infinite Accessories save error: " + ex);
            }
        }

        private static string Sanitize(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');

            return value;
        }
    }

    public sealed class PlayerData
    {
        public string Key { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public bool Enabled { get; set; }
        public List<int> Accessories { get; set; } = new List<int>();
    }
}
