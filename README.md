ModernItemBlocker

ModernItemBlocker is a Rust server plug‑in for the uMod/Oxide framework that allows administrators to block specific items, clothing and ammunition either for a limited time after each wipe or permanently until removed. The plug‑in is designed to be configurable, efficient and easy to manage via in‑game chat, the F1 developer console, RCON or the server console.

Features

Timed and permanent blocks – Define separate lists of items, clothes and ammunition that are blocked for a configurable number of hours after each wipe (timed) or blocked indefinitely across wipes (permanent). Timed blocks automatically expire once the configured duration has passed.

Configuration file – All settings and lists are stored in oxide/data/ModernItemBlockerConfig.json. The plug‑in will create this file on first run and populate it with sensible defaults. You can edit it manually and reload it in game with /modernblocker reload.

Administrative commands – Manage blocked items directly from the game or via RCON without editing files. The /modernblocker command supports listing the current block lists, adding new entries and removing existing ones.

Permission system – Use the standard uMod permission system to control who can use the management commands or bypass blocks entirely. See the Permissions section below.

Localization – All player‑facing messages are defined via the Lang API and can be translated or customized by adding language files under oxide/lang.

Wipe detection – The plug‑in listens for the OnNewSave hook to detect wipes and automatically resets the timed block timer.

Optional Duel detection – If the optional Duel plug‑in is installed, players in active duels are exempt from item blocking.

Installation

Ensure your server is running uMod/Oxide version v2.0.6599 or newer.

Download ModernItemBlocker.cs and place it into the oxide/plugins directory on your Rust server.

Start the server or reload the plug‑in with oxide.reload ModernItemBlocker. The plug‑in will generate a configuration file at oxide/data/ModernItemBlockerConfig.json.

Commands

All commands can be run in chat (prefixed with /), in the F1 client console, via RCON or in the server console. If using chat, do not include brackets (< and >); they indicate placeholders.

/modernblocker list

Displays all blocked items, clothes and ammunition in both the permanent and timed categories.

/modernblocker add <permanent|timed> <item|cloth|ammo> <name>

Adds an entry to the specified list. For example:

/modernblocker add permanent item Assault Rifle
/modernblocker add timed ammo High Velocity Rocket


Item names are case‑insensitive and may be specified by their English display name or short name.

/modernblocker remove <permanent|timed> <item|cloth|ammo> <name>

Removes an entry from the specified list. Names are matched case‑insensitively.

/modernblocker reload

Reloads the configuration from oxide/data/ModernItemBlockerConfig.json without unloading the plug‑in. Use this after editing the file by hand.

/modernblocker help

Displays a summary of the available commands and usage.

Configuration

The configuration file is stored at oxide/data/ModernItemBlockerConfig.json. An example configuration is shown below:

{
  "Block Duration (Hours) after Wipe": 30,
  "Permanent Blocked Items": [],
  "Permanent Blocked Clothes": [],
  "Permanent Blocked Ammo": [],
  "Timed Blocked Items": [],
  "Timed Blocked Clothes": [],
  "Timed Blocked Ammo": [],
  "Bypass Permission": "modernitemblocker.bypass",
  "Admin Permission": "modernitemblocker.admin",
  "Chat Prefix": "[ModernBlocker]",
  "Chat Prefix Color": "#f44253"
}


Block Duration (Hours) after Wipe – Number of hours after a wipe (i.e. after OnNewSave) during which timed blocks are enforced.

Permanent Blocked Items / Clothes / Ammo – Lists of names or short names of items, clothing and ammunition that are always blocked until removed.

Timed Blocked Items / Clothes / Ammo – Lists of names or short names of items that are blocked for the duration specified above after each wipe.

Bypass Permission – Permission string granted to players who should be exempt from all blocks.

Admin Permission – Permission string required to use the management commands.

Chat Prefix / Chat Prefix Color – Prefix and color used for messages sent by the plug‑in.

After editing the configuration, use /modernblocker reload to apply your changes without restarting the server.

Permissions

The plug‑in uses the uMod permission system. To grant a permission, use the oxide.grant command. For example:

oxide.grant group admin modernitemblocker.admin
oxide.grant user 76561198012345678 modernitemblocker.bypass


modernitemblocker.admin – Allows a player to use /modernblocker commands to list, add, remove and reload configurations. The server owner and RCON are implicitly considered admins.

modernitemblocker.bypass – Exempts a player from all item, clothing and ammunition blocks. Ideal for moderators or special roles.

Localization

All player‑facing messages are defined using the uMod Lang API. The default English messages are registered by the plug‑in. To provide translations or customize messages, create a file named ModernItemBlocker.<language>.json in your server’s oxide/lang directory (for example, oxide/lang/fr/ModernItemBlocker.json for French). Use the same keys as the English messages and provide your own text.

License

This project is licensed under the MIT License. See LICENSE.md
 for details.

Credits

Written by Gabriel J. Dungan (gjdunga).
