## 4.0.0 - Oxide v2.0.7022 / Naval Update Compatibility

### Breaking Changes
- **CanEquipItem** hook updated to `(PlayerInventory, Item, int targetSlot)` signature required by Oxide v2.0.7022+.
- **CanWearItem** hook updated to `(PlayerInventory, Item, int targetSlot)` signature required by Oxide v2.0.7022+.
- **OnReloadMagazine** (legacy 2-param hook) removed. Replaced entirely by **OnMagazineReload** `(BaseProjectile, IAmmoContainer, BasePlayer)`.

### Improvements
- Ammo blocking now reads `primaryMagazine.ammoType` directly from the weapon instead of searching player inventory, eliminating Facepunch Pool allocations.
- Uses `GetBaseEntity()` instead of `GetComponent<BasePlayer>()` for modern Oxide API compliance.
- Consolidated duplicate block-checking logic into `CheckBlocked()` and `ShouldSkip()` helpers.
- Automatic hook subscribe/unsubscribe when block lists are empty (reduces server overhead).
- Extracted `HandleModifyCommand()`, `HandleLogListCommand()`, `GetTargetList()` for cleaner command handling.
- Input validation for `add`/`remove` commands rejects invalid type/category before modifying lists.
- Added `NotFound` lang key for future use.

### Security
- Config validation: rejects negative `BlockDurationHours`, validates hex color format, strips Rich Text tags from chat prefix.
- Chat messages sanitize the prefix color against a strict hex regex to prevent Rich Text injection.
- Log file path traversal guard in `loglist` command.
- Timed block countdown clamps to `TimeSpan.Zero` instead of showing negative values.
- NPC check uses `UL` suffix for unsigned comparison on `userID`.
- Null guard on `_config` in `OnNewSave` for early-wipe edge case.

### Removed
- Unused imports: `Oxide.Game.Rust.Cui`, `Oxide.Game.Rust.Libraries`, `System.Globalization`.
- Legacy `OnReloadMagazine(BasePlayer, BaseProjectile)` hook and all Facepunch.Pool inventory scanning code.

## 3.0.8

Bumped the plugin version to 3.0.8. Updated plugin with the new OnMagazineReload hook implemented for forward compatibility, while retaining the original OnReloadMagazine for older Oxide/Rust builds. This change bumps the version to 3.0.8 and ensures that reloading logic remains intact regardless of which hook the server invokes.
