/*
 * ModernItemBlocker  v4.1.3
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
 *   CanEquipItem     (PlayerInventory, Item, int targetSlot)
 *   CanWearItem      (PlayerInventory, Item, int targetSlot)
 *   OnMagazineReload (BaseProjectile, IAmmoContainer, BasePlayer)
 *   CanBuild         (Planner, Construction, Construction.Target)
 *
 * CHANGES IN 4.1.3
 * ----------------
 *   VERIFIED  : No (bool, DateTime?, bool)? value tuples were present in the
 *               codebase.  CheckBlocked returns bool? (simple nullable bool)
 *               which compiles correctly on all Oxide build servers without
 *               System.ValueTuple.  No code change required for this item.
 *   SECURITY  : Unload() hook added.  Oxide automatically unsubscribes hooks
 *               on plugin unload, but an explicit Unsubscribe pass in Unload()
 *               prevents any hook firing against a partially-torn-down instance
 *               in rare race conditions (e.g., late CanBuild events arriving
 *               while Oxide is removing the plugin).  All six HashSets are also
 *               cleared to release references early.
 *   SECURITY  : InDuel() now calls PrintWarning() on caught exceptions instead
 *               of silently swallowing them.  Silent catch blocks mask real
 *               failures (e.g., wrong plugin API, version mismatch) making
 *               debugging nearly impossible.  The warning includes the plugin
 *               name and exception message without a stack trace to keep server
 *               console output readable.
 *   SECURITY  : HandleLogListCommand() replaced File.ReadAllLines() with a
 *               tail-read using FileStream + StreamReader.  ReadAllLines() loads
 *               the entire file into memory; on a busy server the daily log can
 *               reach tens of MB.  The new reader seeks from the end of the file
 *               and reads only enough bytes to collect 20 lines, capped at 64 KB
 *               to bound worst-case memory use absolutely.
 *   FIX       : ConsoleCommand() now passes string.Empty (not null) to
 *               FindPlayerById when arg.Connection is null.  FindPlayerById
 *               on some Covalence backends throws on a null-string argument
 *               rather than returning null gracefully.
 *   ADD       : Russian (ru), Spanish (es), and Latin (la) lang files added to
 *               oxide/lang/.
 *   STRUCTURE : ModernItemBlocker.cs removed from repository root.  The canonical
 *               path for Oxide plugins is oxide/plugins/; a duplicate root copy
 *               violated the umod submission structure requirement.
 *
 * CHANGES IN 4.1.2
 * ----------------
 *   SECURITY : [Info] title corrected from "ModernItemBlocker" to "Modern Item Blocker"
 *              to comply with the umod style guide requirement for human-readable,
 *              space-separated plugin titles in the console.
 *   SECURITY : SanitizeLog now strips all ASCII control characters (0x00-0x1F and 0x7F)
 *              in addition to the pipe delimiter (|).  Previously only \n and \r were
 *              removed; null bytes and other control chars could still corrupt log
 *              parsers.  A compiled Regex replaces the manual char.Replace chain.
 *   SECURITY : Null guard added in CheckBlocked for displayName and shortName.
 *              Custom or modded items can have a null displayName.english string;
 *              passing null to HashSet.Contains throws ArgumentNullException.  Both
 *              names are now replaced with an empty string when null.
 *   FIX      : SendLists empty-list placeholder changed from "<none>" to "(none)".
 *              The angle brackets in "<none>" are parsed by Unity's Rich Text renderer
 *              as an unknown tag and may be silently dropped, producing blank output.
 *
 * CHANGES IN 4.1.1
 * ----------------
 *   SECURITY : Log injection fix.  player.displayName and item names are now
 *              sanitised before being written to the log file.  Characters that
 *              could forge a new log line (\n, \r) or break the pipe-delimited
 *              format (|) are replaced with a space.
 *   SECURITY : Null list guard.  ValidateConfig now null-coerces all six block-
 *              list fields.  A config file with "Permanent Blocked Items": null
 *              previously caused BuildHashSets to throw NullReferenceException
 *              on startup; the plugin now falls back to an empty list instead.
 *   SECURITY : Item name length cap (256 chars) added to the add/remove command
 *              parser.  Prevents log and config bloat from arbitrarily long names.
 *   SECURITY : Rich Text stripped from item names in SendLists output.  Entries
 *              containing <color> or other Unity markup tags no longer render as
 *              formatted text in the chat reply.
 *   FIX      : Removed the spurious SaveConfig() call in OnNewSave.  _blockEnd
 *              is a runtime field, not persisted in the config file.  The call
 *              was a no-op that could mislead readers into thinking wipe-time was
 *              being serialised.
 *   FIX      : README minimum Oxide version corrected from v2.0.6599 to v2.0.7022.
 *   ADD      : oxide/lang/en/ModernItemBlocker.json shipped in repo so operators
 *              can see all translatable keys without reading source code.
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
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Modern Item Blocker", "gjdunga", "4.1.3")]
    [Description("Blocks items, clothing, ammunition and deployables temporarily after a wipe or permanently until removed. Compatible with Oxide v2.0.7022+ and the Rust Naval Update.")]
    public class ModernItemBlocker : RustPlugin
    {
        // ---------------------------------------------------------------
        //  Constants
        // ---------------------------------------------------------------

        /// <summary>
        /// Key used with Oxide DataFileSystem for reading/writing the config.
        /// Resolves to oxide/data/ModernItemBlockerConfig.json at runtime.
        /// </summary>
        private const string DataConfigName = "ModernItemBlockerConfig";

        /// <summary>
        /// Base name passed to Oxide's LogToFile helper.
        /// Oxide appends _YYYY-MM-DD.txt, producing e.g. ModernItemBlocker_2025-06-01.txt.
        /// </summary>
        private const string LogFileName = "ModernItemBlocker";

        /// <summary>
        /// Maximum character length accepted for a block-list entry name.
        /// Names longer than this are rejected by the add/remove command parser to
        /// prevent config and log file bloat.
        /// </summary>
        private const int MaxNameLength = 256;

        /// <summary>
        /// Maximum byte count read from the end of a log file by HandleLogListCommand.
        /// Caps worst-case memory allocation at 64 KB regardless of log file size.
        /// </summary>
        private const int LogTailMaxBytes = 65536;

        // ---------------------------------------------------------------
        //  Compiled regexes (allocated once at class load time)
        // ---------------------------------------------------------------

        /// <summary>
        /// Validates a 6-digit RGB hex colour string such as #f44253.
        /// Only lowercase and uppercase hex digits are accepted; no 3-digit shorthand.
        /// </summary>
        private static readonly Regex HexColorRegex =
            new Regex(@"^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

        /// <summary>
        /// Matches any Unity Rich Text markup tag, e.g. &lt;color=#fff&gt;, &lt;b&gt;, &lt;/b&gt;.
        /// Used to strip operator-supplied strings before embedding them in chat messages.
        /// </summary>
        private static readonly Regex RichTextRegex =
            new Regex(@"<[^>]+>", RegexOptions.Compiled);

        /// <summary>
        /// Matches all ASCII control characters (0x00-0x1F, 0x7F) and the pipe character
        /// used as log field delimiter.  All matches are replaced with a space by SanitizeLog.
        ///
        /// Control characters beyond \n and \r (e.g. null bytes, BEL, DEL) can corrupt
        /// log file parsers and terminal emulators; this regex eliminates the entire range.
        /// </summary>
        private static readonly Regex SanitizeLogRegex =
            new Regex(@"[\x00-\x1F\x7F|]", RegexOptions.Compiled);

        // ---------------------------------------------------------------
        //  Cached validated state (populated in ValidateConfig / Init)
        // ---------------------------------------------------------------

        /// <summary>
        /// Hex colour string that has passed HexColorRegex validation.
        /// Cached at load time so hot-path block messages do not re-validate.
        /// Default matches the config default.
        /// </summary>
        private string _safeColor = "#f44253";

        /// <summary>
        /// Chat prefix with all Rich Text tags stripped.
        /// Cached at load time to prevent per-message regex execution.
        /// </summary>
        private string _safePrefix = "[ModernBlocker]";

        // ===================================================================
        //  REGION: Configuration
        //
        //  The plugin stores its configuration in oxide/data/ rather than
        //  oxide/config/ so the data can be mutated at runtime via commands
        //  and re-read without a full plugin reload.  Oxide's DataFileSystem
        //  handles JSON serialisation via Newtonsoft.Json.
        // ===================================================================
        #region Configuration

        private Configuration _config;

        /// <summary>
        /// Serialisable configuration object.
        ///
        /// All list fields hold raw item names or shortnames; the plugin matches
        /// both at runtime so operators can use either form (e.g. "Assault Rifle"
        /// or "rifle.ak").  Matching is always case-insensitive.
        /// </summary>
        public class Configuration
        {
            /// <summary>
            /// How many hours after a wipe the timed block lists remain active.
            /// Set to 0 to disable timed blocking entirely (permanent lists still apply).
            /// Must be non-negative; negative values are corrected to 0 by ValidateConfig.
            /// </summary>
            [JsonProperty("Block Duration (Hours) after Wipe")]
            public int BlockDurationHours = 30;

            // ---- Permanent lists (never expire, persist across wipes) ----

            /// <summary>
            /// Item shortnames or display names that are always blocked, regardless of
            /// how long ago the last wipe occurred.
            /// Example: ["rifle.ak", "Rocket Launcher"]
            /// </summary>
            [JsonProperty("Permanent Blocked Items")]
            public List<string> PermanentBlockedItems = new List<string>();

            /// <summary>
            /// Clothing shortnames or display names that are permanently blocked.
            /// Example: ["metal.facemask", "Heavy Plate Jacket"]
            /// </summary>
            [JsonProperty("Permanent Blocked Clothes")]
            public List<string> PermanentBlockedClothes = new List<string>();

            /// <summary>
            /// Ammunition shortnames or display names that are permanently blocked.
            /// Example: ["ammo.rocket.hv", "High Velocity Rocket"]
            /// </summary>
            [JsonProperty("Permanent Blocked Ammo")]
            public List<string> PermanentBlockedAmmo = new List<string>();

            // ---- Timed lists (active only within BlockDurationHours of a wipe) ----

            /// <summary>
            /// Item shortnames or display names blocked for BlockDurationHours after
            /// the most recent wipe.  The block expires automatically when time elapses.
            /// </summary>
            [JsonProperty("Timed Blocked Items")]
            public List<string> TimedBlockedItems = new List<string>();

            /// <summary>
            /// Clothing shortnames or display names blocked for BlockDurationHours after wipe.
            /// </summary>
            [JsonProperty("Timed Blocked Clothes")]
            public List<string> TimedBlockedClothes = new List<string>();

            /// <summary>
            /// Ammo shortnames or display names blocked for BlockDurationHours after wipe.
            /// </summary>
            [JsonProperty("Timed Blocked Ammo")]
            public List<string> TimedBlockedAmmo = new List<string>();

            // ---- Permissions ----

            /// <summary>
            /// Oxide permission node that exempts the holder from all block checks.
            /// Must begin with the plugin name prefix (modernitemblocker.) to avoid
            /// namespace collisions; Init corrects any misconfigured value automatically.
            /// Default: "modernitemblocker.bypass"
            /// </summary>
            [JsonProperty("Bypass Permission")]
            public string BypassPermission = "modernitemblocker.bypass";

            /// <summary>
            /// Oxide permission node required to use /modernblocker management commands.
            /// Must begin with the plugin name prefix.
            /// Default: "modernitemblocker.admin"
            /// </summary>
            [JsonProperty("Admin Permission")]
            public string AdminPermission = "modernitemblocker.admin";

            // ---- UI ----

            /// <summary>
            /// Plain-text prefix prepended to every block notification in chat.
            /// Rich Text tags are stripped from this value at load time.
            /// Default: "[ModernBlocker]"
            /// </summary>
            [JsonProperty("Chat Prefix")]
            public string ChatPrefix = "[ModernBlocker]";

            /// <summary>
            /// Six-digit hex colour (#RRGGBB) applied to the ChatPrefix via Unity Rich Text.
            /// Must match ^#[0-9A-Fa-f]{6}$; invalid values are corrected to #f44253.
            /// Default: "#f44253"
            /// </summary>
            [JsonProperty("Chat Prefix Color")]
            public string ChatPrefixColor = "#f44253";

            /// <summary>
            /// Returns a new Configuration instance populated with sensible defaults
            /// and empty block lists.  Called when no config file exists.
            /// </summary>
            public static Configuration DefaultConfig() => new Configuration
            {
                BlockDurationHours      = 30,
                TimedBlockedItems       = new List<string>(),
                TimedBlockedClothes     = new List<string>(),
                TimedBlockedAmmo        = new List<string>(),
                PermanentBlockedItems   = new List<string>(),
                PermanentBlockedClothes = new List<string>(),
                PermanentBlockedAmmo    = new List<string>()
            };
        }

        /// <summary>
        /// Called by Oxide when no config file exists.
        /// Creates a default configuration and persists it to disk immediately so
        /// operators have a file to edit before the next reload.
        /// </summary>
        protected override void LoadDefaultConfig()
        {
            PrintWarning("Generating default configuration.");
            _config = Configuration.DefaultConfig();
            SaveDataConfig();
        }

        /// <summary>
        /// Routes Oxide's standard LoadConfig call to our data-file loader.
        /// Oxide calls this during plugin load; we redirect to oxide/data/ instead
        /// of the default oxide/config/ location.
        /// </summary>
        protected override void LoadConfig() => LoadDataConfig();

        /// <summary>
        /// Routes Oxide's standard SaveConfig call to our data-file writer.
        /// Keeps the routing transparent so internal callers can use SaveConfig()
        /// without knowing the storage location.
        /// </summary>
        protected override void SaveConfig() => SaveDataConfig();

        /// <summary>
        /// Reads configuration from oxide/data/ModernItemBlockerConfig.json.
        ///
        /// On success, ValidateConfig is called to enforce field constraints and
        /// cache derived values.  On any error (file missing, JSON invalid, null
        /// result), falls back to defaults and writes a fresh file.
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

        /// <summary>
        /// Serialises the current _config object to oxide/data/ModernItemBlockerConfig.json
        /// via Oxide's DataFileSystem (Newtonsoft.Json, indented format).
        /// </summary>
        private void SaveDataConfig() =>
            Interface.Oxide.DataFileSystem.WriteObject(DataConfigName, _config);

        /// <summary>
        /// Validates loaded configuration values, corrects out-of-range fields, and
        /// caches derived state used in hot paths.
        ///
        /// Security notes:
        ///   * Null list fields are coerced to empty lists so BuildHashSets does not
        ///     throw NullReferenceException on a malformed config file.
        ///   * ChatPrefixColor is validated against HexColorRegex; invalid values are
        ///     reset to the default to prevent Rich Text injection via colour strings.
        ///   * ChatPrefix has all Rich Text tags stripped and the result is cached in
        ///     _safePrefix so no per-message regex is needed in SendBlockMessage.
        /// </summary>
        private void ValidateConfig()
        {
            // ---- Null-coerce all six block lists -------------------------
            // If the JSON file contains "Permanent Blocked Items": null (or any list
            // is missing entirely), the deserializer sets that field to null.
            // BuildHashSets passes these to HashSet<string>(IEnumerable<string>, ...)
            // which throws ArgumentNullException on a null source.  Replace each null
            // with an empty list so the plugin starts safely.
            _config.PermanentBlockedItems   ??= new List<string>();
            _config.PermanentBlockedClothes ??= new List<string>();
            _config.PermanentBlockedAmmo    ??= new List<string>();
            _config.TimedBlockedItems       ??= new List<string>();
            _config.TimedBlockedClothes     ??= new List<string>();
            _config.TimedBlockedAmmo        ??= new List<string>();

            // ---- Numeric range check ------------------------------------
            if (_config.BlockDurationHours < 0)
            {
                PrintWarning($"BlockDurationHours was {_config.BlockDurationHours}; reset to 0.");
                _config.BlockDurationHours = 0;
            }

            // ---- Colour validation and caching --------------------------
            if (HexColorRegex.IsMatch(_config.ChatPrefixColor ?? string.Empty))
            {
                _safeColor = _config.ChatPrefixColor;
            }
            else
            {
                PrintWarning($"ChatPrefixColor '{_config.ChatPrefixColor}' is invalid; reset to #f44253.");
                _config.ChatPrefixColor = "#f44253";
                _safeColor = "#f44253";
            }

            // ---- Prefix sanitisation and caching ------------------------
            _safePrefix = StripRichText(_config.ChatPrefix ?? string.Empty);
            if (_safePrefix != _config.ChatPrefix)
                PrintWarning("Rich Text tags were stripped from ChatPrefix.");
        }

        #endregion

        // ===================================================================
        //  REGION: Data & State
        //
        //  Six HashSets mirror the six config lists.  HashSet<string> with an
        //  OrdinalIgnoreCase comparer gives O(1) contains-check regardless of
        //  list size.  The sets are rebuilt from the lists every time the config
        //  changes so the authoritative source of truth remains the config lists
        //  (which are serialised to disk).
        // ===================================================================
        #region Data & State

        /// <summary>
        /// UTC timestamp after which the timed block window has closed.
        /// Computed from SaveRestore.SaveCreatedTime at startup and reset to
        /// UtcNow + BlockDurationHours whenever OnNewSave fires.
        /// </summary>
        private DateTime _blockEnd;

        // HashSets for O(1) item-name lookups, rebuilt by BuildHashSets().
        private HashSet<string> _timedItems;
        private HashSet<string> _timedClothes;
        private HashSet<string> _timedAmmo;
        private HashSet<string> _permanentItems;
        private HashSet<string> _permanentClothes;
        private HashSet<string> _permanentAmmo;

        /// <summary>
        /// Rebuilds all six block HashSets from the corresponding config lists.
        ///
        /// Must be called after any change to the config lists (add, remove, reload)
        /// so that in-memory lookups reflect the updated state.  The config lists
        /// remain the authoritative source; the HashSets are a read-only lookup cache.
        /// </summary>
        private void BuildHashSets()
        {
            _timedItems       = new HashSet<string>(_config.TimedBlockedItems,       StringComparer.OrdinalIgnoreCase);
            _timedClothes     = new HashSet<string>(_config.TimedBlockedClothes,     StringComparer.OrdinalIgnoreCase);
            _timedAmmo        = new HashSet<string>(_config.TimedBlockedAmmo,        StringComparer.OrdinalIgnoreCase);
            _permanentItems   = new HashSet<string>(_config.PermanentBlockedItems,   StringComparer.OrdinalIgnoreCase);
            _permanentClothes = new HashSet<string>(_config.PermanentBlockedClothes, StringComparer.OrdinalIgnoreCase);
            _permanentAmmo    = new HashSet<string>(_config.PermanentBlockedAmmo,    StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true while the server clock is before _blockEnd, meaning the
        /// timed block window is still active.  Evaluated on every block check.
        /// </summary>
        private bool InTimedBlock => DateTime.UtcNow < _blockEnd;

        #endregion

        // ===================================================================
        //  REGION: Initialization
        // ===================================================================
        #region Initialization

        /// <summary>
        /// Called by Oxide before the server is fully initialised.
        ///
        /// Validates the two permission node strings loaded from config and registers
        /// them with Oxide so they appear in oxide.grant / oxide.revoke output and can
        /// be assigned to groups immediately.
        ///
        /// Permission strings that do not start with the plugin's lowercase name followed
        /// by a dot (e.g. "modernitemblocker.") are automatically corrected and saved to
        /// prevent misconfiguration such as using a bare word like "bypass".
        /// </summary>
        private void Init()
        {
            var prefix = Name.ToLowerInvariant();

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
        /// Called by Oxide once the server is fully initialised.
        ///
        /// Computes _blockEnd from the save file creation timestamp rather than
        /// UtcNow so that restarts within the timed window do not reset the clock.
        /// Falls back to UtcNow when SaveRestore.SaveCreatedTime is unavailable.
        ///
        /// After computing _blockEnd, builds the in-memory HashSets and subscribes
        /// only the Oxide hooks that are needed given the current block lists.
        /// </summary>
        private void OnServerInitialized()
        {
            _blockEnd = SaveRestore.SaveCreatedTime != DateTime.MinValue
                ? SaveRestore.SaveCreatedTime.AddHours(_config.BlockDurationHours)
                : DateTime.UtcNow.AddHours(_config.BlockDurationHours);

            BuildHashSets();
            SubscribeHooks();
        }

        /// <summary>
        /// Called by Oxide when the plugin is unloaded (oxide.unload, server restart,
        /// or hot-reload).
        ///
        /// Explicitly unsubscribes all four hooks to prevent late-arriving events from
        /// dispatching into a partially-torn-down plugin instance.  Oxide's hook
        /// dispatcher is usually correct, but the explicit pass closes any race window
        /// between event queuing and plugin teardown.
        ///
        /// Clears all six HashSets to release object references and allow GC to reclaim
        /// memory immediately rather than waiting for the next full GC cycle.
        /// </summary>
        private void Unload()
        {
            Unsubscribe(nameof(CanEquipItem));
            Unsubscribe(nameof(CanWearItem));
            Unsubscribe(nameof(OnMagazineReload));
            Unsubscribe(nameof(CanBuild));

            _timedItems?.Clear();
            _timedClothes?.Clear();
            _timedAmmo?.Clear();
            _permanentItems?.Clear();
            _permanentClothes?.Clear();
            _permanentAmmo?.Clear();
        }

        /// <summary>
        /// Oxide hook fired when a new save file is created, signalling a wipe.
        ///
        /// Resets _blockEnd to now + BlockDurationHours so the full timed-block
        /// window is available from the start of the new wipe.
        ///
        /// Note: SaveConfig() is intentionally NOT called here.  _blockEnd is a
        /// runtime field that is not stored in the config file; it is recomputed
        /// from SaveRestore.SaveCreatedTime on every server start.  Calling
        /// SaveConfig() would write an unchanged config to disk unnecessarily.
        /// </summary>
        private void OnNewSave(string filename)
        {
            if (_config == null) return; // Guard: wipe fired before Init completed.
            _blockEnd = DateTime.UtcNow.AddHours(_config.BlockDurationHours);
            Puts($"Wipe detected. Timed block window ends {_blockEnd:yyyy-MM-dd HH:mm:ss} UTC.");
        }

        /// <summary>
        /// Subscribes or unsubscribes each Oxide hook based on whether its corresponding
        /// block lists contain any entries.
        ///
        /// Unsubscribing hooks when their lists are empty eliminates Oxide's per-event
        /// dispatch overhead for CanEquipItem, CanWearItem, OnMagazineReload, and CanBuild
        /// when no blocking is configured.  This matters on busy servers where these
        /// events fire hundreds of times per second.
        ///
        /// CanBuild is tied to the items lists because deployables are placed via
        /// inventory items (e.g. "Large Wood Box") and are therefore classified as items.
        /// </summary>
        private void SubscribeHooks()
        {
            bool hasItems   = _permanentItems.Count > 0   || _timedItems.Count > 0;
            bool hasClothes = _permanentClothes.Count > 0 || _timedClothes.Count > 0;
            bool hasAmmo    = _permanentAmmo.Count > 0    || _timedAmmo.Count > 0;

            ToggleHook(nameof(CanEquipItem),     hasItems);
            ToggleHook(nameof(CanWearItem),      hasClothes);
            ToggleHook(nameof(OnMagazineReload), hasAmmo);
            ToggleHook(nameof(CanBuild),         hasItems);
        }

        /// <summary>
        /// Subscribe or unsubscribe a named hook in a single call.
        /// Reduces call-site verbosity in SubscribeHooks.
        /// </summary>
        private void ToggleHook(string hookName, bool enable)
        {
            if (enable) Subscribe(hookName);
            else        Unsubscribe(hookName);
        }

        #endregion

        // ===================================================================
        //  REGION: Localization
        //
        //  All player-facing strings are registered via the Oxide Lang API.
        //  Operators can override these by editing oxide/lang/en/ModernItemBlocker.json
        //  or by creating parallel files for other language codes (ru, es, la, etc.).
        // ===================================================================
        #region Localization

        /// <summary>
        /// Registers the default English message set with Oxide's Lang API.
        /// Keys are stable across versions; new keys are added without changing old ones.
        /// </summary>
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["ItemBlocked"]     = "You cannot use this item.",
                ["ClothBlocked"]    = "You cannot wear this clothing item.",
                ["AmmoBlocked"]     = "You cannot use this ammunition.",
                ["BuildBlocked"]    = "You cannot deploy this item.",
                ["TimedSuffix"]     = "\n{0}d {1:00}:{2:00}:{3:00} remaining until unblock.",
                ["PermanentSuffix"] = "\nThis item is permanently blocked until removed by an admin.",
                ["NotAllowed"]      = "You do not have permission to use this command.",
                ["InvalidArgs"]     = "Invalid syntax. Use /modernblocker help for details.",
                ["NameTooLong"]     = "Item name is too long (max 256 characters).",
                ["Added"]           = "Added '{0}' to the {1} {2} list.",
                ["Removed"]         = "Removed '{0}' from the {1} {2} list.",
                ["NotFound"]        = "'{0}' was not found in the {1} {2} list.",
                ["ListHeader"]      = "Permanent Items: {0}\nPermanent Clothes: {1}\nPermanent Ammo: {2}\nTimed Items: {3}\nTimed Clothes: {4}\nTimed Ammo: {5}",
                ["Usage"]           = "Usage:\n /modernblocker list\n /modernblocker add <permanent|timed> <item|cloth|ammo> <n>\n /modernblocker remove <permanent|timed> <item|cloth|ammo> <n>\n /modernblocker reload\n /modernblocker loglist"
            }, this);
        }

        /// <summary>
        /// Retrieves a localised message string for the given key.
        /// When a player is provided, the message is returned in their configured
        /// Oxide language; otherwise the server default (usually English) is used.
        /// </summary>
        private string Msg(string key, BasePlayer player = null) =>
            lang.GetMessage(key, this, player?.UserIDString);

        #endregion

        // ===================================================================
        //  REGION: Permission Helpers
        // ===================================================================
        #region Permission Helpers

        /// <summary>
        /// Returns true if the player holds the bypass permission configured in
        /// _config.BypassPermission.  Bypass exempts the player from all block checks.
        /// </summary>
        private bool IsBypass(BasePlayer player) =>
            permission.UserHasPermission(player.UserIDString, _config.BypassPermission);

        /// <summary>
        /// Returns true if the IPlayer is either a server admin (IsAdmin flag set by
        /// Oxide for the server console and RCON) or holds the admin permission node.
        /// </summary>
        private bool IsAdmin(IPlayer player) =>
            player.IsAdmin || permission.UserHasPermission(player.Id, _config.AdminPermission);

        #endregion

        // ===================================================================
        //  REGION: Blocking Logic
        //
        //  CheckBlocked and ShouldSkip are called from every hook handler.
        //  They are kept as thin as possible to minimise per-event cost.
        // ===================================================================
        #region Blocking Logic

        /// <summary>
        /// Determines whether an item identified by display name or shortname should
        /// be blocked, and whether that block is permanent or timed.
        ///
        /// Return value semantics (bool?):
        ///   true  = item is permanently blocked.
        ///   false = item is timed-blocked (InTimedBlock is true and name is in timed set).
        ///   null  = item is not blocked by any active rule.
        ///
        /// Permanent blocks take priority over timed blocks.  Timed blocks are only
        /// evaluated when InTimedBlock is true (i.e. within the post-wipe window).
        ///
        /// NOTE: This method returns a simple nullable bool (bool?), NOT a value tuple.
        /// System.ValueTuple is not required and the code builds on all Oxide build
        /// servers without additional assembly references.
        ///
        /// Security: displayName and shortName are null-coerced to string.Empty before
        /// HashSet lookups.  Custom or modded items can return a null displayName.english
        /// string; passing null to HashSet.Contains throws ArgumentNullException.
        /// </summary>
        /// <param name="displayName">
        /// The English display name of the item, e.g. "Assault Rifle".
        /// Sourced from item.info.displayName.english.  May be null for custom items.
        /// </param>
        /// <param name="shortName">
        /// The engine shortname of the item, e.g. "rifle.ak".
        /// Sourced from item.info.shortname.  May be null for custom items.
        /// </param>
        /// <param name="permanent">HashSet of permanently blocked names for this category.</param>
        /// <param name="timed">HashSet of timed-block names for this category.</param>
        private bool? CheckBlocked(
            string displayName, string shortName,
            HashSet<string> permanent, HashSet<string> timed)
        {
            var dn = displayName ?? string.Empty;
            var sn = shortName  ?? string.Empty;

            if (permanent.Contains(dn) || permanent.Contains(sn))
                return true;
            if (InTimedBlock && (timed.Contains(dn) || timed.Contains(sn)))
                return false;
            return null;
        }

        /// <summary>
        /// Returns true if block checks should be skipped for this player entirely.
        ///
        /// Short-circuit evaluation order (cheap checks first):
        ///   1. player is null (defensive; callers should filter before calling)
        ///   2. player is an NPC
        ///   3. player is currently in an active duel (requires a duel plugin)
        ///   4. player holds the bypass permission
        /// </summary>
        private bool ShouldSkip(BasePlayer player) =>
            player == null || IsNPC(player) || InDuel(player) || IsBypass(player);

        // ---------------------------------------------------------------
        //  Hook: CanEquipItem
        //  Signature introduced in Oxide v2.0.7022 (Rust Naval Update).
        //  The targetSlot parameter is unused but required for hook matching.
        // ---------------------------------------------------------------

        /// <summary>
        /// Oxide hook fired when a player attempts to equip an item.
        /// Returns false to cancel; null to allow.
        /// </summary>
        private object CanEquipItem(PlayerInventory inventory, Item item, int targetSlot)
        {
            if (item?.info == null) return null;
            var player = inventory?.GetBaseEntity() as BasePlayer;
            if (ShouldSkip(player)) return null;

            var blocked = CheckBlocked(
                item.info.displayName.english, item.info.shortname,
                _permanentItems, _timedItems);
            if (blocked == null) return null;

            SendBlockMessage(player, "ItemBlocked", blocked.Value);
            LogBlockAttempt(player, item.info.displayName.english, item.info.shortname, "item");
            return false;
        }

        // ---------------------------------------------------------------
        //  Hook: CanWearItem
        //  Signature introduced in Oxide v2.0.7022 (Rust Naval Update).
        // ---------------------------------------------------------------

        /// <summary>
        /// Oxide hook fired when a player attempts to equip a clothing item.
        /// Returns false to cancel; null to allow.
        /// </summary>
        private object CanWearItem(PlayerInventory inventory, Item item, int targetSlot)
        {
            if (item?.info == null) return null;
            var player = inventory?.GetBaseEntity() as BasePlayer;
            if (ShouldSkip(player)) return null;

            var blocked = CheckBlocked(
                item.info.displayName.english, item.info.shortname,
                _permanentClothes, _timedClothes);
            if (blocked == null) return null;

            SendBlockMessage(player, "ClothBlocked", blocked.Value);
            LogBlockAttempt(player, item.info.displayName.english, item.info.shortname, "clothing");
            return false;
        }

        // ---------------------------------------------------------------
        //  Hook: OnMagazineReload
        //  Introduced in Oxide v2.0.7022.  Reads ammo type from the magazine
        //  definition, avoiding Facepunch Pool allocations.
        // ---------------------------------------------------------------

        /// <summary>
        /// Oxide hook fired when a player's weapon begins a magazine reload cycle.
        /// Returns false to cancel; null to allow.
        /// </summary>
        private object OnMagazineReload(BaseProjectile instance, IAmmoContainer ammoSource, BasePlayer player)
        {
            if (ShouldSkip(player)) return null;
            var ammoType = instance?.primaryMagazine?.ammoType;
            if (ammoType == null) return null;

            var blocked = CheckBlocked(
                ammoType.displayName.english, ammoType.shortname,
                _permanentAmmo, _timedAmmo);
            if (blocked == null) return null;

            SendBlockMessage(player, "AmmoBlocked", blocked.Value);
            LogBlockAttempt(player, ammoType.displayName.english, ammoType.shortname, "ammunition");
            return false;
        }

        // ---------------------------------------------------------------
        //  Hook: CanBuild
        //  Fires BEFORE entity placement.  Returning non-null cancels build.
        //  Replaces the former OnEntityBuilt hook (which fired AFTER placement;
        //  returning false was a no-op in <= 4.0.0).
        // ---------------------------------------------------------------

        /// <summary>
        /// Oxide hook fired when a player attempts to place a deployable item.
        /// Returns false to cancel before entity spawn; null to allow.
        /// </summary>
        private object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            var player = planner?.GetOwnerPlayer();
            if (ShouldSkip(player)) return null;

            var item = planner?.GetItem();
            if (item?.info == null) return null;

            var blocked = CheckBlocked(
                item.info.displayName.english, item.info.shortname,
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

        /// <summary>Plugin reference for the "Duelist" plugin (optional).</summary>
        [PluginReference]
        private Plugin Duel;

        /// <summary>Plugin reference for the "DuelsManager" plugin (optional).</summary>
        [PluginReference("DuelsManager")]
        private Plugin DuelsManager;

        /// <summary>
        /// Returns true if the given player is an NPC and should never be subject
        /// to item block checks.
        ///
        /// Two detection methods are used:
        ///   1. Type check: NPCPlayer is the base class for all NPC entities.
        ///   2. Steam64 range check: valid human Steam IDs are >= 76560000000000000.
        ///      IDs outside that range (excluding 0) are engine-generated NPCs.
        /// </summary>
        private bool IsNPC(BasePlayer player) =>
            player is NPCPlayer ||
            !(player.userID >= 76560000000000000UL || player.userID == 0UL);

        /// <summary>
        /// Returns true if the player is currently engaged in an active duel via a
        /// supported duelling plugin (DuelsManager or Duel).
        ///
        /// Each plugin call is wrapped in a try/catch.  On exception, a warning is
        /// printed to the Oxide console so operators can diagnose API mismatches
        /// rather than silently ignoring failures that would bypass blocking for all
        /// players.
        ///
        /// Detection order:
        ///   1. DuelsManager.IsInDuel(player, player)
        ///   2. Duel.IsPlayerOnActiveDuel(player)
        ///
        /// Returns false when neither plugin is installed.
        /// </summary>
        private bool InDuel(BasePlayer player)
        {
            if (DuelsManager != null)
            {
                try
                {
                    if (DuelsManager.Call<bool>("IsInDuel", player, player))
                        return true;
                }
                catch (Exception ex)
                {
                    PrintWarning($"DuelsManager.IsInDuel call failed: {ex.Message}");
                }
            }

            if (Duel != null)
            {
                try
                {
                    return Duel.Call<bool>("IsPlayerOnActiveDuel", player);
                }
                catch (Exception ex)
                {
                    PrintWarning($"Duel.IsPlayerOnActiveDuel call failed: {ex.Message}");
                }
            }

            return false;
        }

        #endregion

        // ===================================================================
        //  REGION: Messaging
        // ===================================================================
        #region Messaging

        /// <summary>
        /// Sends a block notification to the player in chat.
        ///
        /// The prefix uses _safeColor and _safePrefix, both cached at load time by
        /// ValidateConfig, so no regex or validation executes in this hot path.
        ///
        /// If the block is timed, the remaining countdown is clamped to zero so a
        /// race between block expiry and a hook invocation never yields negative time.
        /// </summary>
        /// <param name="player">The player who attempted to use the blocked item.</param>
        /// <param name="messageKey">Lang key for the category-specific base message.</param>
        /// <param name="permanent">
        /// true  = append PermanentSuffix.
        /// false = append TimedSuffix with countdown.
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
        /// Removes all Unity Rich Text markup tags from the input string.
        /// Matches any sequence of the form &lt;...&gt; regardless of tag name.
        /// Returns the input unchanged if it is null or empty.
        /// </summary>
        private static string StripRichText(string input) =>
            string.IsNullOrEmpty(input) ? input : RichTextRegex.Replace(input, string.Empty);

        #endregion

        // ===================================================================
        //  REGION: Logging
        //
        //  Oxide's LogToFile helper appends _YYYY-MM-DD.txt to the base name,
        //  producing e.g. oxide/logs/ModernItemBlocker_2025-06-01.txt.
        //
        //  Security: all user-controlled strings (player display names, item names)
        //  are passed through SanitizeLog before writing.
        // ===================================================================
        #region Logging

        /// <summary>
        /// Sanitises a user-controlled string for safe inclusion in a log line.
        ///
        /// Characters replaced with a space:
        ///   0x00-0x1F  All ASCII control characters including \n, \r, \t, BEL, NUL.
        ///   0x7F       DEL character.
        ///   |          Pipe field delimiter used in log entries.
        ///
        /// Threat example: a player named "\n2099-01-01 00:00:00 | ADMIN fake_entry"
        /// would inject a forged audit line without this sanitiser.
        /// </summary>
        private static string SanitizeLog(string input) =>
            string.IsNullOrEmpty(input) ? input : SanitizeLogRegex.Replace(input, " ");

        /// <summary>
        /// Writes a timestamped log entry in ISO 8601 UTC format, pipe-separated.
        /// </summary>
        private void LogAction(string message) =>
            LogToFile(LogFileName, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} | {message}", this);

        /// <summary>
        /// Logs a single block event with player name, Steam ID, item names,
        /// category, and player world coordinates.
        /// All user-controlled strings are sanitised by SanitizeLog before writing.
        /// </summary>
        private void LogBlockAttempt(BasePlayer player, string displayName, string shortName, string category)
        {
            if (player == null) return;
            var pos = player.transform.position;
            LogAction(
                $"{SanitizeLog(player.displayName)} ({player.UserIDString}) attempted to use blocked " +
                $"{category} '{SanitizeLog(displayName)} ({SanitizeLog(shortName)})' at " +
                $"{pos.x:F1},{pos.y:F1},{pos.z:F1}");
        }

        #endregion

        // ===================================================================
        //  REGION: Commands
        // ===================================================================
        #region Commands

        /// <summary>
        /// Chat command: /modernblocker [subcommand] [args...]
        /// Delegates to ExecuteCommand using the player's IPlayer interface.
        /// </summary>
        [ChatCommand("modernblocker")]
        private void ChatCommand(BasePlayer player, string command, string[] args) =>
            ExecuteCommand(player.IPlayer, args);

        /// <summary>
        /// Console / RCON command: modernblocker [subcommand] [args...]
        ///
        /// Resolves the caller to IPlayer.  Passes string.Empty (not null) to
        /// FindPlayerById when arg.Connection is null; some Covalence backends throw
        /// ArgumentNullException on a null ID string rather than returning null.
        ///
        /// A null iplayer reaching ExecuteCommand is treated as the server console
        /// and is implicitly granted admin access.
        /// </summary>
        [ConsoleCommand("modernblocker")]
        private void ConsoleCommand(ConsoleSystem.Arg arg)
        {
            var caller  = arg.Player();
            IPlayer iplayer = caller?.IPlayer
                ?? covalence.Players.FindPlayerById(
                       arg.Connection?.userid.ToString() ?? string.Empty);
            ExecuteCommand(iplayer, arg.Args);
        }

        /// <summary>
        /// Central command dispatcher.
        ///
        /// Permission check: a null caller is the server console and is always
        /// allowed.  Any non-null caller must hold the admin permission.
        ///
        /// Sub-commands: help, usage, list, add, remove, reload, loglist.
        /// Unrecognised sub-commands display the usage text.
        /// </summary>
        private void ExecuteCommand(IPlayer caller, string[] args)
        {
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

            switch (args[0].ToLowerInvariant())
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
        /// Handles the add and remove sub-commands.
        ///
        /// Expected argument format:
        ///   args[0] = "add" or "remove"
        ///   args[1] = "permanent" or "timed"
        ///   args[2] = "item", "items", "cloth", "clothes", "clothing", or "ammo"
        ///   args[3..] = item name (joined with spaces, may be multi-word)
        ///
        /// Security: item names are capped at MaxNameLength (256) characters and
        /// must be non-empty after trimming.  On success the config list is mutated,
        /// persisted to disk, and the in-memory HashSets and hook subscriptions are
        /// updated.  The action is written to the audit log.
        /// </summary>
        private void HandleModifyCommand(IPlayer caller, string[] args)
        {
            if (args.Length < 4)
            {
                caller?.Reply(Msg("InvalidArgs"));
                return;
            }

            bool isAdd    = args[0].Equals("add", StringComparison.OrdinalIgnoreCase);
            var  type     = args[1].ToLowerInvariant();
            var  category = args[2].ToLowerInvariant();
            var  name     = string.Join(" ", args.Skip(3)).Trim();

            if (type != "permanent" && type != "timed")
            {
                caller?.Reply(Msg("InvalidArgs"));
                return;
            }

            if (category != "item"    && category != "items"   &&
                category != "cloth"   && category != "clothes" &&
                category != "clothing"&& category != "ammo")
            {
                caller?.Reply(Msg("InvalidArgs"));
                return;
            }

            if (string.IsNullOrEmpty(name))
            {
                caller?.Reply(Msg("InvalidArgs"));
                return;
            }

            if (name.Length > MaxNameLength)
            {
                caller?.Reply(Msg("NameTooLong"));
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
                    return; // Nothing changed; skip save and log.
                default:
                    caller?.Reply(Msg("InvalidArgs"));
                    return;
            }

            SaveConfig();
            BuildHashSets();
            SubscribeHooks();

            var actorName = SanitizeLog(caller?.Name ?? "Server");
            var actorId   = caller?.Id ?? "Server";
            LogAction(
                $"{actorName} ({actorId}) {(isAdd ? "added" : "removed")} " +
                $"'{SanitizeLog(name)}' {(isAdd ? "to" : "from")} {type} {category} list.");
        }

        /// <summary>
        /// Displays the last 20 lines from the most recent ModernItemBlocker log file.
        ///
        /// Oxide creates date-suffixed log files (ModernItemBlocker_YYYY-MM-DD.txt).
        /// This command globs for ModernItemBlocker_*.txt, sorts by last-write time,
        /// and reads the tail of the most recent file.
        ///
        /// Security:
        ///   * All candidate file paths are resolved with Path.GetFullPath and compared
        ///     against the resolved logs directory to prevent path traversal.
        ///   * The file is read in reverse from its end rather than loading it entirely
        ///     into memory.  Reading is capped at LogTailMaxBytes (64 KB) regardless of
        ///     log file size, bounding worst-case memory allocation absolutely.
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

                var files = Directory.GetFiles(resolvedDir, LogFileName + "_*.txt");
                if (files.Length == 0)
                {
                    caller?.Reply("No log file has been created yet.");
                    return;
                }

                // Path traversal guard: keep only paths that resolve inside logsDir.
                var safePaths = files
                    .Where(f => Path.GetFullPath(f)
                        .StartsWith(resolvedDir, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (safePaths.Length == 0)
                {
                    caller?.Reply("No accessible log file found.");
                    return;
                }

                var logFile = safePaths.OrderByDescending(File.GetLastWriteTimeUtc).First();

                // Tail-read: seek from end of file, read at most LogTailMaxBytes bytes,
                // then split into lines and return the last 20.  This avoids loading
                // multi-MB log files into memory for a 20-line tail request.
                string tail;
                using (var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long readLen = Math.Min(fs.Length, LogTailMaxBytes);
                    fs.Seek(-readLen, SeekOrigin.End);
                    var buf = new byte[readLen];
                    int read = fs.Read(buf, 0, buf.Length);
                    tail = Encoding.UTF8.GetString(buf, 0, read);
                }

                var lines = tail.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                var start = Math.Max(0, lines.Length - 20);
                caller?.Reply(string.Join("\n", lines.Skip(start)));
            }
            catch (Exception ex)
            {
                caller?.Reply($"Error reading log: {ex.Message}");
            }
        }

        /// <summary>
        /// Formats and sends all six block lists to the caller.
        /// Item names are stripped of Rich Text tags before inclusion to prevent
        /// stored markup from rendering as formatted text in chat.
        /// Empty lists display as "(none)" (not "&lt;none&gt;").
        /// </summary>
        private void SendLists(IPlayer caller)
        {
            string FormatList(List<string> list)
            {
                if (list.Count == 0) return "(none)";
                return string.Join(", ", list.Select(StripRichText));
            }

            caller?.Reply(string.Format(Msg("ListHeader"),
                FormatList(_config.PermanentBlockedItems),
                FormatList(_config.PermanentBlockedClothes),
                FormatList(_config.PermanentBlockedAmmo),
                FormatList(_config.TimedBlockedItems),
                FormatList(_config.TimedBlockedClothes),
                FormatList(_config.TimedBlockedAmmo)));
        }

        // ---------------------------------------------------------------
        //  ModifyResult: typed return value for ModifyList.
        //  Replaces boolean overloading so callers can distinguish Added /
        //  Removed / NotFound / InvalidList without extra out-parameters.
        // ---------------------------------------------------------------

        private enum ModifyResult { Added, Removed, NotFound, InvalidList }

        /// <summary>
        /// Adds or removes <paramref name="name"/> from the config list identified
        /// by the (type, category) pair.
        ///
        /// Add: case-insensitive duplicate check before inserting prevents the same
        /// name appearing twice with different capitalisation.
        ///
        /// Remove: RemoveAll with case-insensitive comparer removes all capitalisation
        /// variants in one pass.  Returns NotFound when no entry matched.
        /// </summary>
        private ModifyResult ModifyList(bool add, string type, string category, string name)
        {
            var targetList = GetTargetList(type, category);
            if (targetList == null) return ModifyResult.InvalidList;

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
        /// Maps a (type, category) pair to the corresponding config List.
        /// Returns null for any unrecognised combination.
        /// </summary>
        private List<string> GetTargetList(string type, string category)
        {
            switch (type)
            {
                case "permanent":
                    switch (category)
                    {
                        case "item":  case "items":                    return _config.PermanentBlockedItems;
                        case "cloth": case "clothes": case "clothing":  return _config.PermanentBlockedClothes;
                        case "ammo":                                   return _config.PermanentBlockedAmmo;
                    }
                    break;
                case "timed":
                    switch (category)
                    {
                        case "item":  case "items":                    return _config.TimedBlockedItems;
                        case "cloth": case "clothes": case "clothing":  return _config.TimedBlockedClothes;
                        case "ammo":                                   return _config.TimedBlockedAmmo;
                    }
                    break;
            }
            return null;
        }

        #endregion
    }
}
