# Changelog

## 0.3.4 (preview) — 2026-11-15

- Fixed logon recovery interrupting an existing GCC session while it was still initializing, which could cause service control timeouts and delay synchronization.
- Observe an initializing session before recovery. A naturally completed connection exits without restarting GCC or services, after a full identity and component recheck.
- Retained the 25-second logon trigger and existing 38-second connection observation budget. A timeout is rechecked before at most one recovery; errors and changed identities never become success.
- Update the enrolled logon executable to use this fix. Boot-to-color timing still requires a real subsequent logon; local read-only and synthetic evidence cannot prove physical lighting latency.

## 0.3.3 (preview) — 2026-11-14

- Added an explicit GIGABYTE logon connection setting; v0.3.2 only provided manual connection.
- Runs once after the current user logs on, skips recovery for a confirmed healthy connection, and exits after completion. Failure results remain available in Settings.
- Registers a protected executable and component baseline with one administrator approval. Configuration drift blocks connection; owned partial registrations can be disabled and recreated.
- No new runtime dependency, resident loop, wake task or changes to vendor startup settings. Actual reboot/logon and hardware recovery remain unverified.

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
