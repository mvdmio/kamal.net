# 07 — accessory exec end-of-options and intact remote argv

Status: pending

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

- [ ] After `--`, tokens such as `-c` stay in the remote command rather than binding as Kamal options
- [ ] Multi-word remote commands preserve intended argv boundaries (no re-split damage)
- [ ] Global config-file `-c` still works for scripts that use it outside end-of-options
- [ ] Tests assert parse/invoke and remote argv shape; `dotnet test` green
