using Oxide.Core.Plugins;
using Oxide.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Rust;
using System.IO;
using System.Text.RegularExpressions;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("ModernItemBlocker", "gjdunga", "4.0.0")]
    [Description("Blocks items, clothing and ammunition temporarily after a wipe or permanently until removed. Compatible with Oxide v2.0.7022+ and the Rust Naval Update.")]
    public class ModernItemBlocker : RustPlugin
    {
        private const string DataConfigName = "ModernItemBlockerConfig";
        private const string LogFileName = "ModernItemBlocker";

        // Regex for validating hex color codes used in Rich Text chat markup
        private static readonly Regex HexColorRegex = new Regex(@"^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

        // Regex to strip Rich Text tags from player-facing strings
        private static readonly Regex RichTextRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled);

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

            [JsonProperty("Bypass Permission")]
            public string BypassPermission = "modernitemblocker.bypass";

            [JsonProperty("Admin Permission")]
            public string AdminPermission = "modernitemblocker.admin";

            [JsonProperty("Chat Prefix")]
            public string ChatPrefix = "[ModernBlocker]";

            [JsonProperty("Chat Prefix Color")]
            public string ChatPrefixColor = "#f44253";

            public static Configuration DefaultConfig()
            {
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
            SaveDataConfig();
        }

        protected override void LoadConfig()
        {
            LoadDataConfig();
        }

        protected override void SaveConfig()
        {
            SaveDataConfig();
        }

        private void LoadDataConfig()
        {
            try
            {
                _config = Interface.Oxide.DataFileSystem.ReadObject<Configuration>(DataConfigName);
                if (_config == null)
                    throw new Exception("Data config was null");
                ValidateConfig();
            }
            catch
            {
                PrintWarning("Data configuration file not found or invalid. Creating a new one.");
                _config = Configuration.DefaultConfig();
                SaveDataConfig();
            }
        }

        private void SaveDataConfig()
        {
            Interface.Oxide.DataFileSystem.WriteObject(DataConfigName, _config);
        }

        private void ValidateConfig()
        {
            if (_config.BlockDurationHours < 0)
            {
                PrintWarning($"BlockDurationHours was negative ({_config.BlockDurationHours}), reset to 0.");
                _config.BlockDurationHours = 0;
            }

            if (!HexColorRegex.IsMatch(_config.ChatPrefixColor))
            {
                PrintWarning($"ChatPrefixColor '{_config.ChatPrefixColor}' is not a valid hex color. Reset to #f44253.");
                _config.ChatPrefixColor = "#f44253";
            }

            // Strip any Rich Text tags from the chat prefix to prevent injection
            _config.ChatPrefix = StripRichText(_config.ChatPrefix);
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

        private bool InTimedBlock => DateTime.UtcNow < _blockEnd;

        #endregion

        #region Initialization

        private void Init()
        {
            var prefix = Name.ToLowerInvariant();
            if (!_config.BypassPermission.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
            {
                _config.BypassPermission = $"{prefix}.bypass";
                PrintWarning($"Bypass permission updated to {_config.BypassPermission} to match plugin prefix.");
                SaveConfig();
            }
            if (!_config.AdminPermission.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
            {
                _config.AdminPermission = $"{prefix}.admin";
                PrintWarning($"Admin permission updated to {_config.AdminPermission} to match plugin prefix.");
                SaveConfig();
            }
            permission.RegisterPermission(_config.BypassPermission, this);
            permission.RegisterPermission(_config.AdminPermission, this);
        }

        private void OnServerInitialized()
        {
            if (SaveRestore.SaveCreatedTime != DateTime.MinValue)
                _blockEnd = SaveRestore.SaveCreatedTime.AddHours(_config.BlockDurationHours);
            else
                _blockEnd = DateTime.UtcNow.AddHours(_config.BlockDurationHours);
            BuildHashSets();
            SubscribeHooks();
        }

        private void OnNewSave(string filename)
        {
            if (_config == null) return;
            _blockEnd = DateTime.UtcNow.AddHours(_config.BlockDurationHours);
            SaveConfig();
            Puts($"Wipe detected. Block end set to {_blockEnd:dd.MM.yyyy HH:mm:ss}");
        }

        /// <summary>
        /// Unsubscribe from expensive hooks when their block lists are empty to reduce overhead.
        /// </summary>
        private void SubscribeHooks()
        {
            bool hasItems = _permanentItems.Count > 0 || _timedItems.Count > 0;
            bool hasClothes = _permanentClothes.Count > 0 || _timedClothes.Count > 0;
            bool hasAmmo = _permanentAmmo.Count > 0 || _timedAmmo.Count > 0;

            if (!hasItems)
                Unsubscribe(nameof(CanEquipItem));
            else
                Subscribe(nameof(CanEquipItem));

            if (!hasClothes)
                Unsubscribe(nameof(CanWearItem));
            else
                Subscribe(nameof(CanWearItem));

            if (!hasAmmo)
                Unsubscribe(nameof(OnMagazineReload));
            else
                Subscribe(nameof(OnMagazineReload));

            if (!hasItems)
                Unsubscribe(nameof(OnEntityBuilt));
            else
                Subscribe(nameof(OnEntityBuilt));
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
                ["NotFound"] = "{0} was not found in the {1} {2} list.",
                ["ListHeader"] = "Permanent Items: {0}\nPermanent Clothes: {1}\nPermanent Ammo: {2}\nTimed Items: {3}\nTimed Clothes: {4}\nTimed Ammo: {5}",
                ["Usage"] = "Usage:\n /modernblocker list - list all blocked items\n /modernblocker add <permanent|timed> <item|cloth|ammo> <name> - add an entry\n /modernblocker remove <permanent|timed> <item|cloth|ammo> <name> - remove an entry\n /modernblocker reload - reload configuration from disk\n /modernblocker loglist - display the last 20 log entries"
            }, this);
        }

        private string Msg(string key, BasePlayer player = null) => lang.GetMessage(key, this, player?.UserIDString);

        #endregion

        #region Permission Helpers

        private bool IsBypass(BasePlayer player) => permission.UserHasPermission(player.UserIDString, _config.BypassPermission);
        private bool IsAdmin(IPlayer player) => player.IsAdmin || permission.UserHasPermission(player.Id, _config.AdminPermission);

        #endregion

        #region Blocking Logic

        /// <summary>
        /// Checks if an item name or shortname appears in a permanent or timed block set.
        /// Returns: true = permanently blocked, false = timed blocked, null = not blocked.
        /// </summary>
        private bool? CheckBlocked(string displayName, string shortName, HashSet<string> permanent, HashSet<string> timed)
        {
            if (permanent.Contains(displayName) || permanent.Contains(shortName))
                return true;
            if (InTimedBlock && (timed.Contains(displayName) || timed.Contains(shortName)))
                return false;
            return null;
        }

        /// <summary>
        /// Common guard checks: returns true if the player should be allowed through (skip blocking).
        /// </summary>
        private bool ShouldSkip(BasePlayer player)
        {
            return player == null || IsNPC(player) || InDuel(player) || IsBypass(player);
        }

        // Updated signature for Oxide v2.0.7022+: adds int targetSlot parameter
        private object CanEquipItem(PlayerInventory inventory, Item item, int targetSlot)
        {
            var player = inventory.GetBaseEntity() as BasePlayer;
            if (ShouldSkip(player)) return null;

            var displayName = item.info.displayName.english;
            var shortName = item.info.shortname;
            var blocked = CheckBlocked(displayName, shortName, _permanentItems, _timedItems);
            if (blocked == null) return null;

            SendBlockMessage(player, "ItemBlocked", blocked.Value);
            LogBlockAttempt(player, $"{displayName} ({shortName})", "item");
            return false;
        }

        // Updated signature for Oxide v2.0.7022+: adds int targetSlot parameter
        private object CanWearItem(PlayerInventory inventory, Item item, int targetSlot)
        {
            var player = inventory.GetBaseEntity() as BasePlayer;
            if (ShouldSkip(player)) return null;

            var displayName = item.info.displayName.english;
            var shortName = item.info.shortname;
            var blocked = CheckBlocked(displayName, shortName, _permanentClothes, _timedClothes);
            if (blocked == null) return null;

            SendBlockMessage(player, "ClothBlocked", blocked.Value);
            LogBlockAttempt(player, $"{displayName} ({shortName})", "clothing");
            return false;
        }

        /// <summary>
        /// Modern magazine reload hook (Oxide v2.0.7022+).
        /// Reads ammo type directly from the weapon's magazine definition instead of
        /// searching the player inventory, eliminating Pool allocations.
        /// </summary>
        private object OnMagazineReload(BaseProjectile instance, IAmmoContainer ammoSource, BasePlayer player)
        {
            if (ShouldSkip(player)) return null;

            var ammoType = instance.primaryMagazine.ammoType;
            if (ammoType == null) return null;

            var displayName = ammoType.displayName.english;
            var shortName = ammoType.shortname;
            var blocked = CheckBlocked(displayName, shortName, _permanentAmmo, _timedAmmo);
            if (blocked == null) return null;

            SendBlockMessage(player, "AmmoBlocked", blocked.Value);
            LogBlockAttempt(player, $"{displayName} ({shortName})", "ammunition");
            return false;
        }

        #endregion

        #region NPC & Duel Detection

        [PluginReference] private Plugin Duel;
        [PluginReference("DuelsManager")] private Plugin DuelsManager;

        private bool IsNPC(BasePlayer player) =>
            player is NPCPlayer || !(player.userID >= 76560000000000000UL || player.userID <= 0UL);

        private bool InDuel(BasePlayer player)
        {
            try
            {
                if (DuelsManager != null)
                {
                    if (DuelsManager.Call<bool>("IsInDuel", player, player))
                        return true;
                }
            }
            catch
            {
                // Ignored - fall through to generic duel plugin
            }

            try
            {
                if (Duel != null)
                    return Duel.Call<bool>("IsPlayerOnActiveDuel", player);
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
            var safeColor = HexColorRegex.IsMatch(_config.ChatPrefixColor) ? _config.ChatPrefixColor : "#f44253";
            var safePrefix = StripRichText(_config.ChatPrefix);
            var prefix = $"<color={safeColor}>{safePrefix}</color> ";
            string message = Msg(messageKey, player);

            if (permanent)
            {
                message += Msg("PermanentSuffix", player);
            }
            else
            {
                var timeLeft = _blockEnd - DateTime.UtcNow;
                if (timeLeft.TotalSeconds < 0) timeLeft = TimeSpan.Zero;
                message += string.Format(Msg("TimedSuffix", player),
                    timeLeft.Days, timeLeft.Hours, timeLeft.Minutes, timeLeft.Seconds);
            }

            PrintToChat(player, prefix + message);
        }

        /// <summary>
        /// Strip Rich Text tags from a string to prevent markup injection.
        /// </summary>
        private static string StripRichText(string input) =>
            string.IsNullOrEmpty(input) ? input : RichTextRegex.Replace(input, string.Empty);

        #endregion

        #region Logging

        private void LogAction(string message)
        {
            string entry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} | {message}";
            LogToFile(LogFileName, entry, this);
        }

        private void LogBlockAttempt(BasePlayer player, string itemName, string category)
        {
            if (player == null) return;
            var pos = player.transform.position;
            LogAction($"{player.displayName} ({player.UserIDString}) attempted to use blocked {category} '{itemName}' at {pos.x:F1},{pos.y:F1},{pos.z:F1}");
        }

        #endregion

        #region Commands

        [ChatCommand("modernblocker")]
        private void ChatCommand(BasePlayer player, string command, string[] args)
        {
            ExecuteCommand(player.IPlayer, args);
        }

        [ConsoleCommand("modernblocker")]
        private void ConsoleCommand(ConsoleSystem.Arg arg)
        {
            var caller = arg.Player();
            IPlayer iplayer = caller?.IPlayer ?? covalence.Players.FindPlayerById(arg.Connection?.userid.ToString());
            ExecuteCommand(iplayer, arg.Args);
        }

        private void ExecuteCommand(IPlayer caller, string[] args)
        {
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

            switch (args[0].ToLower())
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
                    HandleModifyCommand(caller, args);
                    break;

                case "reload":
                    LoadDataConfig();
                    BuildHashSets();
                    SubscribeHooks();
                    caller?.Reply("ModernItemBlocker configuration reloaded from file.");
                    break;

                case "loglist":
                    HandleLogListCommand(caller);
                    break;

                default:
                    caller?.Reply(Msg("Usage"));
                    break;
            }
        }

        private void HandleModifyCommand(IPlayer caller, string[] args)
        {
            if (args.Length < 4)
            {
                caller?.Reply(Msg("InvalidArgs"));
                return;
            }

            bool isAdd = args[0].Equals("add", StringComparison.OrdinalIgnoreCase);
            var type = args[1].ToLower();
            var category = args[2].ToLower();
            var name = string.Join(" ", args.Skip(3));

            // Validate type and category before modifying
            if (type != "permanent" && type != "timed")
            {
                caller?.Reply(Msg("InvalidArgs"));
                return;
            }
            if (category != "item" && category != "items" && category != "cloth" &&
                category != "clothes" && category != "clothing" && category != "ammo")
            {
                caller?.Reply(Msg("InvalidArgs"));
                return;
            }

            if (ModifyList(isAdd, type, category, name))
            {
                var status = isAdd ? Msg("Added") : Msg("Removed");
                caller?.Reply(string.Format(status, name, type, category));
                SaveConfig();
                BuildHashSets();
                SubscribeHooks();

                string actorName = caller?.Name ?? "Server";
                string actorId = caller?.Id ?? "Server";
                string verb = isAdd ? "added" : "removed";
                string prep = isAdd ? "to" : "from";
                LogAction($"{actorName} ({actorId}) {verb} {name} {prep} {type} {category} list");
            }
            else
            {
                caller?.Reply(Msg("InvalidArgs"));
            }
        }

        private void HandleLogListCommand(IPlayer caller)
        {
            try
            {
                string logsDir = Path.Combine(Interface.Oxide.RootDirectory, "oxide", "logs");
                string logFilePath = Path.Combine(logsDir, LogFileName + ".txt");

                // Ensure resolved path stays within the logs directory (path traversal guard)
                string resolvedPath = Path.GetFullPath(logFilePath);
                string resolvedLogsDir = Path.GetFullPath(logsDir);
                if (!resolvedPath.StartsWith(resolvedLogsDir, StringComparison.OrdinalIgnoreCase))
                {
                    caller?.Reply("Invalid log file path.");
                    return;
                }

                if (!File.Exists(logFilePath))
                {
                    caller?.Reply("No log file has been created yet.");
                    return;
                }

                var lines = File.ReadAllLines(logFilePath);
                int start = Math.Max(0, lines.Length - 20);
                caller?.Reply(string.Join("\n", lines.Skip(start)));
            }
            catch (Exception ex)
            {
                caller?.Reply("Error reading log: " + ex.Message);
            }
        }

        private void SendLists(IPlayer caller)
        {
            string FormatList(List<string> list) =>
                list.Count > 0 ? string.Join(", ", list) : "<none>";

            caller?.Reply(string.Format(Msg("ListHeader"),
                FormatList(_config.PermanentBlockedItems),
                FormatList(_config.PermanentBlockedClothes),
                FormatList(_config.PermanentBlockedAmmo),
                FormatList(_config.TimedBlockedItems),
                FormatList(_config.TimedBlockedClothes),
                FormatList(_config.TimedBlockedAmmo)));
        }

        private bool ModifyList(bool add, string type, string category, string name)
        {
            var targetList = GetTargetList(type, category);
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

        private List<string> GetTargetList(string type, string category)
        {
            switch (type)
            {
                case "permanent":
                    switch (category)
                    {
                        case "item":
                        case "items":
                            return _config.PermanentBlockedItems;
                        case "cloth":
                        case "clothes":
                        case "clothing":
                            return _config.PermanentBlockedClothes;
                        case "ammo":
                            return _config.PermanentBlockedAmmo;
                    }
                    break;
                case "timed":
                    switch (category)
                    {
                        case "item":
                        case "items":
                            return _config.TimedBlockedItems;
                        case "cloth":
                        case "clothes":
                        case "clothing":
                            return _config.TimedBlockedClothes;
                        case "ammo":
                            return _config.TimedBlockedAmmo;
                    }
                    break;
            }
            return null;
        }

        #endregion

        #region Entity Built Hook

        private void OnEntityBuilt(Planner plan, GameObject go)
        {
            try
            {
                var player = plan?.GetOwnerPlayer();
                if (ShouldSkip(player)) return;

                var entity = go.GetComponent<BaseEntity>();
                if (entity == null) return;

                string prefabName = entity.ShortPrefabName;
                var itemDef = ItemManager.FindItemDefinition(prefabName);
                string displayName = itemDef?.displayName?.english ?? prefabName;
                string shortName = itemDef?.shortname ?? prefabName;

                var blocked = CheckBlocked(displayName, shortName, _permanentItems, _timedItems);
                if (blocked != null)
                    LogBlockAttempt(player, $"{displayName} ({shortName})", "deployable");
            }
            catch (Exception ex)
            {
                PrintError($"Error in OnEntityBuilt: {ex.Message}");
            }
        }

        #endregion
    }
}
