# 07 — accessory exec end-of-options and intact remote argv

Status: done

## What to build

Operators running `accessory exec` can pass guest tool flags (for example `-c`) without them being eaten by Kamal’s global or command options, and remote argv matches what they typed.

- Stop option parsing at end-of-options (`--`)
- Tokens after `--` remain in the remote command (including short options like `-c`)
- Multi-word remote commands keep intended argv boundaries — no re-shelling or re-splitting damage (`JoinCommands` / shell join must not destroy quoting semantics for the remote side)
- Global short `-c` for config file remains for existing scripts; end-of-options fixes collisions rather than renaming `-c`

Behaviour is verifiable via parse/invoke tests and the remote command shape recorded by the CLI harness / command builders.

## Footprint

Projects: `src/Kamal`, `tests/Kamal.Tests`

- `src/Kamal/Cli/KamalCli.cs` — `accessory exec` argument/option tree and `--` handling
- `src/Kamal/Cli/AccessoryCli.cs` — `Exec` argv handling
- `src/Kamal/Utils/KamalUtils.cs` — `JoinCommands` (if still used for exec)
- `src/Kamal/Commands/Accessory.cs` — `ExecuteInExistingContainer` / `ExecuteInNewContainer` argv shape
- `tests/Kamal.Tests/Cli/AccessoryCliTests.cs`, `ParseTreeTests.cs` — prior art

## Acceptance criteria

- [x] After `--`, tokens such as `-c` stay in the remote command rather than binding as Kamal options
- [x] Multi-word remote commands preserve intended argv boundaries (no re-split damage)
- [x] Global config-file `-c` still works for scripts that use it outside end-of-options
- [x] Tests assert parse/invoke and remote argv shape; `dotnet test` green

## Outcome

`accessory exec` remote argv: stopped using `JoinCommands` (space-join re-split multi-word guest args on the remote shell). Each cmd token is `EscapeShellValue`’d and passed as a separate token into `ExecuteInExistingContainer` / `ExecuteInNewContainer`, so e.g. `sh -c "SELECT 1"` becomes `docker … "sh" "-c" "SELECT 1"`. System.CommandLine already stops option parsing at `--`; documented on the `cmd` argument help. Global recursive `-c`/`--config-file` unchanged when used outside end-of-options. Tests: parse tree (guest `-c` after `--`, bare `-c` still binds config-file, global `-c staging.yml` + `--`), invoke harness remote command shape for reuse and new-container paths. No change needed to `Commands/Accessory.cs` builders (already flatten multi-token argv). `JoinCommands` left as Ruby-compatible helper for app/server exec. `dotnet test`: 841 passed. Footprint matched.
