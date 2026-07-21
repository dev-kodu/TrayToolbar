# Logging & diagnostics standard

**MANDATORY on every path listed below.** Adapted from the KADE.Tekla standard and
re-rooted: KADE.Tekla's rules are about solids and coordinate systems. **This app has no
geometry.** Its risky paths are *startup*, *shell/native interop*, *user config*, and *an
updater that replaces its own executable*. The rules below target those.

## Why this exists

**TrayToolbar is a `WinExe` with no console, no main window, and — when it fails at startup
— no tray icon either.** The user's entire bug report is *"it doesn't do anything."*

Today the **only** diagnostic in the whole app is a single `Error-<timestamp>.txt` written
by the catch-all in `Program.Main` — and that catch wraps **only** the `Application.Run`
loop. Everything before it (`ProcessUpdate`, `EnsureSingleInstance`, `SetShowInTray`,
`MigrateConfiguration`, `HotKeys.Enable`) runs **unprotected**: fail there and there is no
file, no console, no icon, no evidence. The app just silently isn't there.

That is the bar:

> A wrong result — or a silent non-start — must be diagnosable **from the log alone**,
> without a debugger and without reproducing it.

**Current state, honestly: there is no logging infrastructure** (verified 2026-07-17 — no
`ILogger`, no `Trace`, no log file). The first task touching a risky path below should add a
minimal append-with-flush file logger next to the existing `Error-*.txt` convention
(`ConfigHelper.ApplicationRoot`). **Do not add a logging package.** This app is portable and
dependency-light on purpose — `Reimplement notification panel/toast messages without
dependency bloat` is an actual commit here. Respect that.

> **Not to be confused with TODO #85** ("optional launch logging … item name, target path,
> timestamp, username"). That is a *user-facing audit feature* with a configurable path.
> This document is *diagnostics*. Related, not the same; do not implement one as the other.

## 1. What MUST be logged

### Startup — the silent-death path

Log **each step of `Program.Main` as it begins**, before it runs: update check,
single-instance result, toast support, tray registration, config migration, hotkey enable,
form construction. Flush each line.

This costs ~8 lines and converts "it doesn't do anything" into "the last line says
`begin MigrateConfiguration`". That is the single highest-value logging in this repo.

Include: the resolved config path, the exe path, the version, the architecture (x64/ARM64),
and the command-line args (`--show`, `--newversion`).

### Single-instance

`EnsureSingleInstance` decides whether the app **exits immediately**. Log the mutex name,
whether it was created or already held, and the resulting action (run / notify existing +
exit). "It won't start" is most often "an orphaned instance still owns the mutex" — make
that readable.

### Configuration load / save — data-loss critical

`ConfigurationStore`, `TrayToolbarConfiguration`, `ConfigHelper`.

- `ReadConfiguration` swallows **all** errors and returns `new()` — a corrupt or unreadable
  config **silently becomes defaults**, and the next save then overwrites the user's real
  config with those defaults. Log the path, whether the file existed, its size, and — inside
  that catch — **what was thrown**. A user losing every folder and hotkey deserves one line.
- `WriteConfiguration` **deletes then writes** (not atomic). Log before the delete and after
  the write, with the target path and payload size. A `begin` with no `done` is the signature
  of a config file destroyed mid-write.
- `MigrateConfiguration` moves the legacy file and swallows failures. Log both paths and the
  outcome.

### Hotkey registration — fails silently by construction

`HotKeys.Register` **ignores the `RegisterHotKey` return value** and records the id as
registered regardless. If another app owns the combo, the hotkey silently does nothing.

Log **per hotkey**: the id, modifiers, key, **and the native return value**. If you touch
this code, log the failure explicitly — that is a one-line fix for a whole class of
unreproducible "my hotkey stopped working" reports.

### Folder scanning — unbounded recursion

`FolderScanner.EnumerateFilesCore` recurses with **no depth cap and no visited set**.

- Log the scan root, the recursive flag, the filter, the resulting file count, and elapsed ms.
- Log **per directory, before recursing into it** (see the begin rule).
- Its `catch { yield break; }` hides every unreadable directory. Log which directory was
  skipped and why — silently-missing menu entries are otherwise undiagnosable.

**Bound the recursion.** A depth cap plus a visited-path set, and log when the cap trips. See
"prevent, do not catch" below for why a `try/catch` is not an option here.

### Launching — the user-visible action

`Program.Launch` / `CanLaunch` start arbitrary shell targets with `UseShellExecute = true`.
Log the requested target, the `CanLaunch` verdict **and which branch allowed it** (file /
directory / allowed remote URI), and the launch outcome. A refused launch currently looks
identical to a click that did nothing.

### Update — self-replacing executable, trust boundary

`UpdateHelper` / `UpdateLogic` / `UpdateHelperInstaller`. This is the most dangerous path in
the repo: it downloads a zip, verifies it, **copies over the running exe**, and relaunches.

Log every step with its inputs and verdict: the release/version considered, the resolved
download URL **and whether it matched the expected pattern**, the archive verification
result, the copy source/target, the relaunch command line, and the cleanup outcome. A failed
update leaves a broken install and **no explanation** — its `catch { }` (~line 216) throws
the evidence away.

Read `docs/update-security.md` first. **Log the decision, never the secret**: log *that* a
URL was rejected and *why*, not credentials or token contents.

## 2. Log BEFORE the risky step — at item granularity

