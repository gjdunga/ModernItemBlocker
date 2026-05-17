# Contributing to ModernItemBlocker

**Version:** 4.2.0
**Maintainer:** Gabriel Dungan, DunganSoft Technologies (GitHub: [gjdunga](https://github.com/gjdunga))

Thanks for taking the time to improve ModernItemBlocker. This document covers how to file issues, propose changes, follow the coding style of the project, and report security problems responsibly.

---

## Ways to contribute

- **Bug reports.** A reproducible bug report is one of the most valuable contributions.
- **Pull requests** for bug fixes, performance improvements, security hardening, new language files, or documentation updates.
- **Translations.** New language files dropped under `oxide/lang/<code>/ModernItemBlocker.json` covering every key in the English default are always welcome.
- **Compatibility testing.** Report results against newer Oxide/Rust builds; the plug-in tracks the Naval-update hook signatures and we want to know promptly if they change.

---

## Filing an issue

Before opening a new issue:

1. Confirm you are running v4.2.0 of the plug-in and at least Oxide v2.0.7022. Older Oxide releases use different hook signatures and are unsupported.
2. Search the issue tracker for an existing report.
3. Provide:
   - Oxide version (`oxide.version` in console)
   - Rust server build identifier
   - The exact ModernItemBlocker version line printed at load time
   - Steps to reproduce
   - Console / `oxide/logs/oxide.log` excerpt around the failure
   - A redacted copy of `oxide/data/ModernItemBlockerConfig.json` if relevant

Issue tracker: <https://github.com/gjdunga/ModernItemBlocker/issues>

---

## Branch model

- `main` — always reflects the most recently released version (currently **v4.2.0**). Direct pushes are not accepted.
- Topic branches — name them after the change, e.g. `fix/loglist-tail-encoding` or `feat/spanish-translation-fix`. Branch from `main`.
- Pull requests target `main`. Squash-merge is preferred so the history stays close to the changelog.

---

## Local development

The plug-in is a single C# file that Oxide compiles in-place on the server. There is no separate build step.

1. Clone the repository.
2. Edit `oxide/plugins/ModernItemBlocker.cs`.
3. Drop the edited file onto a local test server's `oxide/plugins/` directory.
4. Run `oxide.reload ModernItemBlocker` in the server console to recompile and reload.

Compilation errors appear in `oxide/logs/oxide.log` and in the server console.

---

## Coding standards

- **Target framework:** .NET 4.7.2, the framework Oxide compiles against. Do not use language features that require newer assemblies (e.g. `System.ValueTuple` is not guaranteed across Oxide builds; `bool?` is preferred over `(bool, T)?`).
- **Naming.** PascalCase for types and public members, camelCase for locals, `_camelCase` for private fields.
- **Hot paths.** Anything reachable from `CanEquipItem`, `CanWearItem`, `OnMagazineReload` or `CanBuild` is a hot path. Avoid LINQ, allocations, and regex execution on these paths. Cache validated state at load time (see `_safeColor`, `_safePrefix`).
- **Hook subscription.** When you add a new hook, ensure `SubscribeHooks()` decides whether it is needed and `Unload()` explicitly unsubscribes it.
- **XML docs.** Every method, property and field gets an XML doc comment. The audience is the next maintainer who has to debug a production server at 3 AM.
- **Lang keys.** Player-facing strings go through the `Msg()` helper and Oxide's Lang API. Never hard-code English in a hook handler.
- **Logging.** Anything that contains a player-supplied or item-supplied string passes through `SanitizeLog()` before being written.

---

## Security disclosure

If you find a vulnerability, **please do not file a public issue**.

Email **security@dungansoft.example** (substitute the maintainer's preferred private channel listed on the GitHub profile if that address is unreachable) with:

- A description of the issue
- Affected versions
- Steps to reproduce
- Proof-of-concept code if applicable
- Your suggested fix, if you have one

Expect an acknowledgement within 7 days. Coordinated disclosure timeline is 90 days from acknowledgement, shortened if a fix can ship sooner.

Past security-related changes are catalogued in [changelog.md](changelog.md) under each version's *Security Fixes* heading.

---

## Pull request checklist

Before opening a PR, verify:

- [ ] The plug-in still compiles cleanly on Oxide v2.0.7022 (no newer language features sneaked in).
- [ ] Every new public/private member has an XML doc comment.
- [ ] Hot-path handlers (`CanEquipItem`, `CanWearItem`, `OnMagazineReload`, `CanBuild`) do not allocate or run regex.
- [ ] New player-facing strings are added to **all** language files (`en`, `ru`, `es`, `la`). English fallback is allowed for languages you cannot translate; flag it in the PR description so a translator can pick it up.
- [ ] `[Info(...)]` attribute version, `manifest.json` version, `.umod.yaml` version, README header and `changelog.md` heading are bumped consistently.
- [ ] `changelog.md` includes an entry summarising your change. Group entries under **Security Fixes**, **Bug Fixes**, **Additions**, **Performance**, or **Repository / Metadata** as appropriate.
- [ ] `INSTALL.md` is updated if installation steps changed.

---

## Code of conduct

Be civil and constructive. Disrespectful behaviour, harassment, or personal attacks will result in the PR being closed without merge and the contributor being blocked from the repository at the maintainer's discretion.

---

## License

By contributing you agree that your contribution will be licensed under the [MIT License](LICENSE.md), the same licence the rest of the project uses.
