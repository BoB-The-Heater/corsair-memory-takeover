# Changelog

## 0.3.2 (preview) — 2026-11-12

- Rebuilt the native overview layout and added embedded application, taskbar and tray icons.
- Added per-user taskbar visibility and exit-after-connection settings.
- Restored one-shot GIGABYTE connection for the explicitly supported configuration, with a no-op path for healthy connections and bounded confirmation instead of fixed waits.
- Reduced redundant service and task queries, retained component/identity verification, and improved rotated-log handling for GIGABYTE.
- Preserved ASUS/MSI maintenance limits and rollback behavior. No new third-party runtime or GIGABYTE maintenance task.
- Local checks passed; actual service/driver recovery, desktop interaction and physical lighting remain unverified.

## 0.3.1

- ASUS/MSI service maintenance remains in preview.
- GIGABYTE provides detection and setup guidance only.
- Simplified documentation, website and application help.
- Replaced diagnostic fixtures with synthetic test data.