This is the rule that gets violated most. Read it twice.

**Log before the thing that can kill you, at the granularity of the thing that can kill
you.** Per item, immediately before the risky call. **A single "begin" line before an N-item
loop is NOT compliant.**

```csharp
// NOT COMPLIANT — names the phase, not the culprit.
Log("begin folder scan");
foreach (var dir in dirs) { Recurse(dir); }   // dies in dir #312; log says "begin folder scan"

// COMPLIANT — the last line names the exact item that killed you.
foreach (var dir in dirs)
{
    Log($"begin dir depth={depth} path='{dir}'");
    LogFlush();
    Recurse(dir, depth + 1);
}
```

**FLUSH IMMEDIATELY on begin lines.** A buffered log loses exactly the last lines when the
process dies — destroying precisely the evidence you wrote the line for. If the work is
parallel or on a background thread (the update download is `async`), include the **thread
id**; interleaved lines are unreadable without it.

### Where this rule was earned

In KADE.Tekla, `XbimGeometrySidecar` logged **one** line — `begin CreateContext()` — then
pushed **583,129 entities** through native code and died with exit 139. The failure was
**nondeterministic**: it survived 1 run in 3. The last log line named the *phase*, not the
*culprit*, so three runs yielded no more information than zero runs. The rule already existed
and was violated anyway.

> **"IF IT'S NONDETERMINISTIC YOU DON'T LOG ENOUGH."**

Nondeterminism is not an excuse for weak logging — it is the *reason* for strong logging.
You cannot re-run your way to the answer; the log is all you get. Shell, DPI, theme, and
folder-content state make **this** app's failures environment-dependent in exactly that way.

### Prevent, do not catch — a `try/catch` here is a LIE

**Where a failure is uncatchable, a `try/catch` documented "best-effort, never throws" is a
lie.** This repo ships a live example:

`FolderScanner.EnumerateFilesCore`'s unbounded recursion, given a directory junction loop,
raises **`StackOverflowException` — which .NET Core cannot catch.** The process dies
instantly. `Program.Main`'s `catch (Exception e)` **will not run**, so **no `Error-*.txt` is
ever written.** The user sees the tray icon vanish, and you get nothing.

You cannot catch your way out of that. **Prevent it:** cap the depth, track visited real
paths, and log + flush per directory before recursing — so if it still dies, the last line
names the directory that did it.

The same applies to a hung shell call or a killed process: no `catch`, no `finally`, no
evidence. Log first, then act.

## 3. Timing

Time each phase with a `Stopwatch` and emit a `DONE … in Nms` line: startup, folder scan,
menu rebuild, icon extraction, update download/verify/install. The menu rebuilds on every
folder change — "the menu is slow" must be attributable to a phase without a profiler.

```
DONE scan root='C:\tools\links' recursive=true files=214 skipped=3 in 86ms
```

## 4. Logging is best-effort and must NEVER break the workflow

A logging failure must never stop the app starting, a menu rebuilding, or a launch. Swallow
**logger** errors — a locked or unwritable log file (this app is portable; it may sit in a
read-only folder) is not a reason to fail the user.

**This is the one place a silent catch is correct**, and it applies **only to the logger
itself** — never to the work being logged. Do not cite this rule to justify swallowing a
shell, config, or update error. Note the app may run from a read-only location: if the log
target is unwritable, degrade silently — never prompt, never block startup.

## 5. Keep it proportional

This is a small, deliberately dependency-light tray app. Do not add a logging framework, a DI
logging abstraction, or structured events. Do not log inside paint, `WndProc`, or menu-render
paths — you will flood the file and slow the UI. Instrument **the paths in section 1** and
stop there.

Bound any retry (HTTP, file, shell): a fixed small attempt count, logging **each** attempt
and its outcome. An unbounded retry against a dead network is a hang, not resilience.

Keep pure/testable code pure. `FolderScanner`, `UpdateLogic`, and `ConfigurationStore` are
unit-tested through `IFileSystem` — do not staple a logger dependency onto them. Pass an
optional sink instead, so tests stay silent and the seam stays intact:

```csharp
internal IEnumerable<string> EnumerateFiles(
    string path, bool recursive, TrayToolbarConfiguration config, Action<string>? onLog = null)
```

## 6. Whole-file logging review on EVERY edit (mandatory)

Same discipline as the comment review. When you edit a **pre-existing** file, review the
**whole file** against this standard and bring it up to the bar in the same change:

- [ ] Every path in section 1 logs its inputs, decisions, and outcome — enough to diagnose a
      wrong result or a silent non-start from the log alone.
- [ ] `begin` lines are **per item**, immediately before the risky call, and **flushed**;
      thread id included where the work is async/parallel.
- [ ] Native return values that are currently ignored are logged (`RegisterHotKey`).
- [ ] Recursion is bounded, and the cap logs when it trips.
- [ ] Each phase is timed and emits `DONE … in Nms`.
- [ ] Logger failures are swallowed; the work's failures are **not**.
- [ ] Silent `catch { }` blocks either log, or are reported as bugs
      (`CODE-COMMENTS-AND-DOCS.md` §4).

If the file is genuinely already at the bar, change nothing — but you must have looked.
Logging hygiene on a file you were already editing — **not** a licence to refactor, and (see
`CLAUDE.md`: this is a zero-divergence fork) **not** a licence to spread the diff.
