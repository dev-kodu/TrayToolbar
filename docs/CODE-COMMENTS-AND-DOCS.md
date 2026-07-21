# Code comments & documentation standard

**MANDATORY for all C# in this repo.** Adapted from the KADE.Tekla standard, re-rooted for
TrayToolbar.

**Two scope limits, read them first:**

1. **Enforcement is by review, not by the compiler.** There is no analyzer, no
   `GenerateDocumentationFile`, and no `TreatWarningsAsErrors`, so `CS1591` will never fail
   your build. Nothing catches you but the reviewer.
2. **This repo is a fork of `brondavies/TrayToolbar`, currently at zero divergence**
   (`CLAUDE.md`). Apply this standard to code **you touch**. Do **not** open a
   documentation-retrofit campaign across untouched upstream files — that is a large
   unmergeable diff against upstream, and `AGENTS.md` explicitly forbids broad refactors.

## 1. Public and internal-API members MUST have XML docs

Note the repo's shape: almost everything is `internal` (it is an app, not a library). Apply
this to the **real API surface** — `internal` services, extensions, models, and helpers that
other files call — not just `public`.

A summary states **effect + default + edge-value meaning + units**. Never a restatement of
the member name. If your summary is the identifier with spaces in it, delete it and retry.

```csharp
// USELESS — restates the name.
/// <summary>Enumerates files.</summary>

// USEFUL — effect, filtering, ordering, edge cases, and the failure mode.
/// <summary>
/// Enumerates every file under <paramref name="path"/> that passes the configured file
/// filter, ordered case-insensitively by full path. Folders named in
/// <see cref="TrayToolbarConfiguration.IgnoreFolders"/> are skipped.
/// Returns an empty sequence — never throws — when <paramref name="path"/> is unreadable
/// (access denied, disconnected drive), so callers cannot distinguish "empty" from
/// "unreadable" without a log.
/// When <paramref name="recursive"/> is <see langword="true"/> the walk is unbounded:
/// a directory junction loop will not terminate.
/// </summary>
```

That second example is the bar: it tells the next reader the two things that will actually
hurt them.

### Units and edge values are load-bearing

State the unit whenever a value has one — and in a shell/interop app they usually do:

- icon and font sizes — **pixels**, and say whether they are DPI-scaled or raw;
- timeouts / intervals / debounce — **ms**;
- hotkey ids and modifiers — the id **space** (`BASE_ID + id`) and which enum;
- window messages / native handles — what `0` means (`HWND(0)` is *not* "no window" here).

Where a value is a Win32 constant or a shell convention, **say so and name it**. A magic
number with a documented origin is maintainable; without one it is permanent.

### Required tags

- **`<exception cref="…">` on every thrower** that callers must handle. If a method
  deliberately **never** throws (this codebase has many — `catch { }` then return a default),
  document **that** and what it returns instead. That contract is exactly what makes the
  failure invisible, so it must be written down.
- **Properties use the `Gets` / `Gets or sets` form** (StyleCop SA1623 convention). No
  analyzer enforces it here; follow it for consistency.
- **A worked `<example>` on entry points** — the service methods another file reaches for
  first.
- **`<see cref>` to an overloaded member MUST carry the full signature.** A bare cref to an
  overload is **CS0419** — a warning here, but a **build error** in any repo with warnings as
  errors. Write `<see cref="Register(int, HOT_KEY_MODIFIERS, Keys)"/>`, not
  `<see cref="Register"/>`. This repo **has** overloads (`HotKeys.Register`) — this is a live
  trap, not a hypothetical.
- Nullability is on. If a member returns `null`, the summary says **what null means**.

### Interop deserves more, not less

CsWin32-generated P/Invoke has no docs of its own. When you wrap it, document:

- which native API is called and **what its return/`BOOL` means**;
- **whether the return value is checked** — and if not, why that is acceptable;
- threading/STA/message-pump requirements;
- any handle/COM object the caller becomes responsible for releasing.

## 2. Inline comments explain WHY, never what

The code says what. A comment earns its place only by giving a reason not visible in the
code: a Win32 constraint, a shell quirk, an ordering requirement, a deliberate trade-off.

```csharp
// BAD — narrates the code.
// Dispose the mutex.
_mutex.Dispose();

// GOOD — explains the non-obvious reason.
// Not the owner, so another instance holds the mutex: release ours immediately rather than
// hold a handle the owning instance's lifetime does not account for.
_mutex.Dispose();
```

Rules:

- **No commented-out code.** Git remembers it.
- A *wrong* comment is worse than none — it actively misleads. Fix or delete it.
- **Every silent `catch` must say why swallowing is safe and what the user sees instead.**
  This codebase has twelve of them; a bare `catch { }` in **new** code is unacceptable.

## 3. Whole-file comment review on EVERY edit (mandatory)

When you edit a **pre-existing** file, review **the whole file's** comments against this
standard — not only the lines you touched — and bring the file into compliance in the same
change. Every touched file leaves compliant.

Checklist for the file you touched:

- [ ] Every member of the real API surface has a `<summary>` giving effect + default + edge
      values + **units** — not a name restatement.
- [ ] Properties use `Gets` / `Gets or sets`; throwers have `<exception>`; **never-throws**
      methods say so and document their fallback; entry points have a worked `<example>`;
      crefs to overloads carry the full signature.
- [ ] Interop wrappers document the native API, the return-value check (or its absence), and
      any handle ownership.
- [ ] Inline comments explain *why*. Stale/what-comments and commented-out code are gone.
- [ ] Silent `catch` blocks explain why they are safe — or are reported (below).

If the file is genuinely already compliant, change nothing — but you must have looked.

**Keep it proportional.** Comment hygiene on a file you were already editing. **Not** a
licence to refactor, and — given the fork — **not** a licence to spread the diff.

## 4. Report bugs you find — NEVER silently fix them

Reading a file closely for comments is how bugs get found. This standard found several here
on its first pass (see `CLAUDE.md` → Real traps). When you find one, **tell the user**. Do
not fold the fix into a comment change, do not quietly patch it, do not skip past it.

Report each bug with its **depth**:

- **Severity** — Blocker / High / Medium / Low.
- **Location** — `file:line`.
- **Symptom** — what actually goes wrong.
- **Impact / scope** — who it affects, how often, and whether it can crash the app, **lose
  the user's configuration**, or breach the update/launch trust boundary
  (`docs/update-security.md`), versus cosmetic.

A correct-but-unclear comment is a doc fix. A **wrong** comment hiding a real defect is a
**bug** — report it. In a fork, a bug fix may belong **upstream** rather than here: raise it,
let the user decide where it goes. Do not expand scope on your own.
