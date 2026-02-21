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
