## 4.1.3 - Unload Hook / InDuel Logging / Log Tail Fix / Languages / Structure Cleanup

### Security Fixes

- **`Unload()` hook added**: Oxide automatically unsubscribes hooks when a plugin
  unloads, but an explicit `Unsubscribe` pass in `Unload()` prevents late-arriving
  events from dispatching into a partially-torn-down instance.  All six HashSets are
  also cleared in `Unload()` to release object references for immediate GC rather
  than waiting for the next collection cycle.

- **`InDuel()` exception logging**: Both duelling plugin call-sites previously used
  empty `catch { }` blocks, silently swallowing all exceptions.  A plugin API
  mismatch (wrong signature, version change) would cause the block check to always
  pass `InDuel() == false`, granting implicit duel-bypass to all players without any
  server-side indication that something was wrong.  Each catch block now calls
  `PrintWarning(pluginName + " call failed: " + ex.Message)` so operators can
  diagnose duel plugin compatibility issues from the console log.

- **`HandleLogListCommand()` tail-read replaces `File.ReadAllLines()`**:
  `File.ReadAllLines()` loads the entire file into memory before splitting.  On a
  busy server the daily log can reach tens of megabytes; a single `loglist` command
  would allocate that entire buffer.  The new implementation opens a `FileStream`,
  seeks to `max(0, fileLength - 64KB)` from the end, reads only that tail slice,
  splits into lines, and returns the last 20.  Memory cost is capped at
  `LogTailMaxBytes` (64 KB) regardless of log file size.

- **`ConsoleCommand()` null-string guard for `FindPlayerById`**: When `arg.Connection`
  is null (server console or bare RCON call), `arg.Connection?.userid.ToString()`
  evaluates to `null`.  Some Covalence backend implementations throw
  `ArgumentNullException` on a null ID string rather than returning `null` gracefully.
  The call now passes `string.Empty` as the fallback: `arg.Connection?.userid.ToString()
  ?? string.Empty`.

### Verified No-Op (No Code Change Required)

- **No `(bool, DateTime?, bool)?` value tuples were present in the codebase**.
  `CheckBlocked` returns `bool?` (a simple `Nullable<bool>`), which does not depend
  on `System.ValueTuple` and builds correctly on all Oxide build servers.  The audit
  is documented here to confirm the check was performed.

### Additions

- **Russian (`ru`), Spanish (`es`), and Latin (`la`) language files** added to
  `oxide/lang/`.  All three cover every lang key defined in `LoadDefaultMessages()`.

### Repository Structure

- **`ModernItemBlocker.cs` removed from repository root**.  The canonical plugin
  path for umod submission is `oxide/plugins/ModernItemBlocker.cs`.  A duplicate
  root copy violated the umod structure requirement ("no .cs code in the root") and
  could cause confusion about which copy is authoritative.

- **`manifest.json` updated**: removed the now-deleted `plugin-mirror` root entry,
  added lang entries for `ru`, `es`, and `la`.  `oxide/plugins/ModernItemBlocker.cs`
  is now the sole `plugin` role entry.

---

## 4.1.2 - Security Hardening / Info Title Fix / Null Guard in CheckBlocked

### Security Fixes

- **[Info] title corrected to "Modern Item Blocker"**: The umod style guide requires
  plugin titles in the `[Info]` attribute to use human-readable, space-separated words
  so the name renders correctly in the Oxide server console.  The previous value
  `"ModernItemBlocker"` (no spaces) violated this requirement.

- **SanitizeLog now strips all ASCII control characters**: The previous implementation
  only replaced `\n`, `\r`, and `|`.  Null bytes (`\x00`), BEL (`\x07`), DEL (`\x7F`),
  and other control characters in the range 0x00-0x1F could still be injected into log
  files, corrupting automated parsers and terminal emulators.  A single compiled Regex
  (`[\x00-\x1F\x7F|]`) now replaces the entire control-character range and the pipe
  delimiter in one pass, replacing all matches with a space.

- **Null guard added in CheckBlocked for displayName and shortName**: Custom or modded
  items can return a null `displayName.english` string.  Passing `null` to
  `HashSet<string>.Contains` throws `ArgumentNullException`.  Both names are now
  null-coerced to `string.Empty` before the HashSet lookups.

### Bug Fix

- **SendLists empty-list placeholder changed from `<none>` to `(none)`**: Unity's Rich
  Text renderer parses `<none>` as an unknown markup tag and may silently discard it,
  producing blank output when a list is empty.  Parentheses are unambiguous plain text.

### Repository

- `oxide/lang/en/ModernItemBlocker.json` actually added to repository (was listed in
  manifest and mentioned in the 4.1.1 changelog as shipped, but was absent from the repo).
- `oxide/data/ModernItemBlockerConfig.json` sample added to repository (was listed in
  manifest but absent from the repo).
- `manifest.json` `files` list: removed the `oxide/plugins/ModernItemBlocker.cs`
  plugin-mirror entry (the file is not present in the repo; the canonical path is
  `ModernItemBlocker.cs` at the repository root).

---

## 4.1.1 - Security Hardening / NULL-list Guard / Version Corrections

### Security Fixes

