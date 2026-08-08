# Config expansion is the permanent limited ERB replacement

Ruby Kamal evaluates ERB in `deploy.yml`. Full ERB is out of scope for this port. Dynamic env-driven values (the common ERB use) are handled by config expansion: after YAML load, every string scalar may use `${ENV_VAR}` or `${ENV_VAR:-default}` from the process environment. Bare `${VAR}` with `VAR` unset fails at config load; optional empty values use an explicit default. Secrets files keep their own dotenv rules. Values that must stay secret still go through secrets name-references, not expansion of secret material into YAML.
