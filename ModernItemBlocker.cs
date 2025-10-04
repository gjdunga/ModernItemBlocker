using Oxide.Game.Rust.Cui;
using Oxide.Game.Rust.Libraries;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("ModernItemBlocker", "gjdunga", "3.0.0")]
    [Description("Modern Item Blocker - Blocks items, clothing and ammunition temporarily after a wipe or permanently until removed. Provides admin commands via chat, RCON and UI with permissions.")]
    public class ModernItemBlocker : RustPlugin
    {
        // File name for storing the plugin configuration externally in the data directory
        private const string DataConfigName = "ModernItemBlockerConfig";
        #region Configuration
        private Configuration _config;
        public class Configuration
        {
            [JsonProperty("Block Duration (Hours) after Wipe")]
            public int BlockDurationHours = 30;

            [JsonProperty("Permanent Blocked Items")]
            public List<string> PermanentBlockedItems = new List<string>();

            [JsonProperty("Permanent Blocked Clothes")]
            public List<string> PermanentBlockedClothes = new List<string>();

            [JsonProperty("Permanent Blocked Ammo")]
            public List<string> PermanentBlockedAmmo = new List<string>();

            [JsonProperty("Timed Blocked Items")]
            public List<string> TimedBlockedItems = new List<string>();

            [JsonProperty("Timed Blocked Clothes")]
            public List<string> TimedBlockedClothes = new List<string>();

            [JsonProperty("Timed Blocked Ammo")]
            public List<string> TimedBlockedAmmo = new List<string>();

            [JsonProperty("Bypass Permission")] public string BypassPermission = "modernblocker.bypass";
            [JsonProperty("Admin Permission")] public string AdminPermission = "modernblocker.admin";

            [JsonProperty("Chat Prefix")] public string ChatPrefix = "[ModernBlocker]";
            [JsonProperty("Chat Prefix Color")] public string ChatPrefixColor = "#f44253";

            public static Configuration DefaultConfig()
            {
                return new Configuration
                {
                    BlockDurationHours = 30,
                    TimedBlockedItems = new List<string>
                    {
                        
                    },
                    TimedBlockedClothes = new List<string>
                    {
                        
                    },
                    TimedBlockedAmmo = new List<string>
                    {
                        
                    },
                    PermanentBlockedItems = new List<string>(),
                    PermanentBlockedClothes = new List<string>(),
                    PermanentBlockedAmmo = new List<string>()
                };
            }
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Generating default configuration");
            _config = Configuration.DefaultConfig();
            // Save the default configuration to our external data file immediately
            SaveDataConfig();
        }

        protected override void LoadConfig()
        {
            // Override default config loading to use our external data config
            LoadDataConfig();
        }

        protected override void SaveConfig()
        {
            // Override SaveConfig to also persist to our external data file
            SaveDataConfig();
        }

        /// <summary>
        /// Load configuration from external data file. If no data file exists, create one from default configuration.
        /// </summary>
        private void LoadDataConfig()
        {
            try
            {
                _config = Interface.Oxide.DataFileSystem.ReadObject<Configuration>(DataConfigName);
                if (_config == null)
                    throw new Exception("Data config was null");
            }
            catch
            {
                PrintWarning("Data configuration file not found or invalid. Creating a new one.");
                _config = Configuration.DefaultConfig();
                SaveDataConfig();
            }
        }

        /// <summary>
        /// Save the current configuration to the external data file
        /// </summary>
        private void SaveDataConfig()
        {
            Interface.Oxide.DataFileSystem.WriteObject(DataConfigName, _config);
        }
        #endregion

        #region Data & State
        private DateTime _blockEnd;
        private HashSet<string> _timedItems;
        private HashSet<string> _timedClothes;
        private HashSet<string> _timedAmmo;
        private HashSet<string> _permanentItems;
        private HashSet<string> _permanentClothes;
        private HashSet<string> _permanentAmmo;

        private void BuildHashSets()
        {
            _timedItems = new HashSet<string>(_config.TimedBlockedItems, StringComparer.OrdinalIgnoreCase);
            _timedClothes = new HashSet<string>(_config.TimedBlockedClothes, StringComparer.OrdinalIgnoreCase);
            _timedAmmo = new HashSet<string>(_config.TimedBlockedAmmo, StringComparer.OrdinalIgnoreCase);
            _permanentItems = new HashSet<string>(_config.PermanentBlockedItems, StringComparer.OrdinalIgnoreCase);
            _permanentClothes = new HashSet<string>(_config.PermanentBlockedClothes, StringComparer.OrdinalIgnoreCase);
            _permanentAmmo = new HashSet<string>(_config.PermanentBlockedAmmo, StringComparer.OrdinalIgnoreCase);
        }

        private bool InTimedBlock => (DateTime.UtcNow < _blockEnd);
        #endregion

        #region Initialization
        private void Init()
        {
            permission.RegisterPermission(_config.BypassPermission, this);
            permission.RegisterPermission(_config.AdminPermission, this);
        }

        private void OnServerInitialized()
        {
            // Calculate block end based on the save created time and configured duration
            if (SaveRestore.SaveCreatedTime != DateTime.MinValue)
                _blockEnd = SaveRestore.SaveCreatedTime.AddHours(_config.BlockDurationHours);
            else
                _blockEnd = DateTime.UtcNow.AddHours(_config.BlockDurationHours);
            BuildHashSets();
        }

        private void OnNewSave(string filename)
        {
            // When a new save (wipe) is detected, recalculate the block end
            _blockEnd = DateTime.UtcNow.AddHours(_config.BlockDurationHours);
            SaveConfig();
            Puts($"Wipe detected. Block end set to {_blockEnd:dd.MM.yyyy HH:mm:ss}");
        }
        #endregion

        #region Localization
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["ItemBlocked"] = "Sorry, You cannot use this item.",
                ["ClothBlocked"] = "Sorry, You cannot wear this clothing item.",
                ["AmmoBlocked"] = "Sorry, You cannot use this ammunition.",
                ["TimedSuffix"] = "\n{0}d {1:00}:{2:00}:{3:00} until unblock.",
                ["PermanentSuffix"] = "\nThis item is permanently blocked until removed.",
                ["NotAllowed"] = "Sorry, You do not have permission to use this command.",
                ["InvalidArgs"] = "Invalid syntax. Use /modernblocker help for details.",
                ["Added"] = "Added {0} to {1} {2} list.",
                ["Removed"] = "Removed {0} from {1} {2} list.",
                ["ListHeader"] = "Permanent Items: {0}\nPermanent Clothes: {1}\nPermanent Ammo: {2}\nTimed Items: {3}\nTimed Clothes: {4}\nTimed Ammo: {5}",
                ["Usage"] = "Usage:\n /modernblocker list - list all blocked items\n /modernblocker add <permanent|timed> <item|cloth|ammo> <name> - add an entry\n /modernblocker remove <permanent|timed> <item|cloth|ammo> <name> - remove an entry\n /modernblocker reload - reload configuration from disk"
            }, this);
        }

        private string Msg(string key, BasePlayer player = null) => lang.GetMessage(key, this, player?.UserIDString);
        #endregion

        #region Permissions Helpers
        private bool IsBypass(BasePlayer player) => permission.UserHasPermission(player.UserIDString, _config.BypassPermission);
        private bool IsAdmin(IPlayer player) => player.IsAdmin || permission.UserHasPermission(player.Id, _config.AdminPermission);
        #endregion

        #region Blocking Logic
        private object CanEquipItem(PlayerInventory inventory, Item item)
        {
            var player = inventory.GetComponent<BasePlayer>();
            if (player == null || IsNPC(player) || InDuel(player)) return null;
            if (IsBypass(player)) return null;
            var name = item.info.displayName.english;
            var shortName = item.info.shortname;
            // Permanent blocks
            if (_permanentItems.Contains(name) || _permanentItems.Contains(shortName))
            {
                SendBlockMessage(player, "ItemBlocked", true);
                return false;
            }
            // Timed blocks
            if (InTimedBlock && (_timedItems.Contains(name) || _timedItems.Contains(shortName)))
            {
                SendBlockMessage(player, "ItemBlocked", false);
                return false;
            }
            return null;
        }

        private object CanWearItem(PlayerInventory inventory, Item item)
        {
            var player = inventory.GetComponent<BasePlayer>();
            if (player == null || IsNPC(player) || InDuel(player)) return null;
            if (IsBypass(player)) return null;
            var name = item.info.displayName.english;
            var shortName = item.info.shortname;
            if (_permanentClothes.Contains(name) || _permanentClothes.Contains(shortName))
            {
                SendBlockMessage(player, "ClothBlocked", true);
                return false;
            }
            if (InTimedBlock && (_timedClothes.Contains(name) || _timedClothes.Contains(shortName)))
            {
                SendBlockMessage(player, "ClothBlocked", false);
                return false;
            }
            return null;
        }

        private object OnReloadMagazine(BasePlayer player, BaseProjectile projectile)
        {
            if (player == null || IsNPC(player) || InDuel(player)) return null;
            if (IsBypass(player)) return null;
            // Determine ammo type currently being loaded or used
            Item ammoItem = null;
            var ammoId = projectile.primaryMagazine.ammoType.itemid;
            var current = player.inventory.FindItemIDs(ammoId);
            if (current != null && current.Count > 0)
            {
                ammoItem = current[0];
            }
            else
            {
                var list = Pool.GetList<Item>();
                player.inventory.FindAmmo(list, projectile.primaryMagazine.definition.ammoTypes);
                if (list.Count > 0) ammoItem = list[0];
                Pool.FreeList(ref list);
            }
            if (ammoItem == null) return null;
            var name = ammoItem.info.displayName.english;
            var shortName = ammoItem.info.shortname;
            if (_permanentAmmo.Contains(name) || _permanentAmmo.Contains(shortName))
            {
                SendBlockMessage(player, "AmmoBlocked", true);
                return false;
            }
            if (InTimedBlock && (_timedAmmo.Contains(name) || _timedAmmo.Contains(shortName)))
            {
                SendBlockMessage(player, "AmmoBlocked", false);
                return false;
            }
            return null;
        }

        // Helpers to determine NPC or Duel contexts
        [PluginReference] private Plugin Duel;
        private bool IsNPC(BasePlayer player) => player is NPCPlayer || !(player.userID >= 76560000000000000L || player.userID <= 0L);
        private bool InDuel(BasePlayer player)
        {
            if (Duel == null) return false;
            return Duel.Call<bool>("IsPlayerOnActiveDuel", player);
        }

        #endregion

        #region Messaging
        private void SendBlockMessage(BasePlayer player, string messageKey, bool permanent)
        {
            var prefix = $"<color={_config.ChatPrefixColor}>{_config.ChatPrefix}</color> ";
            string message = Msg(messageKey, player);
            if (permanent)
            {
                message += Msg("PermanentSuffix", player);
            }
            else
            {
                var timeLeft = _blockEnd - DateTime.UtcNow;
                message += string.Format(Msg("TimedSuffix", player), timeLeft.Days, timeLeft.Hours, timeLeft.Minutes, timeLeft.Seconds);
            }
            PrintToChat(player, prefix + message);
        }
        #endregion

        #region Commands
        // Chat command – accessible via in-game chat
        [ChatCommand("modernblocker")]
        private void ChatCommand(BasePlayer player, string command, string[] args)
        {
            ExecuteCommand(player.IPlayer, args);
        }

        // Console command – accessible via RCON, server console or F1 client console
        [ConsoleCommand("modernblocker")]
        private void ConsoleCommand(ConsoleSystem.Arg arg)
        {
            var caller = arg.Player();
            IPlayer iplayer = caller?.IPlayer ?? covalence.Players.FindPlayerById(arg.Connection?.userid.ToString());
            ExecuteCommand(iplayer, arg.Args);
        }

        private void ExecuteCommand(IPlayer caller, string[] args)
        {
            // If caller is null (e.g., RCON without player), treat as admin
            bool isAdmin = caller == null || IsAdmin(caller);
            if (!isAdmin)
            {
                caller?.Reply(Msg("NotAllowed"));
                return;
            }
            if (args == null || args.Length == 0)
            {
                caller?.Reply(Msg("Usage"));
                return;
            }
            var action = args[0].ToLower();
            switch (action)
            {
                case "help":
                case "usage":
                    caller?.Reply(Msg("Usage"));
                    break;
                case "list":
                    SendLists(caller);
                    break;
                case "add":
                case "remove":
                    if (args.Length < 4)
                    {
                        caller?.Reply(Msg("InvalidArgs"));
                        return;
                    }
                    var type = args[1].ToLower(); // permanent or timed
                    var category = args[2].ToLower(); // item, cloth, ammo
                    var name = string.Join(" ", args.Skip(3));
                    if (ModifyList(action == "add", type, category, name))
                    {
                        var status = action == "add" ? Msg("Added") : Msg("Removed");
                        caller?.Reply(string.Format(status, name, type, category));
                        SaveConfig();
                        BuildHashSets();
                    }
                    else
                    {
                        caller?.Reply(Msg("InvalidArgs"));
                    }
                    break;
                case "reload":
                    LoadDataConfig();
                    BuildHashSets();
                    caller?.Reply("ModernItemBlocker configuration reloaded from file.");
                    break;
                default:
                    caller?.Reply(Msg("Usage"));
                    break;
            }
        }

        private void SendLists(IPlayer caller)
        {
            string permItems = _config.PermanentBlockedItems.Count > 0 ? string.Join(", ", _config.PermanentBlockedItems) : "<none>";
            string permClothes = _config.PermanentBlockedClothes.Count > 0 ? string.Join(", ", _config.PermanentBlockedClothes) : "<none>";
            string permAmmo = _config.PermanentBlockedAmmo.Count > 0 ? string.Join(", ", _config.PermanentBlockedAmmo) : "<none>";
            string timedItems = _config.TimedBlockedItems.Count > 0 ? string.Join(", ", _config.TimedBlockedItems) : "<none>";
            string timedClothes = _config.TimedBlockedClothes.Count > 0 ? string.Join(", ", _config.TimedBlockedClothes) : "<none>";
            string timedAmmo = _config.TimedBlockedAmmo.Count > 0 ? string.Join(", ", _config.TimedBlockedAmmo) : "<none>";
            caller?.Reply(string.Format(Msg("ListHeader"), permItems, permClothes, permAmmo, timedItems, timedClothes, timedAmmo));
        }

        private bool ModifyList(bool add, string type, string category, string name)
        {
            List<string> targetList = null;
            switch (type)
            {
                case "permanent":
                    switch (category)
                    {
                        case "item":
                        case "items":
                            targetList = _config.PermanentBlockedItems;
                            break;
                        case "cloth":
                        case "clothes":
                        case "clothing":
                            targetList = _config.PermanentBlockedClothes;
                            break;
                        case "ammo":
                            targetList = _config.PermanentBlockedAmmo;
                            break;
                    }
                    break;
                case "timed":
                    switch (category)
                    {
                        case "item":
                        case "items":
                            targetList = _config.TimedBlockedItems;
                            break;
                        case "cloth":
                        case "clothes":
                        case "clothing":
                            targetList = _config.TimedBlockedClothes;
                            break;
                        case "ammo":
                            targetList = _config.TimedBlockedAmmo;
                            break;
                    }
                    break;
            }
            if (targetList == null)
                return false;
            if (add)
            {
                if (!targetList.Contains(name, StringComparer.OrdinalIgnoreCase))
                    targetList.Add(name);
            }
            else
            {
                targetList.RemoveAll(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
            return true;
        }
        #endregion
    }
}
