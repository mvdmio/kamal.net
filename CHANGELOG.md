# Changelog

All notable changes to Kamal.NET are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Versions track the upstream [Kamal](https://github.com/basecamp/kamal) release
this port is faithful to (`2.11.x` ports Kamal 2.11), with the patch component
reserved for changes to the port itself.

## [Unreleased]

### Fixed

- **SSH uploads with a permission mode no longer crash or apply the wrong
  permissions** — `SshBackend` was converting the mode string (`"0600"`,
  `"0644"`, `"0700"`) into a POSIX bitmask before handing it to SSH.NET's
  `SftpClient.ChangePermissions`, which expects the octal digits read as a
  decimal number instead. `"0600"` and `"0700"` threw
  `ArgumentOutOfRangeException (Parameter 'mode')`, aborting `kamal accessory
  boot`, `kamal deploy` and `kamal app boot` right after the secrets upload;
  `"0644"` silently applied `0420` instead, corrupting the proxy's TLS
  certificate and private key permissions. Both the single-file and recursive
  directory upload paths now go through the shared `UploadMode` parse and pass
  SSH.NET's octal-digit form.
- **Local recursive uploads now apply the mode to the whole tree** —
  `LocalBackend` parsed the mode for a recursive upload and then discarded it,
  because it only applied permissions to paths that were files. The copied
  directory and everything beneath it now get the requested mode, matching the
  SSH backend.
- **An invalid upload mode no longer leaves a file behind** — both backends
  transferred the file first and validated the mode second, so a bad mode wrote
  a secrets file at the default umask before failing. The mode is now parsed
  before anything is transferred.

## [2.11.0] - 2026-08-02

First release. A complete C# port of Kamal 2.11, distributed as a dotnet tool —
no Ruby runtime required.

```bash
dotnet tool install -g mvdmio.Kamal
```

### Added

- **Full Kamal 2.11 command surface** — `setup`, `deploy`, `redeploy`, `rollback`,
  `details`, `audit`, `config`, `docs`, `init`, `remove`, `upgrade`, `version`,
  plus the `app`, `accessory`, `build`, `proxy`, `prune`, `registry`, `server`,
  `lock` and `secrets` command groups.
- **Existing `config/deploy.yml` files work as-is** — the YAML configuration
  model, validators and offline docs (`kamal docs [section]`) are ported from
  upstream, including destinations (`-d staging`), roles, tagged hosts, per-role
  env, accessories, aliases, deploy locks and audit logging.
- **Deploy orchestration** mirroring `kamal/cli/main.rb`: lock → hooks → proxy
  boot → stale container detection → app boot behind a healthcheck barrier →
  prune. Hooks (`pre-connect`, `pre-build`, `pre-deploy`, `post-deploy`, …) run
  at the same points as upstream.
- **Secrets** — `.kamal/secrets` with dotenv parsing and `$(command)`
  substitution, and ten secret-manager adapters: 1Password, LastPass, Bitwarden,
  Bitwarden Secrets Manager, Doppler, Enpass, GCP Secret Manager, AWS Secrets
  Manager and Passbolt.
- **SSH execution layer** built on SSH.NET with connection pooling, replacing
  SSHKit.
- **750+ ported tests**, most asserting byte-identical docker/shell command
  strings against the same expectations as the Ruby suite.
- **Source Link and `.snupkg` symbols** in the published package, so stack
  traces map back to sources.

### Known deviations from Ruby Kamal

- **No ERB in `deploy.yml`** — config files are parsed as plain YAML. Move
  dynamic values to env or secrets.
- **SSH auth** uses key files (`ssh.keys`, `key_data`, or a default
  `~/.ssh/id_*`); ssh-agent and password auth are not supported yet.
- **`ssh.proxy`** supports a single jump host (`user@bastion`); `proxy_command`
  and chained jump hosts are not supported.
- **OpenTelemetry audit log shipping** is not ported; file-based audit logging
  works.
- `kamal init --bundle` (Gemfile binstubs) is not applicable to .NET and prints
  a note.
- `-h` is `--hosts` (as upstream); use `--help` or `-?` for help.

[Unreleased]: https://github.com/mvdmio/kamal.net/compare/v2.11.0...HEAD
[2.11.0]: https://github.com/mvdmio/kamal.net/releases/tag/v2.11.0
