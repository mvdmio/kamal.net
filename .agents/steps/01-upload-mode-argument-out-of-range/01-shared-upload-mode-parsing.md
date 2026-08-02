# 01 — Shared upload mode parsing

Status: done

## What to build

A single place in the execution layer that understands the `mode` string an upload
carries (`"0600"`, `"0644"`, `"0700"`, `"755"`, `"1777"`) and hands each backend the
form that backend actually needs. Today the meaning of that string is re-derived at
every call site, and each site derives it differently — this step makes one parse the
source of truth. Pure prefactor: no user-visible behaviour changes yet.

The parse takes the mode string as octal digits, optionally with a leading zero and
optionally with a fourth leading digit for setuid/setgid/sticky, and exposes both
representations its consumers need:

- the POSIX bitmask form (`UnixFileMode`), which is what the local backend already
  applies via `File.SetUnixFileMode`;
- SSH.NET's form for `SftpClient.ChangePermissions` — the octal digits read as a
  decimal number (`"0600"` → `600`, `"1777"` → `1777`), **not** a bitmask. This is the
  representation nothing in the codebase provides yet and step 02 depends on.

A mode string that is not valid POSIX octal (`"abc"`, `"0999"`, `"77777"`, empty) is
rejected with an error whose message names the offending mode string and the remote
path it was going to be applied to. It must not surface an SSH.NET or BCL parameter
name.

The local backend routes its existing `ApplyMode` conversion through this parse, so its
behaviour is unchanged but the duplication is gone. Keep the mode as a `string` on
`IBackend.Upload` — the signatures do not change.

## Footprint

Projects: `src/Kamal/Kamal.csproj`, `tests/Kamal.Tests/Kamal.Tests.csproj`

- `src/Kamal/Execution/` — new file for the shared parse (a name like `UploadMode.cs`;
  avoid `FileMode.cs`, it collides with `System.IO.FileMode`)
- `src/Kamal/Execution/LocalBackend.cs` — `ApplyMode`, currently
  `(UnixFileMode)Convert.ToInt32(mode, 8)`
- `src/Kamal/Execution/IBackend.cs` — `Upload` overloads; the `mode` parameter's
  documented contract lives here
- `tests/Kamal.Tests/Execution/` — new test file next to `LocalBackendTests.cs`
- `tests/Kamal.Tests/Execution/LocalBackendTests.cs` — existing local upload coverage

## Acceptance criteria

- [ ] `"0600"`, `"0644"`, `"0700"`, `"755"` and `"1777"` all parse, and each yields the
      matching `UnixFileMode` bitmask and the matching SSH.NET octal-digit value
      (`600`, `644`, `700`, `755`, `1777`)
- [ ] An invalid mode string (`"abc"`, `"0999"`, `"77777"`, `""`) raises an error whose
      message contains both the mode string and the remote path
- [ ] The unit tests run without an SSH connection and without touching a remote host
- [ ] `LocalBackend` applies modes through the shared parse and its existing upload
      behaviour is unchanged
- [ ] `IBackend.Upload` signatures are untouched
- [ ] Solution builds and the full test suite passes

## Outcome

Added `src/Kamal/Execution/UploadMode.cs`: a `readonly record struct UploadMode` with a
private constructor and a static `Parse(string mode, string remotePath)` factory. It
validates the mode is 1-4 characters of octal digits (`0`-`7`), then exposes:

- `UnixFileMode` — `(UnixFileMode)Convert.ToInt32(mode, 8)`, the same conversion
  `LocalBackend` already used.
- `SshOctal` — `int.Parse(mode, NumberStyles.None, CultureInfo.InvariantCulture)`, the
  octal digits read as a decimal number (`"0600"` → `600`, `"1777"` → `1777`), which
  step 02 will hand to `SftpClient.ChangePermissions`.

An invalid mode raises `FormatException` with a message naming both the mode string and
the remote path, e.g. `Invalid upload mode "abc" for remote path "/remote/path": expected
octal permission digits such as "0600", "755", or "1777".` — no SSH.NET or BCL parameter
name is surfaced.

`LocalBackend.ApplyMode` now calls `UploadMode.Parse(mode, path)` and passes
`.UnixFileMode` to `File.SetUnixFileMode`, replacing its inline
`(UnixFileMode)Convert.ToInt32(mode, 8)`. Behaviour is unchanged — verified by the
existing `LocalBackendTests` passing unmodified.

`IBackend.Upload`'s two overloads gained doc comments spelling out the `mode` string
contract (octal digits, optional leading zero, optional fourth digit) and pointing at
`UploadMode` as the shared parse; signatures themselves are untouched.

New `tests/Kamal.Tests/Execution/UploadModeTests.cs`: a `Theory` covering `"0600"`,
`"0644"`, `"0700"`, `"755"`, `"1777"` against both the expected `UnixFileMode` bitmask and
expected `SshOctal` value, plus a `Theory` covering `"abc"`, `"0999"`, `"77777"`, `""`
asserting `FormatException` with both the mode string and remote path in the message.
Neither test touches SSH or a remote host.

Full solution build and `dotnet test Kamal.slnx` are green (763 tests passed).

**Deviations from the footprint guess:** none of substance. The footprint's suggested
name `UploadMode.cs` was used as-is. No other files needed changes — `SshBackend.cs` is
intentionally untouched; it still has its own `Convert.ToInt16(mode, 8)` bug, which step
02 fixes by switching it to consume `UploadMode.SshOctal`.
