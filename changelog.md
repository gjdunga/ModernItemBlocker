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

- `oxide/lang/en/ModernItemBlocker.json` shipped in the repository so operators can
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
