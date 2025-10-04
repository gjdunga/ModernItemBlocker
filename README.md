# ModernItemBlocker
(RUST) (uMod) (C#) Allows one to block usage/crafting of certain items.  This can be done per wipe, per person and for a timed amount of time. 

I have modernized the code and added new functionality so that administrators can permanently block items for an entire wipe and have those blocks persist across wipes until explicitly changed. Key improvements include:

Persistent blocking lists: Added configuration fields for “List of permanently blocked items/clothes/ammunition” alongside the existing timed lists. These lists persist across wipes and are enforced even when the timed block has expired.

Admin management commands: Implemented chat/console commands with sub‑commands list, add and remove. Administrators (or those with the permission) can view, add or remove items from the permanent block lists without touching the configuration file.

Permission registration: Registered a new permission in addition to the existing bypass and refresh permissions.

Performance and security: Converted all block lists to case‑insensitive HashSets for faster lookups. Added checks to avoid null references, early returns and unnecessary GUI operations. Added new localized messages for permanently blocked items, clothing and ammo. Replaced outdated WWW web requests with modern async download logic.

Unified blocking logic: Updated CanEquipItem, CanWearItem and ammunition hooks to always check permanent lists first, then the timed lists if the block timer is active. Added new messages indicating permanent blocks when relevant.

Key features & usage

Timed and permanent blocks: The configuration separates “Timed” and “Permanent” lists for items, clothing and ammunition. Timed lists are enforced after each wipe for a configurable number of hours. Permanent lists persist across wipes until explicitly removed.

Admin commands accessible everywhere: The command /modernblocker works from chat, the F1 client console, RCON or the server console. Use it to list, add or remove entries from either the timed or permanent lists:

/modernblocker list – shows all blocked items, clothes and ammo.

/modernblocker add <permanent|timed> <item|cloth|ammo> <name> – adds a name to the chosen list.

/modernblocker remove <permanent|timed> <item|cloth|ammo> <name> – removes an entry.

/modernblocker help – shows usage instructions.

Permissions:

modernblocker.admin – required to execute /modernblocker commands. Server admins automatically have access, but you can grant this to other users.

modernblocker.bypass – exempt players from all blocks (useful for moderators or special roles).

Messages & UI: When a player attempts to use a blocked item, clothing or ammo, they receive a chat message with either a timer (“x days hh:mm:ss until unblock”) or a notice that the item is permanently blocked until removed.

Automatic wipe detection: The plug‑in listens for OnNewSave to reset the timed block window based on the configured duration.

You can drop the compiled script below into your server’s oxide/plugins folder and adjust the default configuration as needed.

The ModernItemBlocker plugin has been updated so that it now stores its configuration in a separate JSON file and provides an in‑game reload command.

What’s new

External configuration file – Instead of using the default uMod config system, the plugin now saves and loads its settings from a file named ModernItemBlockerConfig.json in your server’s oxide/data directory. When the plugin first runs, it will generate this file if it doesn’t exist.

Automatic persistence – Whenever you add or remove entries using /modernblocker, the updated configuration is written to this data file. Wipe events also trigger a save.

Reload command – Use /modernblocker reload (in chat, RCON or the server console) to reload the configuration from disk without reloading the entire plugin. This lets you edit ModernItemBlockerConfig.json by hand and apply changes on the fly.

Updated usage message – The help text now includes the reload command.

