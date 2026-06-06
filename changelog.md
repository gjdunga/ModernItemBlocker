## 4.2.1 - Built Different Compile Fix (ConsoleSystem.Arg.Args StringView Refactor)

### Compile Fix

- **`ConsoleSystem.Arg.Args` retyped from `string[]` to `Facepunch.StringView[]`
  in the Facepunch "Built Different" Rust update (build 2627.285.1,
  2026-06-04)**.  On a server running Built Different with Oxide 2.0.7423, the
  plug-in failed to compile with:

  ```
  ModernItemBlocker - Failed to compile:
  Argument 2: cannot convert from 'Facepunch.StringView[]' to 'string[]'
  ```

  The offending call was `ExecuteCommand(iplayer, arg.Args)` inside the
  `[ConsoleCommand("modernblocker")]` handler.  `ExecuteCommand` operates on
  plain `string[]` because it does case-insensitive `switch` / `Equals` /
  `Trim` work that is awkward to express on `StringView`; converting every
  internal helper to `StringView` would have rippled across the entire
  Commands region.

  The fix is a single boundary conversion: iterate `arg.Args` once, call
  `ToString()` on each `StringView`, and pass the resulting `string[]` to
  `ExecuteCommand`.  `StringView.ToString()` materialises the underlying
  substring, so no information is lost.  A null or zero-length `Args` array
  is propagated as a null `string[]` so the existing "no args -> usage"
  branch in `ExecuteCommand` remains reachable.

  The fix is also **source-compatible with the pre-Built Different
  `string[]` typing**: `string[]` indexing returns `string`, and
  `string.ToString()` is a no-op that returns itself.  Servers on Rust
  builds older than 2627.285.1 therefore do not need a separate plug-in
  build.

### Compatibility

- **Verified compatible with Oxide 2.0.7423** (2026-06-05) and the Facepunch
  **Built Different** Rust update (build **2627.285.1**, 2026-06-04).  None of
  the four blocking hooks used by this plug-in (`CanEquipItem`, `CanWearItem`,
  `OnMagazineReload`, `CanBuild`) changed signature in this release; only the
  `ConsoleSystem.Arg` argument API did.
- Compatibility statement updated across the plug-in source header,
  `manifest.json`, `.umod.yaml`, README header and INSTALL.md.

### Documentation

- INSTALL.md gains an entry in the troubleshooting table mapping the
  `Argument 2: cannot convert ... StringView[] to string[]` error to "upgrade
  to v4.2.1".
- INSTALL.md "Upgrading" section spells out the specific symptom and
  resolution so operators searching for the error message can find the fix
  directly.

---

## 4.2.0 - Path-Traversal Hardening / InDuel Warn-Once / Transform Null Guard / Author Update

### Security Fixes

- **Path-traversal prefix check tightened in `HandleLogListCommand`**: The 4.0.0
  guard kept candidate log files whose `Path.GetFullPath` result began with the
  resolved log directory string.  That string check accepted any sibling
  directory sharing a prefix, e.g. `/srv/server/oxide/logs-evil/file.txt`
  satisfied `StartsWith("/srv/server/oxide/logs")`.  In the current code path
  `Directory.GetFiles(resolvedDir, ...)` does not enumerate sibling
  directories, so exploitation required a symlink inside the log directory and
  was therefore defence-in-depth only; nonetheless the prefix is now suffixed
  with `Path.DirectorySeparatorChar` before the comparison so the guard fails
  closed if the enumeration semantics ever change.

- **`LogBlockAttempt` null-guards `player.transform`**: If a player disconnects
  in the narrow window between a block hook firing and `LogBlockAttempt`
  running, `player.transform` can be null.  Reading `.position` would throw
  `NullReferenceException` and abort the audit-log write entirely.  Position
  is now read defensively; when the transform is unavailable the entry records
  `at unknown` instead of failing to write.

### Performance / Operability

- **`InDuel` exception logging is now warn-once per duelling plugin**: 4.1.3
  promoted silent `catch { }` blocks to `PrintWarning` so a broken duel-plugin
  integration would not silently bypass all blocking.  On a busy server with a
  permanently incompatible duel plugin, however, that produced hundreds of
  identical warnings per second.  Each call site now latches a private bool
  after the first warning; subsequent invocations of the same call site stay
  silent until the next plugin load (the latch is cleared in `Unload()` so
  operators still see the issue resurface on `oxide.reload`).

### Compatibility

- **Log directory resolved from `Interface.Oxide.LogDirectory` when set**,
  falling back to `Path.Combine(RootDirectory, "oxide", "logs")` for older
  Oxide builds that do not expose the property.  This insulates the plug-in
  against future Oxide directory-layout changes.

- **Verified against Oxide 2.0.7214 (2026-05-17)**.  No hook signature changes
  affecting `CanEquipItem`, `CanWearItem`, `OnMagazineReload`, or `CanBuild`
  between 2.0.7022 and 2.0.7214; the plug-in continues to work without
  modification on every release in that range.

### Metadata

- **Author updated** to "Gabriel Dungan (DunganSoft Technologies)" in the
  `[Info]` attribute, `manifest.json`, `.umod.yaml`, `LICENSE`, `LICENSE.md`,
  README credits line, and changelog header.  GitHub handle `gjdunga` is
  preserved under the new `author_github` key in the metadata files.

- **Version bumped** to 4.2.0 in plug-in source header, `[Info]` attribute,
  `manifest.json`, `.umod.yaml`, README header and INSTALL.md.

### Documentation

- **`INSTALL.md` added** with fresh-install, upgrade-from-4.1.x,
  permissions-setup, verification, uninstall and troubleshooting sections.
- **`CONTRIBUTING.md` added** with branch model, coding-standards (target
  framework, hot-path rules, lang-key policy), pull-request checklist and a
  security-disclosure policy.
- **README updated**: version/author/license header block added at the top,
  installation section points to `INSTALL.md` for the full walkthrough, new
  `Contributing` section points to `CONTRIBUTING.md`, credits line updated.

---

## 4.1.4 - Log Name Null-Coerce Fix

### Bug Fix

- **Item name double-read eliminated in hook handlers**: `CheckBlocked` received null
  guards for `displayName.english` and `shortname` in 4.1.2 so that custom or modded
  items with a null display name do not throw `ArgumentNullException` during HashSet
  lookups.  However, each hook handler (`CanEquipItem`, `CanWearItem`,
  `OnMagazineReload`, `CanBuild`) read those fields a second time when passing them
  to `LogBlockAttempt`.  On a custom item with a null `displayName.english`,
  `SanitizeLog` received null, returned null, and string interpolation produced the
  literal text `"null"` in the audit log entry rather than an empty string.

  The fix extracts `displayName.english` and `shortname` into local `dn` / `sn`
  variables at the top of each block in each hook handler, null-coercing to
  `string.Empty` at that point.  Both `CheckBlocked` and `LogBlockAttempt` then
  receive the same already-coerced values.  The null-coerce logic in `CheckBlocked`
  itself is retained as a defence-in-depth guard for any future callers.

### Compatibility

- Verified compatible with Oxide 2.0.7182 (2026-03-25).  No hook signature changes
  affecting `CanEquipItem`, `CanWearItem`, `OnMagazineReload`, or `CanBuild` were
  introduced between 2.0.7022 and 2.0.7182.

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