- **Log injection prevention**: `player.displayName` and item names are now sanitised
  via a `SanitizeLog` helper before being written to the audit log.  Characters that
  could forge a new log line (`\n`, `\r`) or corrupt the pipe-delimited format (`|`)
  are replaced with a space.  Previously a player named
  `\n2099-01-01 00:00:00 | ADMIN fake_entry` could inject arbitrary lines into the
  audit trail.

- **Null list guard**: `ValidateConfig` now null-coerces all six block-list fields
  using `??=`.  A config file containing `"Permanent Blocked Items": null` (or any
  list set to JSON null) previously caused `BuildHashSets` to throw
  `NullReferenceException` on startup, preventing the plugin from loading.  The
  plugin now replaces null fields with empty lists and continues normally.

- **Item name length cap**: The `add`/`remove` command parser now rejects names
  longer than 256 characters.  Arbitrarily long names could bloat the config file
  and log entries to any size.  A new `NameTooLong` lang key provides feedback.

- **Rich Text stripped from list output**: `SendLists` now runs `StripRichText` on
  every entry before including it in the chat reply.  Previously a stored entry
  containing `<color=red>text</color>` would render as coloured markup in chat.

### Bug Fixes

- **Spurious `SaveConfig()` removed from `OnNewSave`**: `_blockEnd` is a runtime
  field and is not persisted in the config file; it is recomputed from
  `SaveRestore.SaveCreatedTime` on every server start.  Calling `SaveConfig()` on
  wipe was a no-op that wrote an unchanged config to disk and misleadingly implied
  that the wipe timestamp was being serialised.

- **README minimum Oxide version corrected**: Installation section previously stated
  `v2.0.6599`; the plugin actually requires `v2.0.7022+` (Rust Naval Update hook
  signatures).

### Additions

- `oxide/lang/en/ModernItemBlocker.json` added to repository listing so operators can
  see all translatable keys without reading source code.

- `NameTooLong` lang key added (used by the new name-length cap).

### Repository / Metadata

- `.umod.yaml`: `tags` field added (`items`, `administration`, `permissions`,
  `wipe`, `blocker`).
- `manifest.json`: `compatibility.oxide` corrected from `"2.x"` to `">=2.0.7022"`.
  `oxide/lang/en/ModernItemBlocker.json` added to `files` list.

---

## 4.1.0 - Deployable Blocking Fix / Log Read Fix / Remove Feedback Fix

### Bug Fixes

- **CanBuild replaces OnEntityBuilt**: `OnEntityBuilt` fires *after* an entity is
  placed; returning `false` from it is a no-op in the Rust engine.  All deployable
  blocking in 4.0.0 was silently non-functional.  `CanBuild(Planner, Construction,
  Construction.Target)` fires *before* placement and its return value is respected.
  Deployables are now actually blocked.

- **loglist now reads the correct file**: Oxide's `LogToFile` creates files named
  `ModernItemBlocker_YYYY-MM-DD.txt`.  The command was looking for
  `ModernItemBlocker.txt` which never exists.  The command now globs for
  `ModernItemBlocker_*.txt`, sorts by last-write time and reads the most recent file.

- **remove command correctly reports NotFound**: Previously, removing a name that was
  not in a list returned "Removed" anyway.  `ModifyList` now returns a typed
  `ModifyResult` enum; the caller reports "Removed", "Added", or "NotFound"
  accurately.  The `NotFound` lang key is no longer dead code.

### Performance

- Hex colour re-validation removed from the `SendBlockMessage` hot path.
  `ValidateConfig` caches `_safeColor` and `_safePrefix` at load time; no regex
  executes on every block event.

- `CanBuild` subscription replaces the now-removed `OnEntityBuilt` subscription in
  `SubscribeHooks`, keeping hook overhead consistent.

### Internal

- `ModifyResult` enum (Added / Removed / NotFound / InvalidList) replaces boolean
  return from `ModifyList`, eliminating overloaded boolean semantics.

- `ToggleHook` helper reduces `Subscribe` / `Unsubscribe` call sites to one.

- Added `BuildBlocked` lang key for the new deployable block message.

- Verbose XML doc comments added throughout for maintainability.

---

## 4.0.0 - Oxide v2.0.7022 / Naval Update Compatibility

### Breaking Changes
- **CanEquipItem** hook updated to `(PlayerInventory, Item, int targetSlot)` signature.
- **CanWearItem** hook updated to `(PlayerInventory, Item, int targetSlot)` signature.
- **OnReloadMagazine** (legacy 2-param hook) removed. Replaced by **OnMagazineReload**
  `(BaseProjectile, IAmmoContainer, BasePlayer)`.

### Improvements
- Ammo blocking reads `primaryMagazine.ammoType` directly, eliminating Pool allocations.
- Uses `GetBaseEntity()` for modern Oxide API compliance.
- Consolidated block-checking into `CheckBlocked()` and `ShouldSkip()` helpers.
- Automatic hook subscribe/unsubscribe when block lists are empty.

### Security
- Config validation: rejects negative `BlockDurationHours`, validates hex colour,
  strips Rich Text from chat prefix.
- Log file path traversal guard in `loglist` command.
- Timed block countdown clamped to `TimeSpan.Zero`.

### Removed
- Unused imports, legacy `OnReloadMagazine` hook, Facepunch.Pool inventory scanning.

---

## 3.0.8

Bumped version to 3.0.8. Added `OnMagazineReload` hook for forward compatibility while
retaining `OnReloadMagazine` for older builds.
