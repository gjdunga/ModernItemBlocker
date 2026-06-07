# Installing ModernItemBlocker

**Version:** 4.2.6
**Author:** Gabriel Dungan, DunganSoft Technologies
**License:** GPL-3.0

This document covers installation, upgrade, configuration, removal and verification of ModernItemBlocker on an Oxide-managed Rust server.

---

## Requirements

| Component | Minimum | Verified |
|---|---|---|
| Rust server | Current Facepunch release | **Built Different** build 2627.285.1 (2026-06-04) |
| Oxide / uMod | **v2.0.7022** (Rust Naval Update) | **v2.0.7423** (2026-06-05) |
| .NET Framework | 4.7.2 (bundled with Oxide) | 4.7.2 |
| Disk space | < 1 MB plug-in + log space | n/a |

If your server runs Oxide older than v2.0.7022 the plug-in **will not load**: it relies on the post-Naval `CanEquipItem(PlayerInventory, Item, int)`, `CanWearItem(PlayerInventory, Item, int)`, `OnMagazineReload(BaseProjectile, IAmmoContainer, BasePlayer)` and `CanBuild(Planner, Construction, Construction.Target)` hook signatures. Upgrade Oxide first.

---

## Fresh install

1. Stop the Rust server (or be ready to reload at the end).
2. Copy `oxide/plugins/ModernItemBlocker.cs` from this repository into your server's `oxide/plugins/` directory.
3. (Optional) Copy any of the language files you want from this repo's `oxide/lang/<code>/ModernItemBlocker.json` into your server's matching `oxide/lang/<code>/` directory. English (`en`), Russian (`ru`), Spanish (`es`) and Latin (`la`) are provided.
4. Start the server, or reload the plug-in: `oxide.reload ModernItemBlocker`.
5. On first load the plug-in writes a default configuration to `oxide/config/ModernItemBlocker.json`. Edit it (see `README.md` for the field reference) and run `/modernblocker reload` to apply changes without a server restart.

---

## Upgrading from 4.1.x or 4.2.0 to 4.2.1

The 4.2.1 release is **drop-in compatible** with 4.1.x and 4.2.0 configurations and language files. No config migration is required.

If your server console reports the following compile error after pulling the Built Different Rust update (build 2627.285.1, 2026-06-04) on Oxide 2.0.7423, **upgrading to 4.2.1 resolves it**:

```
ModernItemBlocker - Failed to compile:
Argument 2: cannot convert from 'Facepunch.StringView[]' to 'string[]'
```

The error is caused by Facepunch retyping `ConsoleSystem.Arg.Args` from `string[]` to `Facepunch.StringView[]` as part of the StringView console-argument refactor in Built Different. 4.2.1 materialises the StringView array to a plain `string[]` at the boundary so the rest of the plug-in keeps working unchanged. The fix is source-compatible with the older `string[]` typing, so the same plug-in file loads on both pre-Built-Different and Built-Different servers.

To upgrade:

1. Replace `oxide/plugins/ModernItemBlocker.cs` with the new file.
2. Run `oxide.reload ModernItemBlocker` (or restart the server).
3. Confirm the version printed in the console matches `4.2.6`:

   ```
   [Modern Item Blocker] Loaded plugin Modern Item Blocker v4.2.6 by gjdunga
   ```

If you previously edited language files, they remain compatible. New keys, if any, will fall back to the English default until you translate them.

---

## Permissions setup

After loading the plug-in, grant the two permission nodes as needed:

```text
oxide.grant group admin modernitemblocker.admin
oxide.grant user 76561198012345678 modernitemblocker.bypass
```

- `modernitemblocker.admin` — required to run any `/modernblocker` management command. Server console and RCON are implicitly admin.
- `modernitemblocker.bypass` — exempts a player from all blocks.

---

## Verifying the install

After a successful load you should see, in `oxide/logs/oxide.log`:

```
Loaded plugin Modern Item Blocker v4.2.6 by gjdunga
```

Run `/modernblocker list` in chat to confirm the command pipeline is functional. With the default config (all six lists empty) the output is six `(none)` rows.

To verify wipe handling, look in the console after `OnNewSave` fires (start of a new wipe):

```
Wipe detected. Timed block window ends YYYY-MM-DD HH:MM:SS UTC.
```

---

## Uninstalling

1. `oxide.unload ModernItemBlocker` in the console (or stop the server).
2. Remove `oxide/plugins/ModernItemBlocker.cs`.
3. (Optional) Delete `oxide/config/ModernItemBlocker.json` and `oxide/lang/*/ModernItemBlocker.json` if you do not plan to reinstall.
4. (Optional) `oxide.revoke` the two permission nodes from any groups/users.

The plug-in does not register any timers, save handlers, or external resources, so unloading is clean.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Loaded plugin` line never appears | Oxide failed to compile the `.cs` file | Check `oxide/logs/oxide.log` for the compile error |
| `Argument 2: cannot convert from 'Facepunch.StringView[]' to 'string[]'` | Built Different (2026-06-04) retyped `ConsoleSystem.Arg.Args` from `string[]` to `Facepunch.StringView[]` | Upgrade to **v4.2.1** or newer |
| `Generating default configuration` printed every reload | Data config file invalid JSON or missing | Validate the JSON; the plug-in falls back to defaults |
| `ChatPrefixColor '...' is invalid` warning | Hex colour did not match `^#[0-9A-Fa-f]{6}$` | Use a 6-digit hex with leading `#` (e.g. `#f44253`) |
| `Rich Text tags were stripped from ChatPrefix` warning | Prefix contained `<...>` markup | Set a plain-text prefix; Rich Text is stripped automatically |
| `DuelsManager.IsInDuel call failed` warning | Installed duel plugin's API signature differs from expected | Update or remove the offending plugin; the warning fires once per load and is suppressed thereafter |
| `Log directory not found` reply from `/modernblocker loglist` | Oxide's log directory was deleted at runtime | Recreate `oxide/logs/`; Oxide will repopulate on the next log write |

---

## Reporting bugs and security issues

Bugs and feature requests: open an issue at <https://github.com/gjdunga/ModernItemBlocker/issues>.

Security vulnerabilities: see the **Security disclosure** section of [CONTRIBUTING.md](CONTRIBUTING.md).
