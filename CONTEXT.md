# Kamal.NET

Domain language for deploying applications with the Kamal.NET tool (the C# port of Kamal).

## Language

**Destination**:
A named deploy target that selects an overlay config and secrets file (for example `deploy.auth.yml` and `.kamal/secrets.auth`).
_Avoid_: environment, stage, target (when you mean this Kamal concept)

**Secret**:
A named sensitive value loaded from Kamal secrets files (and optional external secret adapters), referenced by name from config rather than inlined in `deploy.yml` when possible.
_Avoid_: env var (when you mean a secrets-file entry), credential blob

**Failure class**:
A stable category for why a Kamal run stopped, exposed to operators and CI as both a process exit code and a log marker. Classes include at least connect, auth, build, healthcheck, and lock.
_Avoid_: error type (implementation exceptions), phase (when you mean the failure category, not the deploy step)

**Connect failure**:
A failure class for transport-level SSH problems (timeouts, dropped sockets, unreachable hosts) that may be safe to retry when the operator opts in.
_Avoid_: SSH error (too broad — includes auth), transient failure (informal)

**Auth failure**:
A failure class for SSH identity rejection or missing usable keys. Not retried by deploy retry.
_Avoid_: connect failure, permission denied (OS phrasing only)

**Config expansion**:
Substitution of `${ENV_VAR}` and `${ENV_VAR:-default}` in string values of `deploy.yml` after YAML is loaded, using the process environment. This is the port’s limited replacement for Ruby ERB for env-driven values.
_Avoid_: ERB, interpolation (unqualified), secrets expansion (secrets dotenv has its own rules)

**Deploy phase**:
A named stage of a run used in log markers (for example connect, build, boot), distinct from a failure class even when names overlap.
_Avoid_: step, stage (when you mean the logged phase name)
