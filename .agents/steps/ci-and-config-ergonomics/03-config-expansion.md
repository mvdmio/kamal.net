# 03 — Config expansion after YAML load

Status: done

## What to build

Config authors can use process-environment placeholders in any string scalar in `deploy.yml` (and destination overlays) without full ERB.

After YAML load **and** after deep-merge of base + destination, expand every string scalar in the config tree:

- `${ENV_VAR}` — required; if `ENV_VAR` is unset in the process environment, config load fails with a clear error
- `${ENV_VAR:-default}` — optional with default (including empty default via `${VAR:-}`)

Expansion source is the **process environment only**, not the secrets map. Secrets stay name-referenced as today. Non-string nodes are not rewritten. Nested mappings and sequences are walked fully.

This is the limited permanent ERB replacement (ADR 0002): common `ENV.fetch` / default patterns move into expansion; “migrating from ERB” narrative for docs comes later with CI docs.

## Footprint

Projects: `src/Kamal`, `tests/Kamal.Tests`

- `src/Kamal/Configuration/KamalConfiguration.cs` — `LoadRawConfig` / `LoadConfigFile` (expand after merge)
- `src/Kamal/Configuration/` — expansion helper (or adjacent util)
- `src/Kamal/Secrets/Dotenv/VariableSubstitution.cs` — prior art only; dotenv rules differ (missing → empty); do not silently reuse those semantics for bare `${VAR}`
- `tests/Kamal.Tests/Configuration/` — expansion coverage (nested strings, defaults, unset error, non-strings untouched, expand after destination merge)

## Acceptance criteria

- [x] Nested string scalars expand from the process environment
- [x] Bare `${VAR}` with `VAR` unset fails at config load with an operator-readable message
- [x] `${VAR:-default}` and `${VAR:-}` work; set vars substitute the real value
- [x] Non-string scalars are unchanged
- [x] Destination overlay merge runs before expansion (base + destination, then expand)
- [x] Secrets map is not used as the expansion source
- [x] Tests cover the cases above; `dotnet test` green

## Outcome

Added `ConfigExpansion` (`src/Kamal/Configuration/ConfigExpansion.cs`): after base+destination deep-merge in `LoadRawConfig`, walks the config tree and expands `${ENV_VAR}` / `${ENV_VAR:-default}` from the process environment only. Bare unset `${VAR}` raises `KamalConfigurationError` with an operator-readable message (optional form suggested). Set-but-empty is not treated as unset. Non-strings left alone; secrets map is not consulted. Injectable `getEnv` on `ExpandString` for unit seams; load path uses process env. Tests: `ConfigExpansionTests` (11). `dotnet test`: 791 passed. Footprint matched; docs left to step 06.
