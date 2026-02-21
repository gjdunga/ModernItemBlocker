# ModernItemBlocker

ModernItemBlocker is a Rust server plug-in for the uMod/Oxide framework that allows administrators to block specific items, clothing and ammunition either for a limited time after each wipe or permanently until removed. The plug-in is designed to be configurable, efficient and easy to manage via in-game chat, the F1 developer console, RCON or the server console.

---

## Features

**Timed and permanent blocks** - Define separate lists of items, clothes and ammunition that are blocked for a configurable number of hours after each wipe (timed) or blocked indefinitely across wipes (permanent). Timed blocks automatically expire once the configured duration has passed.

**Configuration file** - All settings and lists are stored in `oxide/data/ModernItemBlockerConfig.json`. The plug-in will create this file on first run and populate it with sensible defaults. You can edit it manually and reload it in game with `/modernblocker reload`.

**Administrative commands** - Manage blocked items directly from the game or via RCON without editing files. The `/modernblocker` command supports listing the current block lists, adding new entries and removing existing ones.

**Permission system** - Use the standard uMod permission system to control who can use the management commands or bypass blocks entirely. See the Permissions section below.

**Localization** - All player-facing messages are defined via the Lang API and can be translated or customized by adding language files under `oxide/lang`. A default English file is provided at `oxide/lang/en/ModernItemBlocker.json`.

**Wipe detection** - The plug-in listens for the `OnNewSave` hook to detect wipes and automatically resets the timed block timer.

**Optional duel detection** - If a duelling plug-in (such as Duelist or Duels Manager) is installed, players in active duels are exempt from item blocking. ModernItemBlocker automatically detects these plug-ins and skips block checks when a player is currently duelling.

**Logging and audit trail** - The plug-in records all administrative changes to the block lists and any attempts by players to use or deploy blocked items. Each log entry includes the UTC timestamp, the player's name and Steam ID, the item involved and the approximate coordinates. Logs are written to `oxide/logs/ModernItemBlocker_YYYY-MM-DD.txt` and can be viewed in game using `/modernblocker loglist`.

---

## Installation

1. Ensure your server is running uMod/Oxide version **v2.0.7022 or newer** (Rust Naval Update).
2. Download `ModernItemBlocker.cs` and place it into the `oxide/plugins` directory on your Rust server.
3. Start the server or reload the plug-in with `oxide.reload ModernItemBlocker`. The plug-in will generate a configuration file at `oxide/data/ModernItemBlockerConfig.json`.

---

## Commands

All commands can be run in chat (prefixed with `/`), in the F1 client console, via RCON or in the server console. When using chat, omit the angle brackets (`<` and `>`); they mark placeholders.

### `/modernblocker list`
Displays all blocked items, clothes and ammunition in both the permanent and timed categories.

### `/modernblocker add <permanent|timed> <item|cloth|ammo> <name>`
Adds an entry to the specified list. Examples:
```
/modernblocker add permanent item Assault Rifle
/modernblocker add timed ammo High Velocity Rocket
```
Item names are case-insensitive and may be the English display name or the engine shortname.

### `/modernblocker remove <permanent|timed> <item|cloth|ammo> <name>`
Removes an entry from the specified list. Names are matched case-insensitively.

### `/modernblocker reload`
Reloads the configuration from `oxide/data/ModernItemBlockerConfig.json` without unloading the plug-in. Use this after editing the file by hand.

### `/modernblocker loglist`
Displays the last 20 entries from the plug-in's log file. Restricted to users with the `modernitemblocker.admin` permission. To review the full log, open `oxide/logs/ModernItemBlocker_YYYY-MM-DD.txt` on your server.

### `/modernblocker help`
Displays a summary of available commands and usage.

---

## Configuration

The configuration file is stored at `oxide/data/ModernItemBlockerConfig.json`. An example:

```json
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
```

| Field | Description |
|---|---|
| `Block Duration (Hours) after Wipe` | Hours after a wipe during which timed blocks are enforced. Set to 0 to disable. |
| `Permanent Blocked Items / Clothes / Ammo` | Items blocked until explicitly removed by an admin. |
| `Timed Blocked Items / Clothes / Ammo` | Items blocked for the configured duration after each wipe. |
| `Bypass Permission` | Permission node that exempts a player from all blocks. |
| `Admin Permission` | Permission node required to use management commands. |
| `Chat Prefix` / `Chat Prefix Color` | Prefix text and 6-digit hex colour shown in block notifications. |

After editing the file, run `/modernblocker reload` to apply changes without restarting.

---

## Permissions

Grant permissions using the `oxide.grant` command:

```
oxide.grant group admin modernitemblocker.admin
oxide.grant user 76561198012345678 modernitemblocker.bypass
```

| Permission | Description |
|---|---|
| `modernitemblocker.admin` | Allows use of all `/modernblocker` commands. Server console and RCON are implicitly admin. |
| `modernitemblocker.bypass` | Exempts a player from all item, clothing and ammunition blocks. |

---

## Logging

ModernItemBlocker writes dated log files to `oxide/logs/ModernItemBlocker_YYYY-MM-DD.txt`. The log captures:

- **Administrative actions** - When an admin adds or removes a block list entry: timestamp, display name, Steam ID, action taken.
- **Blocked usage attempts** - When a player tries to use a blocked item: timestamp, display name, Steam ID, item display name and shortname, world coordinates.

All user-controlled strings (player names, item names) are sanitised before writing to prevent log injection.

Use `/modernblocker loglist` in chat, RCON, or the F1 console to view the last 20 entries without opening the file.

---

## Duels Integration

ModernItemBlocker optionally integrates with duelling plug-ins so players in an active duel are not penalized by block lists. No configuration is required; install one of the supported plug-ins alongside ModernItemBlocker:

- **Duelist** - Full duelling system with betting, queues, ranked ladder and UI.
- **Duels Manager** - Timer-based duels with API method `IsInDuel(player, player)`.

If neither plug-in is installed the duel check has no effect.

---

## Localization

All player-facing messages use the uMod Lang API. To translate or customize messages, edit or copy `oxide/lang/en/ModernItemBlocker.json` and create a parallel file for your language code (e.g. `oxide/lang/fr/ModernItemBlocker.json`). Use the same keys as the English file.

---

## License

This project is licensed under the MIT License. See [LICENSE.md](LICENSE.md) for details.

## Credits

Written by Gabriel J. Dungan (gjdunga).
