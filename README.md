# Kamal.NET

A .NET port of [Kamal](https://github.com/basecamp/kamal) (v2.11) — deploy web apps anywhere, from bare metal to cloud VMs, with zero-downtime deploys, powered by Docker and kamal-proxy. Same `config/deploy.yml`, same commands, same workflow. **No Ruby required** — installs as a regular dotnet tool.

```bash
dotnet tool install -g mvdmio.Kamal

kamal init     # creates config/deploy.yml, .kamal/secrets, sample hooks
kamal setup    # bootstrap servers with Docker + deploy from scratch
kamal deploy   # build, push, and zero-downtime deploy
```

Your existing Kamal `deploy.yml` files work as-is. See the [official Kamal docs](https://kamal-deploy.org) for configuration reference — this port follows it. `kamal docs [section]` works offline too.

## Why

Kamal is a fantastic deployment tool, but it ships as a Ruby gem. If your whole stack is .NET, installing a Ruby runtime just to deploy is friction. Kamal.NET is a faithful port of the Ruby implementation to C#, distributed through NuGet as a dotnet tool.

## What's included

The complete Kamal 2.11 command surface:

| Command group | Commands |
|---|---|
| root | `setup`, `deploy`, `redeploy`, `rollback`, `details`, `audit`, `config`, `docs`, `init`, `remove`, `upgrade`, `version` |
| `app` | `boot`, `start`, `stop`, `details`, `exec`, `containers`, `stale_containers`, `images`, `logs`, `remove`, `version`, `live`, `maintenance`, … |
| `accessory` | `boot`, `upload`, `directories`, `reboot`, `start`, `stop`, `restart`, `details`, `exec`, `logs`, `remove`, `upgrade` |
| `build` | `deliver`, `push`, `pull`, `create`, `remove`, `details`, `dev` |
| `proxy` | `boot`, `boot_config`, `reboot`, `start`, `stop`, `restart`, `details`, `logs`, `remove`, `upgrade` |
| `prune` | `all`, `images`, `containers` |
| `registry` | `setup`, `remove`, `login`, `logout` |
| `server` | `bootstrap`, `exec` |
| `lock` | `status`, `acquire`, `release`, `force_release` |
| `secrets` | `fetch`, `extract`, `print` |

Plus: destinations (`-d staging`), roles and tagged hosts, per-role env, accessories, aliases, deploy locks, audit logging, hooks (`pre-connect`, `pre-build`, `pre-deploy`, `post-deploy`, …), `.kamal/secrets` with `$(command)` substitution, and the secret-manager adapters (1Password, LastPass, Bitwarden, Bitwarden Secrets Manager, Doppler, Enpass, GCP Secret Manager, AWS Secrets Manager, Passbolt).

## How faithful is the port?

The Ruby implementation was ported layer by layer, and the upstream test suite came with it — **750+ tests**, most asserting byte-identical docker/shell command strings against the same expectations as the Ruby tests. The deploy orchestration (lock → hooks → proxy boot → stale container detection → app boot with healthcheck barrier → prune) mirrors `kamal/cli/main.rb` exactly.

Architecture maps 1:1 to upstream:

| Ruby (basecamp/kamal) | Kamal.NET |
|---|---|
| `Kamal::Configuration` (+ validators, docs) | `Kamal.Configuration` (YamlDotNet) |
| `Kamal::Secrets` (+ dotenv, adapters) | `Kamal.Secrets` |
| `Kamal::Commands::*` (command builders) | `Kamal.Commands` |
| SSHKit `on(hosts)` execution | `Kamal.Execution` (SSH.NET) |
| `Kamal::Commander` | `Kamal.Commander` |
| Thor CLI | `Kamal.Cli` (System.CommandLine) |

## Known deviations from Ruby Kamal

- **No ERB in `deploy.yml`** — config files are parsed as plain YAML. ERB templating is Ruby-specific; if you relied on it, move dynamic values to env/secrets.
- **SSH auth** uses key files (`ssh.keys`, `key_data`, or default `~/.ssh/id_*`); ssh-agent and password auth are not supported yet.
- **`ssh.proxy`** supports a single jump host (`user@bastion`); `proxy_command` and chained jump hosts are not supported.
- **OpenTelemetry audit log shipping** is not ported (file-based audit logging works).
- `kamal init --bundle` (Gemfile binstubs) is not applicable to .NET and prints a note.
- `-h` is `--hosts` (as upstream); use `--help`/`-?` for help.

## Building from source

```bash
git clone https://github.com/mvdmio/kamal.net
cd kamal.net
dotnet test                                  # run the full suite
dotnet pack src/Kamal -c Release -o artifacts
dotnet tool install -g mvdmio.Kamal --add-source ./artifacts
```

Requires the .NET 10 SDK. The tool rolls forward, so it runs on any newer runtime.

## License

MIT. Kamal.NET is a derivative work of [Kamal](https://github.com/basecamp/kamal), © David Heinemeier Hansson, also MIT-licensed. See [LICENSE](LICENSE). "Kamal" is the name of the original project; this port is not affiliated with or endorsed by 37signals.
