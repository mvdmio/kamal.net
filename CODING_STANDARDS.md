# Coding standards

## Mirror

This repo is a port: every behaviour in `src/Kamal` **mirrors** a Ruby original in [`basecamp/kamal`](https://github.com/basecamp/kamal). *Where upstream code lands* pairs each original with its C# file. The one exception is a **deviation**, below.

- **The upstream test is the spec.** Upstream's `test/**` pins the exact command strings the port must emit. Port its assertions into the matching `tests/Kamal.Tests` file.
- **Docs and templates are verbatim copies.** Upstream's `lib/kamal/configuration/docs/*.yml` and `lib/kamal/cli/templates/**` land unchanged in `src/Kamal/Configuration/Docs/` and `src/Kamal/Templates/`. They ship as embedded resources: `kamal docs` prints the docs and `kamal init` writes the templates, byte for byte.

## Deviations

A **deviation** is a deliberate difference from Ruby Kamal.

- Read the decisions in `docs/adr/` before deviating.
- README's *Known deviations from Ruby Kamal* is the list. Record each new deviation there.

## Domain terms

Name or describe a domain concept with the terms in `CONTEXT.md`.

## Where upstream code lands

Ruby `snake_case` → C# `PascalCase`. A Ruby module split across `foo/bar.rb` becomes a `Foo.Bar.cs` partial of `Foo.cs`.

| basecamp/kamal | this port |
|---|---|
| `lib/kamal/cli/<x>.rb` | `src/Kamal/Cli/<X>Cli.cs` (`base.rb` → `CliBase.cs`) |
| `lib/kamal/cli/app/boot.rb` | `src/Kamal/Cli/AppBoot.cs` |
| `lib/kamal/cli/healthcheck/*.rb` | `src/Kamal/Cli/Healthcheck.cs` |
| `lib/kamal/cli/templates/**` | `src/Kamal/Templates/**` |
| `lib/kamal/commander.rb`, `commander/specifics.rb` | `src/Kamal/Commander.cs`, `Commander.Specifics.cs` |
| `lib/kamal/commands/<x>/<y>.rb` | `src/Kamal/Commands/<X>.<Y>.cs` |
| `lib/kamal/configuration/**` | `src/Kamal/Configuration/**` (`env/tag.rb` → `EnvTag.cs`, `proxy/run.rb` → `ProxyRun.cs`) |
| `lib/kamal/configuration/validator{,/*}.rb` | `src/Kamal/Configuration/Validation/` |
| `lib/kamal/secrets/**` | `src/Kamal/Secrets/**` |
| `lib/kamal/{utils,tags,git,env_file}.rb`, `utils/sensitive.rb` | `src/Kamal/Utils/` |
| `lib/kamal/sshkit_with_ext.rb` and SSHKit usage | `src/Kamal/Execution/` |
| `lib/kamal/output/*.rb` | `src/Kamal/Output/` |
| `test/<area>/<x>_test.rb` | `tests/Kamal.Tests/<Area>/<X>Tests.cs` |
