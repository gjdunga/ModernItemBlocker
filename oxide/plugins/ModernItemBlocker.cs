/*
 * ModernItemBlocker  v4.1.0
 * Author : gjdunga
 * License: MIT
 *
 * PURPOSE
 * -------
 * Blocks items, clothing, ammunition, and deployables either permanently or for
 * a configurable duration after a server wipe.  Integrates with Oxide permission
 * groups and optionally skips players who are inside a Duel arena.
 *
 * COMPATIBILITY
 * -------------
 * Requires Oxide v2.0.7022+ (Rust Naval Update hook signatures).
 * Hook signatures that changed in that release:
 *   CanEquipItem  (PlayerInventory, Item, int targetSlot)
 *   CanWearItem   (PlayerInventory, Item, int targetSlot)
 *   OnMagazineReload (BaseProjectile, IAmmoContainer, BasePlayer)
 *   CanBuild      (Planner, Construction, Construction.Target)   <- v4.1.0 addition
 *
 * CHANGES IN 4.1.0
 * ----------------
 *   BUG FIX  : Replaced OnEntityBuilt with CanBuild.  OnEntityBuilt fires AFTER
 *              placement so returning false was a no-op; deployable blocking was
 *              silently non-functional.  CanBuild fires BEFORE placement and its
 *              return value is respected by the engine.
 *   BUG FIX  : loglist command now searches for the Oxide date-suffixed log file
 *              (ModernItemBlocker_YYYY-MM-DD.txt) instead of looking for the
 *              non-existent ModernItemBlocker.txt.
 *   BUG FIX  : remove command now correctly distinguishes "removed" from
 *              "not found" and uses the NotFound lang key instead of always
 *              reporting success.
 *   PERF     : Hex color re-validation removed from SendBlockMessage hot path;
 *              the validated value is cached at load/config-change time.
 *   PERF     : CanBuild hook subscription replaces the now-removed OnEntityBuilt
 *              subscription, so Subscribe/Unsubscribe logic is consistent.
 */

