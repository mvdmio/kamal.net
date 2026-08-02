# 02 — SSH uploads apply the requested mode

Status: done

## What to build

Uploading over SSH with a permission mode works and lands the exact permissions asked
for. `kamal accessory boot NAME` gets past the secrets upload instead of dying with
`ArgumentOutOfRangeException (Parameter 'mode')`, `kamal deploy` and `kamal app boot`
get past the role secrets upload, and the proxy host TLS certificate and key land as
`0644` rather than the silently-wrong `0420`.

The SSH backend stops handing SSH.NET a POSIX bitmask. Both places that call
`SftpClient.ChangePermissions` — the single-file/stream upload and the recursive
directory upload — go through the shared parse from step 01 and pass SSH.NET's
octal-digit form. The recursive path applies the mode to the created directory and to
every file and subdirectory beneath it, so a directory upload with `"0700"` leaves the
whole tree at `0700`.

An upload whose mode string is not valid POSIX octal fails with an error naming the
mode string and the remote path, before or instead of anything SSH.NET would say about
a parameter called `mode`.

Callers keep passing the strings they pass today (`"0600"` for secrets, `"0644"` for
host TLS material, `"0700"` for the proxy error pages directory) — those match upstream
Kamal and do not change. Record the fix under `## [Unreleased]` in `CHANGELOG.md`.

## Footprint

Projects: `src/Kamal/Kamal.csproj`, `tests/Kamal.Tests/Kamal.Tests.csproj`

- `src/Kamal/Execution/SshBackend.cs` — `UploadStream`, `UploadDirectory`, both
  currently `sftp.ChangePermissions(path, Convert.ToInt16(mode, 8))`
- `src/Kamal/Execution/` — the shared parse added in step 01
- `src/Kamal/Cli/AppBoot.cs` — mode call sites (`"0600"` secrets, `"0700"` error pages,
  `"0644"` host TLS cert and key); read-only, they stay as they are
- `src/Kamal/Cli/AccessoryCli.cs` — `"0600"` accessory secrets call site; read-only
- `tests/Kamal.Tests/Execution/FakeBackend.cs` — `Uploads` records the mode string
  unconverted, which is why no existing test caught this
- `CHANGELOG.md` — `## [Unreleased]`

## Acceptance criteria

- [ ] Neither SSH upload path passes a bitmask to `ChangePermissions`; no
      `Convert.ToInt16(mode, 8)` remains in `SshBackend`
- [ ] A recursive directory upload applies the mode to the directory and to every file
      and subdirectory below it
- [ ] An upload with an invalid mode string fails with a message naming the mode string
      and the remote path, not an SSH.NET parameter name
- [ ] Caller-side mode strings are unchanged
- [ ] `CHANGELOG.md` records the fix under `## [Unreleased]`
- [ ] Solution builds and the full test suite passes

Verified by the maintainer against a real host, not by this step: `kamal accessory boot
NAME` completes past the secrets upload; `kamal deploy` completes past the role secrets
upload; the uploaded files show `0600` / `0644` / `0700` on the remote.

## Outcome

`SshBackend.UploadStream` and `SshBackend.UploadDirectory` (both `private static`, in
`src/Kamal/Execution/SshBackend.cs`) now call `UploadMode.Parse(mode, remotePath).SshOctal`
instead of `Convert.ToInt16(mode, 8)`. `SftpClient.ChangePermissions`'s second parameter
is `short`, and `UploadMode.SshOctal` is `int` (max value `1777`, well within `short`
range), so the call site casts: `(short)UploadMode.Parse(mode, remotePath).SshOctal`.
An invalid mode now throws `UploadMode.Parse`'s `FormatException` — naming the mode
string and the remote path — before `ChangePermissions` (and therefore SSH.NET) ever
sees it.

`UploadDirectory`'s existing recursion was already structurally correct: it applies
`mode` to the created directory, then recurses into every file (via `UploadStream`) and
every subdirectory beneath it, threading the same `mode` string through unchanged. Only
the conversion at each of the two call sites was wrong, so fixing both call sites was
sufficient — no change to the recursion shape was needed.

No caller-side mode strings changed (`AppBoot.cs`, `AccessoryCli.cs` untouched, as the
footprint predicted).

No new unit test was added. `SshBackend` has no existing unit test coverage of any
kind — its `Run`, `Upload`, and SFTP paths all require a live SSH connection, and
`tests/Kamal.Tests/Execution/FakeBackend.cs` (used by every test that exercises upload
call sites) records the `mode` string unconverted rather than routing through
`SshBackend`, so it cannot exercise this fix either. The conversion itself
(`UploadMode.Parse` — octal parsing, both representations, and the invalid-mode error
naming the mode and remote path) is already fully covered by
`tests/Kamal.Tests/Execution/UploadModeTests.cs` from step 01; step 02 adds no new logic
beyond wiring `SshBackend`'s two call sites to that existing, tested parse. Real-host
verification (`kamal accessory boot`, `kamal deploy` completing past the secrets upload,
correct remote permissions) is explicitly left to the maintainer per this step's brief.

Recorded the fix under `## [Unreleased]` → `### Fixed` in `CHANGELOG.md`.

Full solution build and `dotnet test Kamal.slnx` are green (763 tests passed — the same
count as after step 01, since no new tests were added).

**Deviations from the footprint guess:** the footprint's `sftp.ChangePermissions(path,
Convert.ToInt16(mode, 8))` render was approximate — the actual parameter name is
`remotePath`, and passing `UploadMode.SshOctal` (an `int`) to `ChangePermissions` (which
takes `short`) required an explicit cast the footprint didn't call out. No file beyond
`SshBackend.cs` and `CHANGELOG.md` needed changes; `AppBoot.cs`, `AccessoryCli.cs`, and
`FakeBackend.cs` were read-only as predicted and required no edits.
