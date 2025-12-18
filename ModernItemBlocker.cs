using Oxide.Game.Rust.Cui;
using Oxide.Game.Rust.Libraries;
using Oxide.Core.Plugins;
using Oxide.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
// Import Rust namespace for new hook parameters (IAmmoContainer)
using Rust;

// Additional namespace for file operations
using System.IO;

// Import Covalence to enable the IPlayer interface and covalence player lookups
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    // Updated author per user request
[Info("ModernItemBlocker", "gjdunga", "3.0.8")]
    [Description("Blocks items, clothing and ammunition temporarily after a wipe or permanently until removed. Provides admin commands via chat, RCON and UI with permissions.")]
    public class ModernItemBlocker : RustPlugin
    {
        // File name for storing the plugin configuration externally in the data directory
        private const string DataConfigName = "ModernItemBlockerConfig";

        // Name of the log file (without extension). Log entries will be written to oxide/logs/{LogFileName}.txt
        private const string LogFileName = "ModernItemBlocker";
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

            // Permissions should follow the plugin name prefix "modernitemblocker" to avoid warnings from Oxide.
            [JsonProperty("Bypass Permission")] public string BypassPermission = "modernitemblocker.bypass";
            [JsonProperty("Admin Permission")] public string AdminPermission = "modernitemblocker.admin";

            [JsonProperty("Chat Prefix")] public string ChatPrefix = "[ModernBlocker]";
            [JsonProperty("Chat Prefix Color")] public string ChatPrefixColor = "#f44253";

            public static Configuration DefaultConfig()
            {
                // Return a configuration with no default items, clothes or ammo blocked.
                return new Configuration
                {
                    BlockDurationHours = 30,
                    TimedBlockedItems = new List<string>(),
                    TimedBlockedClothes = new List<string>(),
                    TimedBlockedAmmo = new List<string>(),
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
            // Ensure permissions are prefixed with the plugin name (lowercase) to avoid Oxide warnings.
            var prefix = Name.ToLowerInvariant();
            if (!_config.BypassPermission.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
            {
                _config.BypassPermission = $"{prefix}.bypass";
                PrintWarning($"Bypass permission name updated to {_config.BypassPermission} to match plugin prefix.");
                SaveConfig();
            }
            if (!_config.AdminPermission.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
            {
                _config.AdminPermission = $"{prefix}.admin";
                PrintWarning($"Admin permission name updated to {_config.AdminPermission} to match plugin prefix.");
                SaveConfig();
            }
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
                ["ItemBlocked"] = "You cannot use this item.",
                ["ClothBlocked"] = "You cannot wear this clothing item.",
                ["AmmoBlocked"] = "You cannot use this ammunition.",
                ["TimedSuffix"] = "\n{0}d {1:00}:{2:00}:{3:00} until unblock.",
                ["PermanentSuffix"] = "\nThis item is permanently blocked until removed.",
                ["NotAllowed"] = "You do not have permission to use this command.",
                ["InvalidArgs"] = "Invalid syntax. Use /modernblocker help for details.",
                ["Added"] = "Added {0} to {1} {2} list.",
                ["Removed"] = "Removed {0} from {1} {2} list.",
                ["ListHeader"] = "Permanent Items: {0}\nPermanent Clothes: {1}\nPermanent Ammo: {2}\nTimed Items: {3}\nTimed Clothes: {4}\nTimed Ammo: {5}",
                ["Usage"] = "Usage:\n /modernblocker list - list all blocked items\n /modernblocker add <permanent|timed> <item|cloth|ammo> <name> - add an entry\n /modernblocker remove <permanent|timed> <item|cloth|ammo> <name> - remove an entry\n /modernblocker reload - reload configuration from disk\n /modernblocker loglist - display the last 20 log entries"
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
                // Log the blocked attempt
                LogBlockAttempt(player, $"{name} ({shortName})", "item");
                return false;
            }
            // Timed blocks
            if (InTimedBlock && (_timedItems.Contains(name) || _timedItems.Contains(shortName)))
            {
                SendBlockMessage(player, "ItemBlocked", false);
                // Log the blocked attempt
                LogBlockAttempt(player, $"{name} ({shortName})", "item");
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
                // Log the blocked attempt
                LogBlockAttempt(player, $"{name} ({shortName})", "clothing");
                return false;
            }
            if (InTimedBlock && (_timedClothes.Contains(name) || _timedClothes.Contains(shortName)))
            {
                SendBlockMessage(player, "ClothBlocked", false);
                // Log the blocked attempt
                LogBlockAttempt(player, $"{name} ({shortName})", "clothing");
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
            // Find the first item in the player's inventory that matches the ammo ID
            var current = Facepunch.Pool.GetList<Item>();
            try
            {
                // New API in recent Rust versions requires passing a List<Item> and the ID
                player.inventory.FindItemsByItemID(current, ammoId);
                if (current.Count > 0)
                {
                    ammoItem = current[0];
                }
                else
                {
                    // Fallback: search using FindAmmo for the types defined in the magazine definition
                    var list = Facepunch.Pool.GetList<Item>();
                    try
                    {
                        player.inventory.FindAmmo(list, projectile.primaryMagazine.definition.ammoTypes);
                        if (list.Count > 0)
                        {
                            ammoItem = list[0];
                        }
                    }
                    finally
                    {
                        Facepunch.Pool.FreeList(ref list);
                    }
                }
            }
            finally
            {
                Facepunch.Pool.FreeList(ref current);
            }
            if (ammoItem == null) return null;
            var name = ammoItem.info.displayName.english;
            var shortName = ammoItem.info.shortname;
            if (_permanentAmmo.Contains(name) || _permanentAmmo.Contains(shortName))
            {
                SendBlockMessage(player, "AmmoBlocked", true);
                // Log the blocked attempt
                LogBlockAttempt(player, $"{name} ({shortName})", "ammunition");
                return false;
            }
            if (InTimedBlock && (_timedAmmo.Contains(name) || _timedAmmo.Contains(shortName)))
            {
                SendBlockMessage(player, "AmmoBlocked", false);
                // Log the blocked attempt
                LogBlockAttempt(player, $"{name} ({shortName})", "ammunition");
                return false;
            }
            return null;
        }

        /// <summary>
        /// New reload hook for recent Rust/Oxide versions. This mirrors the existing
        /// OnReloadMagazine logic by delegating to that method. The new hook
        /// receives the weapon instance, an ammo source (IAmmoContainer) and the
        /// owner player. To maintain compatibility with older versions, the
        /// original OnReloadMagazine hook is retained. When both hooks exist,
        /// Oxide will call the most specific one available.
        /// </summary>
        /// <param name="instance">The projectile (weapon) being reloaded.</param>
        /// <param name="ammoSource">The container providing the ammo; unused in this plugin.</param>
        /// <param name="player">The player attempting to reload.</param>
        /// <returns>False to block reloading of blocked ammo; null to allow.</returns>
        private object OnMagazineReload(BaseProjectile instance, IAmmoContainer ammoSource, BasePlayer player)
        {
            // Delegate to the legacy reload handler to reuse logic
            return OnReloadMagazine(player, instance);
        }

        // Helpers to determine NPC or Duel contexts
        // Generic duel plugin reference (e.g. Duelist)
        [PluginReference] private Plugin Duel;
        // Duels Manager plugin reference. When present, this will be used to
        // determine if a player is currently in a duel via the IsInDuel API
        // documented on the Duels Manager plugin page【50364323406535†L68-L76】.
        // Note: The Duels Manager API expects two Player arguments; we pass the
        // same player twice as a best‑effort check. If a more specific API
        // becomes available, this call can be updated accordingly.
        [PluginReference("DuelsManager")] private Plugin DuelsManager;
        private bool IsNPC(BasePlayer player) => player is NPCPlayer || !(player.userID >= 76560000000000000L || player.userID <= 0L);
        private bool InDuel(BasePlayer player)
        {
            // If the Duels Manager plugin is loaded, use its API.  The
            // IsInDuel(Player, Player) call returns true if the specified
            // players are currently duelling【50364323406535†L68-L76】.  Since we
            // only have one player context here, we pass the same player as
            // both parameters.  If Duels Manager is not installed or the call
            // fails, fall back to the generic duel plugin.
            try
            {
                if (DuelsManager != null)
                {
                    bool result = DuelsManager.Call<bool>("IsInDuel", player, player);
                    if (result) return true;
                }
            }
            catch
            {
                // ignored – will fall back to generic duel plugin
            }
            // Fall back to generic duel plugin (e.g. Duelist)
            try
            {
                if (Duel != null)
                {
                    return Duel.Call<bool>("IsPlayerOnActiveDuel", player);
                }
            }
            catch
            {
                // If call fails, treat as not in duel
            }
            return false;
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

        #region Logging
        /// <summary>
        /// Writes a log entry to the plugin's log file with a UTC timestamp.
        /// </summary>
        /// <param name="message">The message to log. Date/time will be prepended automatically.</param>
        private void LogAction(string message)
        {
            // Prepend a timestamp for each log entry
            string entry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} | {message}";
            LogToFile(LogFileName, entry, this);
        }

        /// <summary>
        /// Logs an attempt by a player to use a blocked item, clothing, ammo or deployable.
        /// Includes the player's display name, Steam ID and approximate position.
        /// </summary>
        /// <param name="player">The player attempting to use the item.</param>
        /// <param name="itemName">The display name or shortname of the blocked item.</param>
        /// <param name="category">The category of the blocked item (item, cloth, ammo, deployable).</param>
        private void LogBlockAttempt(BasePlayer player, string itemName, string category)
        {
            if (player == null) return;
            var pos = player.transform.position;
            string log = $"{player.displayName} ({player.UserIDString}) attempted to use blocked {category} '{itemName}' at {pos.x:F1},{pos.y:F1},{pos.z:F1}";
            LogAction(log);
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
                        // Log the administrative action
                        string actorName = caller?.Name ?? "Server";
                        string actorId = caller?.Id ?? "Server";
                        string logMessage = action == "add"
                            ? $"{actorName} ({actorId}) added {name} to {type} {category} list"
                            : $"{actorName} ({actorId}) removed {name} from {type} {category} list";
                        LogAction(logMessage);
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

                case "loglist":
                    {
                        // Show the last 20 log lines to the caller
                        try
                        {
                            // Build the path to the log file (oxide/logs/LogFileName.txt)
                            string logsDir = Path.Combine(Interface.Oxide.RootDirectory, "oxide", "logs");
                            string logFilePath = Path.Combine(logsDir, LogFileName + ".txt");
                            if (!File.Exists(logFilePath))
                            {
                                caller?.Reply("No log file has been created yet.");
                            }
                            else
                            {
                                var lines = File.ReadAllLines(logFilePath);
                                int count = lines.Length;
                                int start = Math.Max(0, count - 20);
                                var lastLines = lines.Skip(start);
                                var response = string.Join("\n", lastLines);
                                caller?.Reply(response);
                            }
                        }
                        catch (Exception ex)
                        {
                            caller?.Reply("Error reading log: " + ex.Message);
                        }
                        break;
                    }
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

        // Hook called when an entity is built (structure or deployable).
        // This is used to log placement attempts of items that are on the blocked lists.
        private void OnEntityBuilt(Planner plan, GameObject go)
        {
            try
            {
                var player = plan?.GetOwnerPlayer();
                if (player == null || IsNPC(player) || InDuel(player)) return;
                // Respect bypass permission: do not log for bypass players
                if (IsBypass(player)) return;

                // Attempt to determine the prefab and item names
                var entity = go.GetComponent<BaseEntity>();
                if (entity == null) return;
                string prefabName = entity.ShortPrefabName;
                // Look up the item definition by prefab short name (if available)
                var itemDef = ItemManager.FindItemDefinition(prefabName);
                string displayName = itemDef?.displayName?.english ?? prefabName;
                string shortName = itemDef?.shortname ?? prefabName;
                bool blocked = false;
                if (_permanentItems.Contains(displayName) || _permanentItems.Contains(shortName))
                {
                    blocked = true;
                }
                else if (InTimedBlock && (_timedItems.Contains(displayName) || _timedItems.Contains(shortName)))
                {
                    blocked = true;
                }
                if (blocked)
                {
                    // Log the deployment attempt; category 'deployable' covers any placed entity
                    LogBlockAttempt(player, $"{displayName} ({shortName})", "deployable");
                }
            }
            catch (Exception ex)
            {
                // Ensure that logging failures do not affect gameplay
                PrintError($"Error in OnEntityBuilt: {ex.Message}");
            }
        }
        #endregion
    }
}
