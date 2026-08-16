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
        public override Version Version => new Version(1, 3, 0);
        public override string Description =>
            "Server-side extra accessory storage with duplicate and modifier support.";

        private readonly Dictionary<string, PlayerData> players =
            new Dictionary<string, PlayerData>(StringComparer.OrdinalIgnoreCase);

        private const int MaxExtraAccessories = 20;

        private static readonly MethodInfo? UpdateAccessoryMethod =
            typeof(Player).GetMethod(
                "UpdateAccessory",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic,
                null,
                new Type[] { typeof(Item), typeof(bool) },
                null);

        private string DataDirectory =>
            Path.Combine(TShock.SavePath, "InfiniteAccessories");

        public InfiniteAccessoriesPlugin(Main game) : base(game)
        {
        }

        public override void Initialize()
        {
            Directory.CreateDirectory(DataDirectory);

            ServerApi.Hooks.GameUpdate.Register(
                this,
                OnGameUpdate);

            Commands.ChatCommands.Add(
                new Command(
                    "infaccessories.use",
                    InfAccCommand,
                    "infacc")
                {
                    HelpText = "Manage your extra accessories."
                });

            TShock.Log.ConsoleInfo(
                "Infinite Accessories v1.3.0 loaded.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (PlayerData data in players.Values)
                    Save(data);

                ServerApi.Hooks.GameUpdate.Deregister(
                    this,
                    OnGameUpdate);

                Commands.ChatCommands.RemoveAll(
                    c => c.Name.Equals(
                        "infacc",
                        StringComparison.OrdinalIgnoreCase));
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

        private void ApplyExtraAccessories(
            Player player,
            PlayerData data)
        {
            if (UpdateAccessoryMethod == null)
                return;

            int count = 0;

            foreach (StoredAccessory stored in data.Accessories)
            {
                if (count >= MaxExtraAccessories)
                    break;

                if (stored.Type <= 0 ||
                    stored.Type >= ItemID.Count)
                    continue;

                Item accessory = new Item();
                accessory.SetDefaults(stored.Type);

                if (!accessory.accessory)
                    continue;

                accessory.Prefix(stored.Prefix);

                try
                {
                    UpdateAccessoryMethod.Invoke(
                        player,
                        new object[]
                        {
                            accessory,
                            false
                        });

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
                        "Maximum extra accessories: " +
                        MaxExtraAccessories);
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
                "=== Infinite Accessories ===");

            player.SendInfoMessage(
                "/infacc on");

            player.SendInfoMessage(
                "/infacc off");

            player.SendInfoMessage(
                "/infacc add");

            player.SendInfoMessage(
                "/infacc list");

            player.SendInfoMessage(
                "/infacc remove <slot>");

            player.SendInfoMessage(
                "/infacc clear");

            player.SendInfoMessage(
                "/infacc max");

            player.SendInfoMessage(
                "Hold an accessory before using /infacc add.");

            player.SendInfoMessage(
                "The accessory's modifier is saved.");

            player.SendInfoMessage(
                "Duplicates with different modifiers are allowed.");
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
                    "Your selected inventory slot is invalid.");
                return;
            }

            Item heldItem =
                player.inventory[selectedSlot];

            if (heldItem == null ||
                heldItem.IsAir ||
                heldItem.type <= 0)
            {
                args.Player.SendErrorMessage(
                    "Hold an accessory first.");
                return;
            }

            if (!heldItem.accessory)
            {
                args.Player.SendErrorMessage(
                    "The item you are holding is not an accessory.");
                return;
            }

            if (data.Accessories.Count >=
                MaxExtraAccessories)
            {
                args.Player.SendErrorMessage(
                    "You reached the maximum of " +
                    MaxExtraAccessories +
                    " extra accessories.");
                return;
            }

            int itemId = heldItem.type;

            int prefix = heldItem.prefix;

            string itemName = heldItem.Name;

            data.Accessories.Add(
                new StoredAccessory
                {
                    Type = itemId,
                    Prefix = prefix
                });

            heldItem.stack--;

            if (heldItem.stack <= 0)
                heldItem.TurnToAir();

            Save(data);

            string modifierName =
                GetPrefixName(prefix);

            if (string.IsNullOrEmpty(modifierName))
            {
                args.Player.SendSuccessMessage(
                    "Added " +
                    itemName +
                    " to your extra accessories.");
            }
            else
            {
                args.Player.SendSuccessMessage(
                    "Added " +
                    modifierName +
                    " " +
                    itemName +
                    " to your extra accessories.");
            }

            args.Player.SendInfoMessage(
                "Extra accessories: " +
                data.Accessories.Count +
                "/" +
                MaxExtraAccessories);
        }

        private void RemoveAccessory(
            CommandArgs args,
            PlayerData data)
        {
            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage(
                    "Usage: /infacc remove <slot>");
                return;
            }

            if (!int.TryParse(
                    args.Parameters[1],
                    out int slot))
            {
                args.Player.SendErrorMessage(
                    "Slot must be a number.");
                return;
            }

            int index = slot - 1;

            if (index < 0 ||
                index >= data.Accessories.Count)
            {
                args.Player.SendErrorMessage(
                    "That slot does not exist.");
                return;
            }

            StoredAccessory stored =
                data.Accessories[index];

            if (stored.Type <= 0 ||
                stored.Type >= ItemID.Count)
            {
                data.Accessories.RemoveAt(index);
                Save(data);

                args.Player.SendErrorMessage(
                    "The stored accessory was invalid.");
                return;
            }

            Item item = new Item();

            item.SetDefaults(
                stored.Type);

            item.Prefix(
                stored.Prefix);

            args.Player.GiveItem(
                stored.Type,
                1,
                stored.Prefix);

            data.Accessories.RemoveAt(index);

            Save(data);

            string modifierName =
                GetPrefixName(stored.Prefix);

            if (string.IsNullOrEmpty(modifierName))
            {
                args.Player.SendSuccessMessage(
                    "Removed " +
                    item.Name +
                    " and returned it to you.");
            }
            else
            {
                args.Player.SendSuccessMessage(
                    "Removed " +
                    modifierName +
                    " " +
                    item.Name +
                    " and returned it to you.");
            }
        }

        private void ListAccessories(
            TSPlayer player,
            PlayerData data)
        {
            if (data.Accessories.Count == 0)
            {
                player.SendInfoMessage(
                    "You have no extra accessories.");
                return;
            }

            player.SendInfoMessage(
                "Extra accessories: " +
                data.Accessories.Count +
                "/" +
                MaxExtraAccessories);

            player.SendInfoMessage(
                "Enabled: " +
                data.Enabled);

            for (int i = 0;
                 i < data.Accessories.Count;
                 i++)
            {
                StoredAccessory stored =
                    data.Accessories[i];

                if (stored.Type <= 0 ||
                    stored.Type >= ItemID.Count)
                {
                    player.SendInfoMessage(
                        (i + 1) +
                        ". Invalid accessory");
                    continue;
                }

                Item item = new Item();

                item.SetDefaults(
                    stored.Type);

                string modifier =
                    GetPrefixName(stored.Prefix);

                if (string.IsNullOrEmpty(modifier))
                {
                    player.SendInfoMessage(
                        (i + 1) +
                        ". " +
                        item.Name);
                }
                else
                {
                    player.SendInfoMessage(
                        (i + 1) +
                        ". " +
                        modifier +
                        " " +
                        item.Name);
                }
            }
        }

        private void ClearAccessories(
            TSPlayer player,
            PlayerData data)
        {
            if (data.Accessories.Count == 0)
            {
                player.SendInfoMessage(
                    "You have no extra accessories.");
                return;
            }

            int amount =
                data.Accessories.Count;

            foreach (StoredAccessory stored
                     in data.Accessories)
            {
                if (stored.Type <= 0 ||
                    stored.Type >= ItemID.Count)
                    continue;

                player.GiveItem(
                    stored.Type,
                    1,
                    stored.Prefix);
            }

            data.Accessories.Clear();

            Save(data);

            player.SendSuccessMessage(
                "Cleared " +
                amount +
                " extra accessories.");

            player.SendInfoMessage(
                "The stored accessories were returned with their modifiers.");
        }

        private static string GetPrefixName(
            int prefix)
        {
            if (prefix <= 0)
                return "";

            try
            {
                Item item = new Item();

                item.SetDefaults(
                    ItemID.WoodenSword);

                item.Prefix(
                    prefix);

                return item.HoverName
                    .Replace(
                        item.Name,
                        "")
                    .Trim();
            }
            catch
            {
                return "";
            }
        }

        private PlayerData GetData(
            TSPlayer player)
        {
            string key;

            if (string.IsNullOrWhiteSpace(
                    player.UUID))
            {
                key = player.Name;
            }
            else
            {
                key = player.UUID;
            }

            if (!players.TryGetValue(
                    key,
                    out PlayerData? data))
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

        private PlayerData Load(
            string key)
        {
            string path =
                Path.Combine(
                    DataDirectory,
                    Sanitize(key) +
                    ".json");

            try
            {
                if (File.Exists(path))
                {
                    PlayerData? loaded =
                        JsonSerializer.Deserialize<PlayerData>(
                            File.ReadAllText(path));

                    if (loaded != null)
                    {
                        loaded.Accessories ??=
                            new List<StoredAccessory>();

                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                TShock.Log.Error(
                    "Infinite Accessories load error: " +
                    ex);
            }

            return new PlayerData
            {
                Key = key,
                PlayerName = "",
                Enabled = false,
                Accessories =
                    new List<StoredAccessory>()
            };
        }

        private void Save(
            PlayerData data)
        {
            if (string.IsNullOrWhiteSpace(
                    data.Key))
                return;

            try
            {
                Directory.CreateDirectory(
                    DataDirectory);

                string path =
                    Path.Combine(
                        DataDirectory,
                        Sanitize(data.Key) +
                        ".json");

                string json =
                    JsonSerializer.Serialize(
                        data,
                        new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });

                File.WriteAllText(
                    path,
                    json);
            }
            catch (Exception ex)
            {
                TShock.Log.Error(
                    "Infinite Accessories save error: " +
                    ex);
            }
        }

        private static string Sanitize(
            string value)
        {
            foreach (char c in
                     Path.GetInvalidFileNameChars())
            {
                value =
                    value.Replace(
                        c,
                        '_');
            }

            return value;
        }
    }

    public sealed class PlayerData
    {
        public string Key { get; set; } = "";

        public string PlayerName { get; set; } = "";

        public bool Enabled { get; set; }

        public List<StoredAccessory> Accessories { get; set; } =
            new List<StoredAccessory>();
    }

    public sealed class StoredAccessory
    {
        public int Type { get; set; }

        public int Prefix { get; set; }
    }
}