using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("ModernItemBlocker", "gjdunga", "4.1.0")]
    [Description("Blocks items, clothing, ammunition and deployables temporarily after a wipe or permanently until removed. Compatible with Oxide v2.0.7022+ and the Rust Naval Update.")]
    public class ModernItemBlocker : RustPlugin
    {
        // ---------------------------------------------------------------
        //  Constants
        // ---------------------------------------------------------------

        /// Name used for the data-file config (oxide/data/ModernItemBlockerConfig.json).
        private const string DataConfigName = "ModernItemBlockerConfig";

        /// Base name passed to Oxide LogToFile; Oxide appends _YYYY-MM-DD.txt.
        private const string LogFileName = "ModernItemBlocker";

        // ---------------------------------------------------------------
        //  Compiled regexes (allocated once at startup)
        // ---------------------------------------------------------------

        /// Validates a 6-digit hex colour, e.g. #f44253.
        private static readonly Regex HexColorRegex =
            new Regex(@"^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

        /// Strips Unity Rich Text markup (<color=…>, <b>, etc.) from arbitrary strings.
        private static readonly Regex RichTextRegex =
            new Regex(@"<[^>]+>", RegexOptions.Compiled);

        // ---------------------------------------------------------------
        //  Cached validated state (set once in ValidateConfig / Init)
        // ---------------------------------------------------------------

        /// Sanitised prefix colour, guaranteed to pass HexColorRegex after validation.
        private string _safeColor = "#f44253";

        /// Sanitised chat prefix (Rich Text stripped), used in every block message.
        private string _safePrefix = "[ModernBlocker]";

        // ===================================================================
        //  REGION: Configuration
        //  The plugin stores its configuration in oxide/data/ rather than
        //  oxide/config/ so the data can be mutated at runtime via commands
        //  and re-read without a full plugin reload.
        // ===================================================================
        #region Configuration

        private Configuration _config;

        /// <summary>
        /// Serialisable configuration object.  All list fields hold raw item names or
        /// shortnames; the plugin matches both at runtime so operators can use either form.
        /// </summary>
        public class Configuration
        {
            /// <summary>
            /// How many hours after a wipe the timed block lists remain active.
            /// A value of 0 disables timed blocking entirely.
            /// </summary>
            [JsonProperty("Block Duration (Hours) after Wipe")]
            public int BlockDurationHours = 30;

            // ---- Permanent lists (never expire) ----

            /// <summary>Item shortnames or display names that are always blocked.</summary>
            [JsonProperty("Permanent Blocked Items")]
            public List<string> PermanentBlockedItems = new List<string>();

            /// <summary>Clothing shortnames or display names that are always blocked.</summary>
            [JsonProperty("Permanent Blocked Clothes")]
            public List<string> PermanentBlockedClothes = new List<string>();

            /// <summary>Ammo shortnames or display names that are always blocked.</summary>
            [JsonProperty("Permanent Blocked Ammo")]
            public List<string> PermanentBlockedAmmo = new List<string>();

            // ---- Timed lists (active only within BlockDurationHours of wipe) ----

            /// <summary>Item shortnames or display names blocked for BlockDurationHours after wipe.</summary>
            [JsonProperty("Timed Blocked Items")]
            public List<string> TimedBlockedItems = new List<string>();

            /// <summary>Clothing shortnames or display names blocked for BlockDurationHours after wipe.</summary>
            [JsonProperty("Timed Blocked Clothes")]
            public List<string> TimedBlockedClothes = new List<string>();

            /// <summary>Ammo shortnames or display names blocked for BlockDurationHours after wipe.</summary>
            [JsonProperty("Timed Blocked Ammo")]
            public List<string> TimedBlockedAmmo = new List<string>();

            // ---- Permissions ----

            /// <summary>
            /// Oxide permission node that exempts a player from all block checks.
            /// Must start with the plugin prefix (enforced in Init).
            /// </summary>
            [JsonProperty("Bypass Permission")]
            public string BypassPermission = "modernitemblocker.bypass";

            /// <summary>
            /// Oxide permission node that grants access to /modernblocker admin commands.
            /// Must start with the plugin prefix (enforced in Init).
            /// </summary>
            [JsonProperty("Admin Permission")]
            public string AdminPermission = "modernitemblocker.admin";

            // ---- UI ----

            /// <summary>Plain-text prefix shown before each block notification in chat.</summary>
            [JsonProperty("Chat Prefix")]
            public string ChatPrefix = "[ModernBlocker]";

            /// <summary>Six-digit hex colour applied to ChatPrefix in Rich Text markup.</summary>
            [JsonProperty("Chat Prefix Color")]
            public string ChatPrefixColor = "#f44253";

            /// <summary>Returns a default configuration with empty block lists.</summary>
            public static Configuration DefaultConfig() => new Configuration
            {
                BlockDurationHours = 30,
                TimedBlockedItems      = new List<string>(),
                TimedBlockedClothes    = new List<string>(),
                TimedBlockedAmmo       = new List<string>(),
                PermanentBlockedItems  = new List<string>(),
                PermanentBlockedClothes= new List<string>(),
                PermanentBlockedAmmo   = new List<string>()
            };
        }

        /// <summary>Creates and writes a fresh default configuration the first time the plugin loads.</summary>
        protected override void LoadDefaultConfig()
        {
            PrintWarning("Generating default configuration.");
            _config = Configuration.DefaultConfig();
            SaveDataConfig();
        }

        /// <summary>Routes Oxide's LoadConfig call to our data-file loader.</summary>
        protected override void LoadConfig() => LoadDataConfig();

        /// <summary>Routes Oxide's SaveConfig call to our data-file writer.</summary>
        protected override void SaveConfig() => SaveDataConfig();

        /// <summary>
        /// Reads the configuration from oxide/data/ModernItemBlockerConfig.json.
        /// Falls back to defaults if the file is absent or corrupted.
        /// </summary>
        private void LoadDataConfig()
        {
            try
            {
                _config = Interface.Oxide.DataFileSystem.ReadObject<Configuration>(DataConfigName);
                if (_config == null)
                    throw new Exception("Deserialized config was null.");
                ValidateConfig();
            }
            catch
            {
                PrintWarning("Data configuration file not found or invalid. Creating default.");
                _config = Configuration.DefaultConfig();
                SaveDataConfig();
            }
        }

        /// <summary>Serialises the current configuration to oxide/data/.</summary>
        private void SaveDataConfig() =>
            Interface.Oxide.DataFileSystem.WriteObject(DataConfigName, _config);

        /// <summary>
        /// Validates loaded configuration values and corrects any that are out-of-range.
        /// Also caches the sanitised colour and prefix strings used in every chat message
        /// so per-message re-validation is not needed.
        /// </summary>
        private void ValidateConfig()
        {
            // BlockDurationHours must be non-negative.
            if (_config.BlockDurationHours < 0)
            {
                PrintWarning($"BlockDurationHours was {_config.BlockDurationHours}; reset to 0.");
                _config.BlockDurationHours = 0;
            }

            // Validate and cache the hex colour.
            if (HexColorRegex.IsMatch(_config.ChatPrefixColor))
                _safeColor = _config.ChatPrefixColor;
            else
            {
                PrintWarning($"ChatPrefixColor '{_config.ChatPrefixColor}' is invalid; reset to #f44253.");
                _config.ChatPrefixColor = "#f44253";
                _safeColor = "#f44253";
            }

            // Strip Rich Text tags from the prefix to prevent markup injection.
            _safePrefix = StripRichText(_config.ChatPrefix);
            if (_safePrefix != _config.ChatPrefix)
                PrintWarning("Rich Text tags were stripped from ChatPrefix.");
        }

        #endregion

        // ===================================================================
        //  REGION: Data & State
        //  HashSet lookups are O(1) with case-insensitive comparer.
        //  Lists are rebuilt into HashSets each time the config changes.
        // ===================================================================
        #region Data & State

        /// UTC timestamp after which the timed block period has expired.
        private DateTime _blockEnd;

        // HashSets for O(1) item-name lookups.
        private HashSet<string> _timedItems;
        private HashSet<string> _timedClothes;
        private HashSet<string> _timedAmmo;
        private HashSet<string> _permanentItems;
        private HashSet<string> _permanentClothes;
        private HashSet<string> _permanentAmmo;

        /// <summary>
        /// Rebuilds all block HashSets from the current config lists.
        /// Call after any config change so lookups reflect the new state.
        /// </summary>
        private void BuildHashSets()
        {
            _timedItems     = new HashSet<string>(_config.TimedBlockedItems,     StringComparer.OrdinalIgnoreCase);
            _timedClothes   = new HashSet<string>(_config.TimedBlockedClothes,   StringComparer.OrdinalIgnoreCase);
            _timedAmmo      = new HashSet<string>(_config.TimedBlockedAmmo,      StringComparer.OrdinalIgnoreCase);
            _permanentItems = new HashSet<string>(_config.PermanentBlockedItems,  StringComparer.OrdinalIgnoreCase);
            _permanentClothes= new HashSet<string>(_config.PermanentBlockedClothes, StringComparer.OrdinalIgnoreCase);
            _permanentAmmo  = new HashSet<string>(_config.PermanentBlockedAmmo,  StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>True while the server is within the wipe-timed block window.</summary>
        private bool InTimedBlock => DateTime.UtcNow < _blockEnd;

        #endregion

        // ===================================================================
        //  REGION: Initialization
        // ===================================================================
        #region Initialization

        /// <summary>
        /// Runs before the server is fully initialised.  Validates permission nodes
        /// and registers them with Oxide so they appear in oxide.grant/oxide.revoke.
        /// </summary>
        private void Init()
        {
            var prefix = Name.ToLowerInvariant();

            // Enforce that permission strings start with the plugin prefix.
            // This prevents misconfiguration where a generic word like "bypass" is used.
            if (!_config.BypassPermission.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
            {
                _config.BypassPermission = $"{prefix}.bypass";
                PrintWarning($"Bypass permission corrected to {_config.BypassPermission}.");
                SaveConfig();
            }
            if (!_config.AdminPermission.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
            {
                _config.AdminPermission = $"{prefix}.admin";
                PrintWarning($"Admin permission corrected to {_config.AdminPermission}.");
                SaveConfig();
            }

            permission.RegisterPermission(_config.BypassPermission, this);
            permission.RegisterPermission(_config.AdminPermission, this);
        }

        /// <summary>
        /// Runs once the server is fully initialised.  Computes the block end time
        /// from the save file creation timestamp (accurate across restarts), builds
        /// HashSets, and subscribes only the hooks that are needed.
        /// </summary>
        private void OnServerInitialized()
        {
            // Prefer the actual save creation time so restarts within the timed window
            // do not reset the clock.
            _blockEnd = SaveRestore.SaveCreatedTime != DateTime.MinValue
                ? SaveRestore.SaveCreatedTime.AddHours(_config.BlockDurationHours)
                : DateTime.UtcNow.AddHours(_config.BlockDurationHours);

            BuildHashSets();
            SubscribeHooks();
        }

        /// <summary>
        /// Fired by Oxide when a new save file is created (i.e. a wipe has occurred).
        /// Resets the timed block window to now + BlockDurationHours.
        /// </summary>
        private void OnNewSave(string filename)
        {
            if (_config == null) return;   // Guard against edge case where wipe fires before Init.
            _blockEnd = DateTime.UtcNow.AddHours(_config.BlockDurationHours);
            SaveConfig();
            Puts($"Wipe detected. Block window ends {_blockEnd:dd.MM.yyyy HH:mm:ss} UTC.");
        }

        /// <summary>
        /// Subscribes or unsubscribes each hook based on whether its corresponding block
        /// lists contain any entries.  Unsubscribing empty hooks eliminates Oxide dispatch
        /// overhead for every game event that would otherwise hit a dead check.
        /// </summary>
        private void SubscribeHooks()
        {
            bool hasItems   = _permanentItems.Count > 0   || _timedItems.Count > 0;
            bool hasClothes = _permanentClothes.Count > 0 || _timedClothes.Count > 0;
            bool hasAmmo    = _permanentAmmo.Count > 0    || _timedAmmo.Count > 0;

            ToggleHook(nameof(CanEquipItem),    hasItems);
            ToggleHook(nameof(CanWearItem),     hasClothes);
            ToggleHook(nameof(OnMagazineReload),hasAmmo);
            // CanBuild covers deployable blocking.  Replaces the former OnEntityBuilt
            // subscription which fired after placement and could not actually prevent it.
            ToggleHook(nameof(CanBuild),        hasItems);
        }

        /// <summary>Subscribe or unsubscribe a named hook in one call.</summary>
        private void ToggleHook(string hookName, bool enable)
        {
            if (enable) Subscribe(hookName);
            else        Unsubscribe(hookName);
        }

        #endregion

        // ===================================================================
        //  REGION: Localization
        // ===================================================================
        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["ItemBlocked"]    = "You cannot use this item.",
                ["ClothBlocked"]   = "You cannot wear this clothing item.",
                ["AmmoBlocked"]    = "You cannot use this ammunition.",
                ["BuildBlocked"]   = "You cannot deploy this item.",
                ["TimedSuffix"]    = "\n{0}d {1:00}:{2:00}:{3:00} remaining until unblock.",
                ["PermanentSuffix"]= "\nThis item is permanently blocked until removed by an admin.",
                ["NotAllowed"]     = "You do not have permission to use this command.",
                ["InvalidArgs"]    = "Invalid syntax. Use /modernblocker help for details.",
                ["Added"]          = "Added '{0}' to the {1} {2} list.",
                ["Removed"]        = "Removed '{0}' from the {1} {2} list.",
                ["NotFound"]       = "'{0}' was not found in the {1} {2} list.",
                ["ListHeader"]     = "Permanent Items: {0}\nPermanent Clothes: {1}\nPermanent Ammo: {2}\nTimed Items: {3}\nTimed Clothes: {4}\nTimed Ammo: {5}",
                ["Usage"]          = "Usage:\n /modernblocker list\n /modernblocker add <permanent|timed> <item|cloth|ammo> <name>\n /modernblocker remove <permanent|timed> <item|cloth|ammo> <name>\n /modernblocker reload\n /modernblocker loglist"
            }, this);
        }

        /// <summary>
        /// Retrieves a localised message for <paramref name="key"/>.
        /// If <paramref name="player"/> is provided the message is returned in their language.
        /// </summary>
        private string Msg(string key, BasePlayer player = null) =>
            lang.GetMessage(key, this, player?.UserIDString);

        #endregion

        // ===================================================================
        //  REGION: Permission Helpers
        // ===================================================================
        #region Permission Helpers

        /// <summary>Returns true if the player holds the bypass permission.</summary>
        private bool IsBypass(BasePlayer player) =>
            permission.UserHasPermission(player.UserIDString, _config.BypassPermission);

        /// <summary>Returns true if the IPlayer is a server admin or holds the admin permission.</summary>
        private bool IsAdmin(IPlayer player) =>
            player.IsAdmin || permission.UserHasPermission(player.Id, _config.AdminPermission);

        #endregion

        // ===================================================================
        //  REGION: Blocking Logic
        //  Central helpers used by all hook handlers.
        // ===================================================================
        #region Blocking Logic

        /// <summary>
        /// Checks whether a given item should be blocked.
        /// Matches against both display name and shortname so operators can use either.
        /// </summary>
        /// <param name="displayName">Human-readable item name (english).</param>
        /// <param name="shortName">Engine shortname, e.g. "rifle.ak".</param>
        /// <param name="permanent">HashSet of permanently blocked names.</param>
        /// <param name="timed">HashSet of timed-block names.</param>
        /// <returns>
        /// true  = permanently blocked,
        /// false = timed blocked (only when InTimedBlock),
        /// null  = not blocked.
        /// </returns>
        private bool? CheckBlocked(
            string displayName, string shortName,
            HashSet<string> permanent, HashSet<string> timed)
        {
            if (permanent.Contains(displayName) || permanent.Contains(shortName))
                return true;
            if (InTimedBlock && (timed.Contains(displayName) || timed.Contains(shortName)))
                return false;
            return null;
        }

        /// <summary>
        /// Returns true if the player should bypass all block checks.
        /// Covers: null player, NPC, in-duel player, or bypass permission holder.
        /// </summary>
        private bool ShouldSkip(BasePlayer player) =>
            player == null || IsNPC(player) || InDuel(player) || IsBypass(player);

        // ---------------------------------------------------------------
        //  Hook: CanEquipItem
        //  Signature changed in Oxide v2.0.7022 to include targetSlot.
        // ---------------------------------------------------------------

        /// <summary>
        /// Prevents a player from equipping a blocked item.
        /// Returning false cancels the equip action.
        /// </summary>
        private object CanEquipItem(PlayerInventory inventory, Item item, int targetSlot)
        {
            var player = inventory.GetBaseEntity() as BasePlayer;
            if (ShouldSkip(player)) return null;

            var blocked = CheckBlocked(
                item.info.displayName.english,
                item.info.shortname,
                _permanentItems, _timedItems);

            if (blocked == null) return null;

            SendBlockMessage(player, "ItemBlocked", blocked.Value);
            LogBlockAttempt(player, item.info.displayName.english, item.info.shortname, "item");
            return false;
        }

        // ---------------------------------------------------------------
        //  Hook: CanWearItem
        //  Signature changed in Oxide v2.0.7022 to include targetSlot.
        // ---------------------------------------------------------------

        /// <summary>
        /// Prevents a player from wearing a blocked clothing item.
        /// Returning false cancels the wear action.
        /// </summary>
        private object CanWearItem(PlayerInventory inventory, Item item, int targetSlot)
        {
            var player = inventory.GetBaseEntity() as BasePlayer;
            if (ShouldSkip(player)) return null;

            var blocked = CheckBlocked(
                item.info.displayName.english,
                item.info.shortname,
                _permanentClothes, _timedClothes);

            if (blocked == null) return null;

            SendBlockMessage(player, "ClothBlocked", blocked.Value);
            LogBlockAttempt(player, item.info.displayName.english, item.info.shortname, "clothing");
            return false;
        }

        // ---------------------------------------------------------------
        //  Hook: OnMagazineReload
        //  Oxide v2.0.7022+ hook; reads ammo type from the weapon magazine
        //  definition directly, avoiding Facepunch Pool inventory scans.
        // ---------------------------------------------------------------

        /// <summary>
        /// Prevents a player from reloading with a blocked ammunition type.
        /// Returning false cancels the reload.
        /// </summary>
        private object OnMagazineReload(BaseProjectile instance, IAmmoContainer ammoSource, BasePlayer player)
        {
            if (ShouldSkip(player)) return null;

            var ammoType = instance.primaryMagazine?.ammoType;
            if (ammoType == null) return null;

            var blocked = CheckBlocked(
                ammoType.displayName.english,
                ammoType.shortname,
                _permanentAmmo, _timedAmmo);

            if (blocked == null) return null;

            SendBlockMessage(player, "AmmoBlocked", blocked.Value);
            LogBlockAttempt(player, ammoType.displayName.english, ammoType.shortname, "ammunition");
            return false;
        }

        // ---------------------------------------------------------------
        //  Hook: CanBuild  (replaces OnEntityBuilt from v4.0.0)
        //  Fires BEFORE placement; returning a non-null value cancels the build.
        //  OnEntityBuilt fired AFTER placement and had no ability to prevent it.
        // ---------------------------------------------------------------

        /// <summary>
        /// Prevents a player from deploying a blocked deployable item.
        /// Returning false cancels the build action before the entity is spawned.
        /// </summary>
        private object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            var player = planner?.GetOwnerPlayer();
            if (ShouldSkip(player)) return null;

            // The planner carries the source inventory item which holds the item definition.
            var item = planner.GetItem();
            if (item?.info == null) return null;

            var blocked = CheckBlocked(
                item.info.displayName.english,
                item.info.shortname,
                _permanentItems, _timedItems);

            if (blocked == null) return null;

            SendBlockMessage(player, "BuildBlocked", blocked.Value);
            LogBlockAttempt(player, item.info.displayName.english, item.info.shortname, "deployable");
            return false;
        }

        #endregion

        // ===================================================================
        //  REGION: NPC & Duel Detection
        // ===================================================================
        #region NPC & Duel Detection

        [PluginReference]
        private Plugin Duel;

        [PluginReference("DuelsManager")]
        private Plugin DuelsManager;

        /// <summary>
        /// Returns true if the player is an NPC.
        /// Uses type check first, then falls back to Steam ID range:
        /// valid Steam64 IDs are >= 76560000000000000; IDs outside that range are engine entities.
        /// </summary>
        private bool IsNPC(BasePlayer player) =>
            player is NPCPlayer ||
            !(player.userID >= 76560000000000000UL || player.userID == 0UL);

        /// <summary>
        /// Returns true if the player is currently inside an active duel.
        /// Checks DuelsManager first, then falls back to the generic Duel plugin.
        /// Exceptions from either plugin call are swallowed to prevent cascade failures.
        /// </summary>
        private bool InDuel(BasePlayer player)
        {
            try
            {
                if (DuelsManager != null && DuelsManager.Call<bool>("IsInDuel", player, player))
                    return true;
            }
            catch { /* plugin call failed; continue to next check */ }

            try
            {
                if (Duel != null)
                    return Duel.Call<bool>("IsPlayerOnActiveDuel", player);
            }
            catch { /* plugin call failed */ }

            return false;
        }

        #endregion

        // ===================================================================
        //  REGION: Messaging
        // ===================================================================
        #region Messaging

        /// <summary>
        /// Sends a block notification to the player.
        /// Uses cached _safeColor and _safePrefix set during ValidateConfig; no
        /// per-call regex execution.
        /// </summary>
        /// <param name="player">Recipient.</param>
        /// <param name="messageKey">Lang key for the base message (e.g. "ItemBlocked").</param>
        /// <param name="permanent">
        /// true  = append PermanentSuffix.
        /// false = append TimedSuffix with remaining time countdown.
        /// </param>
        private void SendBlockMessage(BasePlayer player, string messageKey, bool permanent)
        {
            var prefix  = $"<color={_safeColor}>{_safePrefix}</color> ";
            var message = Msg(messageKey, player);

            if (permanent)
            {
                message += Msg("PermanentSuffix", player);
            }
            else
            {
                var timeLeft = _blockEnd - DateTime.UtcNow;
                if (timeLeft < TimeSpan.Zero) timeLeft = TimeSpan.Zero;
                message += string.Format(
                    Msg("TimedSuffix", player),
                    timeLeft.Days, timeLeft.Hours, timeLeft.Minutes, timeLeft.Seconds);
            }

            PrintToChat(player, prefix + message);
        }

        /// <summary>
        /// Removes all Unity Rich Text markup from <paramref name="input"/>.
        /// Used to sanitise operator-supplied strings before embedding in chat.
        /// </summary>
        private static string StripRichText(string input) =>
            string.IsNullOrEmpty(input) ? input : RichTextRegex.Replace(input, string.Empty);

        #endregion

        // ===================================================================
        //  REGION: Logging
        //  Oxide's LogToFile appends _YYYY-MM-DD.txt to the base file name.
        // ===================================================================
        #region Logging

        /// <summary>
        /// Writes a timestamped entry to the Oxide log file.
        /// Oxide creates oxide/logs/ModernItemBlocker_YYYY-MM-DD.txt automatically.
        /// </summary>
        private void LogAction(string message) =>
            LogToFile(LogFileName, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} | {message}", this);

        /// <summary>
        /// Logs a single block event including the player's name, Steam ID, item info,
        /// category, and world position.
        /// </summary>
        private void LogBlockAttempt(BasePlayer player, string displayName, string shortName, string category)
        {
            if (player == null) return;
            var pos = player.transform.position;
            LogAction(
                $"{player.displayName} ({player.UserIDString}) attempted to use blocked " +
                $"{category} '{displayName} ({shortName})' at " +
                $"{pos.x:F1},{pos.y:F1},{pos.z:F1}");
        }

        #endregion

        // ===================================================================
        //  REGION: Commands
        // ===================================================================
        #region Commands

        /// <summary>Chat command alias for /modernblocker.  Delegates to ExecuteCommand.</summary>
        [ChatCommand("modernblocker")]
        private void ChatCommand(BasePlayer player, string command, string[] args) =>
            ExecuteCommand(player.IPlayer, args);

        /// <summary>
        /// Console / RCON command handler for modernblocker.
        /// Resolves the caller to an IPlayer; a null caller is treated as the server
        /// console and is granted admin access automatically.
        /// </summary>
        [ConsoleCommand("modernblocker")]
        private void ConsoleCommand(ConsoleSystem.Arg arg)
        {
            var caller = arg.Player();
            IPlayer iplayer = caller?.IPlayer
                ?? covalence.Players.FindPlayerById(arg.Connection?.userid.ToString());
            ExecuteCommand(iplayer, arg.Args);
        }

        /// <summary>
        /// Central command dispatcher.  Validates caller permissions then routes to
        /// the appropriate sub-command handler.
        /// </summary>
        private void ExecuteCommand(IPlayer caller, string[] args)
        {
            // A null caller means the server console; grant admin implicitly.
            if (caller != null && !IsAdmin(caller))
            {
                caller.Reply(Msg("NotAllowed"));
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
                    caller?.Reply("ModernItemBlocker configuration reloaded.");
                    break;

                case "loglist":
                    HandleLogListCommand(caller);
                    break;

                default:
                    caller?.Reply(Msg("Usage"));
                    break;
            }
        }

        /// <summary>
        /// Handles the add/remove sub-commands.
        /// Validates type (permanent|timed) and category (item|cloth|ammo) before
        /// touching any list.  Distinguishes "removed" from "not found" so the admin
        /// receives accurate feedback.
        /// </summary>
        private void HandleModifyCommand(IPlayer caller, string[] args)
        {
            if (args.Length < 4)
            {
                caller?.Reply(Msg("InvalidArgs"));
                return;
            }

            bool isAdd     = args[0].Equals("add", StringComparison.OrdinalIgnoreCase);
            var  type      = args[1].ToLower();
            var  category  = args[2].ToLower();
            var  name      = string.Join(" ", args.Skip(3)).Trim();

            // Validate type.
            if (type != "permanent" && type != "timed")
            {
                caller?.Reply(Msg("InvalidArgs"));
                return;
            }
            // Validate category.
            if (category != "item"  && category != "items"    &&
                category != "cloth" && category != "clothes"  &&
                category != "clothing" && category != "ammo")
            {
                caller?.Reply(Msg("InvalidArgs"));
                return;
            }

            var result = ModifyList(isAdd, type, category, name);

            switch (result)
            {
                case ModifyResult.Added:
                    caller?.Reply(string.Format(Msg("Added"), name, type, category));
                    break;
                case ModifyResult.Removed:
                    caller?.Reply(string.Format(Msg("Removed"), name, type, category));
                    break;
                case ModifyResult.NotFound:
                    caller?.Reply(string.Format(Msg("NotFound"), name, type, category));
                    return;   // Nothing changed; skip save and log.
                default:
                    caller?.Reply(Msg("InvalidArgs"));
                    return;
            }

            SaveConfig();
            BuildHashSets();
            SubscribeHooks();

            var actorName = caller?.Name ?? "Server";
            var actorId   = caller?.Id   ?? "Server";
            LogAction($"{actorName} ({actorId}) {(isAdd ? "added" : "removed")} " +
                      $"'{name}' {(isAdd ? "to" : "from")} {type} {category} list.");
        }

        /// <summary>
        /// Reads the last 20 lines from the most recent ModernItemBlocker log file.
        /// Oxide creates date-suffixed log files (e.g. ModernItemBlocker_2025-01-15.txt)
        /// so the command searches for matching files and opens the most recent one.
        /// </summary>
        private void HandleLogListCommand(IPlayer caller)
        {
            try
            {
                var logsDir     = Path.Combine(Interface.Oxide.RootDirectory, "oxide", "logs");
                var resolvedDir = Path.GetFullPath(logsDir);

                if (!Directory.Exists(resolvedDir))
                {
                    caller?.Reply("Log directory not found.");
                    return;
                }

                // Oxide appends _YYYY-MM-DD.txt; collect all matching files.
                var files = Directory.GetFiles(resolvedDir, LogFileName + "_*.txt");
                if (files.Length == 0)
                {
                    caller?.Reply("No log file has been created yet.");
                    return;
                }

                // Path traversal guard: every resolved path must be inside logsDir.
                var safePaths = files.Where(f =>
                    Path.GetFullPath(f).StartsWith(resolvedDir, StringComparison.OrdinalIgnoreCase)).ToArray();

                if (safePaths.Length == 0)
                {
                    caller?.Reply("No accessible log file found.");
                    return;
                }

                // Pick the most recently modified file.
                var logFile = safePaths.OrderByDescending(File.GetLastWriteTimeUtc).First();
                var lines   = File.ReadAllLines(logFile);
                var start   = Math.Max(0, lines.Length - 20);
                caller?.Reply(string.Join("\n", lines.Skip(start)));
            }
            catch (Exception ex)
            {
                caller?.Reply($"Error reading log: {ex.Message}");
            }
        }

        /// <summary>Formats and replies with all six block lists.</summary>
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

        // ---------------------------------------------------------------
        //  Result enum used by ModifyList to eliminate boolean overloading.
        // ---------------------------------------------------------------

        private enum ModifyResult { Added, Removed, NotFound, InvalidList }

        /// <summary>
        /// Adds or removes <paramref name="name"/> from the appropriate config list.
        /// Returns <see cref="ModifyResult"/> so callers can give accurate feedback.
        /// </summary>
        private ModifyResult ModifyList(bool add, string type, string category, string name)
        {
            var targetList = GetTargetList(type, category);
            if (targetList == null)
                return ModifyResult.InvalidList;

            if (add)
            {
                if (!targetList.Contains(name, StringComparer.OrdinalIgnoreCase))
                    targetList.Add(name);
                return ModifyResult.Added;
            }
            else
            {
                int removed = targetList.RemoveAll(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
                return removed > 0 ? ModifyResult.Removed : ModifyResult.NotFound;
            }
        }

        /// <summary>
        /// Maps (type, category) string pairs to the corresponding config list.
        /// Returns null for any unrecognised combination.
        /// </summary>
        private List<string> GetTargetList(string type, string category)
        {
            switch (type)
            {
                case "permanent":
                    switch (category)
                    {
                        case "item": case "items":               return _config.PermanentBlockedItems;
                        case "cloth": case "clothes":
                        case "clothing":                         return _config.PermanentBlockedClothes;
                        case "ammo":                             return _config.PermanentBlockedAmmo;
                    }
                    break;
                case "timed":
                    switch (category)
                    {
                        case "item": case "items":               return _config.TimedBlockedItems;
                        case "cloth": case "clothes":
                        case "clothing":                         return _config.TimedBlockedClothes;
                        case "ammo":                             return _config.TimedBlockedAmmo;
                    }
                    break;
            }
            return null;
        }

        #endregion
    }
}
