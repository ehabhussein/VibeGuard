---
schema_version: 1
archetype: engineering/configuration-management
title: Configuration Management
summary: Config is code's input; separate from secrets, load once at startup, fail fast on missing values, make env parity the default.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - configuration
  - config
  - environment-variables
  - 12-factor
  - settings
  - secrets
  - feature-flags
  - env-parity
  - config-schema
  - runtime-config
  - hot-reload
  - config-validation
related_archetypes:
  - engineering/deployment-discipline
  - engineering/project-bootstrapping
  - engineering/observability
  - engineering/api-evolution
references:
  article: "12-Factor App — Config (III)"
  book: "The Pragmatic Programmer — externalizing configuration"
  article: "Martin Fowler — Feature Toggles"
---

# Configuration Management -- Principles

## When this applies
Every non-trivial program has config: database URLs, feature flags, timeouts, API endpoints, log levels. The discipline starts before the first deploy: how does the code know what environment it is in, where are values read from, what is a secret versus a setting, what happens when a required value is missing. Get this wrong and you get hard-coded URLs in production, secrets in git history, and environment-specific bugs that only appear on the day of a customer demo.

## Architectural placement
Config is the bridge between code (identical across environments) and environments (which must differ). It lives at the startup boundary: the program loads config once, validates it, and passes strongly-typed values to the rest of the system. Config is distinct from secrets (which have their own lifecycle and access controls) and from feature flags (which change at runtime without redeploy). Conflating the three produces systems where rotating a database password requires a deploy and flipping a feature requires a secrets-management ticket.

## Principles
1. **Config is injected, not compiled.** The same binary runs in dev, staging, prod. Environment-specific values come from outside -- env vars, config files, a secrets manager. Baking values into the source is a deploy-time mistake that stops being caught at code review.
2. **Load and validate at startup.** Parse all config on boot, fail fast if required values are missing or malformed, and log the effective config (redacting secrets). A service that starts successfully with an invalid config and crashes on the first request is a service that wakes up on-call.
3. **Strongly-type config after load.** The rest of the code sees `config.database.url: Url`, not `os.environ["DB_URL"]` scattered in 40 files. A single parser converts strings to typed values once; everyone else consumes a struct. This also makes config testable.
4. **Separate config from secrets.** A secret is a credential with a lifecycle (rotation, access control, audit). A setting is a tunable with none of that. Store secrets in a vault; store settings in env vars or a checked-in-with-redaction file. Mixing them forces every setting change through the secrets pipeline.
5. **Env parity is the default, divergence is justified.** Dev, staging, and prod should differ only in values that *must* differ -- endpoints, credentials, scale. Divergence in feature flags, library versions, or algorithmic choices produces "works on my staging" failures. List the known differences; shrink the list over time.
6. **Defaults are for local dev only.** Production should require every value to be explicitly set; no hidden defaults. Local defaults are convenience ("db=localhost:5432"). Silent production defaults are how prod ends up pointing at a dev database.
7. **Feature flags are separate from deploy.** A feature flag changes behavior at runtime without a deploy -- that is its entire point. If flipping a flag requires a deploy, it is not a flag, it is a config value. Use real flag infrastructure for anything that needs mid-incident toggling.
8. **Document the config schema.** A `config.example.yml` or a typed schema in code, in the repo, showing every key, its type, its default (or "required"), and a one-line description. "Read the source" is not documentation, especially when config is loaded from three sources with a merge order.
9. **Config changes are auditable.** Who changed what, when, why. In small teams this is a git commit on a config repo; in larger ones it is a feature-flag platform's audit log. Config changes cause incidents as often as code changes do; they deserve the same traceability.
10. **Hot reload is a feature, not a default.** Hot-reloading config is useful for feature flags and log levels; it is dangerous for database pools, schemas, or anything that requires atomic cross-service change. Most settings should require a restart -- the restart is a natural consistency boundary.

## Anti-patterns
- Database URLs and API keys hard-coded in source, committed to git, visible in every PR forever.
- A `if env == "prod"` branch scattered through business logic, making dev and prod code paths diverge subtly.
- Config loaded lazily on first use -- production boots successfully, then the first request fails with "missing config key X".
- Secrets stored in env vars printed to logs on startup, or `config.dump()` that includes the database password.
- A sprawling `settings.py` with 300 lines, all from different eras, no schema, no validation, no deletion -- adding a value is easy, removing one is terrifying.
- Environment-specific `if` branches in tests that run differently on CI vs laptop, making "it passed locally" meaningless.
- Configuration by convention ("just name the env var `FOO_BAR_BAZ` and hope the loader finds it"), producing silent misconfigurations when a typo happens.
- Using feature flags as permanent config toggles -- flags that have been on for two years, still carrying conditionals in code that could be deleted.
- Shipping a default that "works for development" and silently landing in production when someone forgets to set the var.
- Config-as-code files that are edited by hand in prod without going through review, diverging from the repo within days.

## References
- 12-Factor App -- "III. Config" (12factor.net/config)
- Andrew Hunt & David Thomas -- *The Pragmatic Programmer*
- Martin Fowler -- "Feature Toggles (aka Feature Flags)" (martinfowler.com/articles/feature-toggles.html)
- HashiCorp Vault, AWS Secrets Manager, 1Password -- reference secrets backends
- Jez Humble & David Farley -- *Continuous Delivery* (configuration management chapter)
