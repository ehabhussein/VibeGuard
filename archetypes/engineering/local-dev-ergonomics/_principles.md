---
schema_version: 1
archetype: engineering/local-dev-ergonomics
title: Local Dev Ergonomics
summary: One-command setup, hermetic environments, fast inner loop; the dev environment is a product with engineers as its users.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - developer-experience
  - dx
  - local-dev
  - onboarding
  - inner-loop
  - hot-reload
  - dev-environment
  - setup-script
  - dev-container
  - docker-compose
  - devcontainer
  - tooling
related_archetypes:
  - engineering/project-bootstrapping
  - engineering/continuous-integration
  - engineering/configuration-management
  - engineering/documentation-discipline
references:
  article: "Stripe Engineering — Developer productivity at scale"
  article: "Shopify Engineering — dev, a dev environment management tool"
  book: "The DevOps Handbook — Kim, Humble, Debois, Willis"
---

# Local Dev Ergonomics -- Principles

## When this applies
Every engineer, every day. The time from "pull latest" to "see the change running" -- the inner loop -- is the single biggest multiplier on engineering throughput. A 10-second inner loop produces 50 experiments per hour; a 3-minute loop produces 20, and the engineer's mind wanders during each wait. The dev environment is not a side concern to the product; it is the factory, and the factory's efficiency sets the product's ceiling.

## Architectural placement
Local dev ergonomics is the engineering-effort investment made in the team's own tools: setup scripts, dev containers, seed data, hot reload, dependency managers, local service orchestration. It sits upstream of every other discipline -- fast tests matter only if you can run them fast locally; good commit hygiene matters only if you can verify changes quickly. A painful dev environment quietly undermines every other engineering practice.

## Principles
1. **One command for setup.** A new engineer clones the repo and runs `./setup.sh` (or `make setup`, or `devcontainer up`), waits five minutes, and has a working environment. If the README is a 40-line checklist of manual steps, the team is paying the cost every time someone joins, changes machine, or reinstalls.
2. **The inner loop is sacred.** Change a file → see the result. The time this takes determines the cadence of thinking. Hot reload, incremental compilation, partial test runs, watch modes -- invest in these until the loop is under 10 seconds for common changes. Every minute saved in the loop saves hours per week.
3. **Hermetic local environment.** Docker, Nix, devcontainers, asdf -- tools that isolate the project's dependencies from the host and from other projects. "Works on my machine" is a symptom of a non-hermetic environment. Hermeticity means any engineer on any OS can run the code identically.
4. **Seed data that reflects reality.** Production-like fixtures, not empty databases. Engineers testing against empty tables miss every bug that only appears with data volume, relationship fan-out, or edge-case values. Seed data should be realistic enough that "works locally" means something.
5. **Local services match production topology.** If production has a Postgres + Redis + message queue, local dev has the same -- via docker-compose, Tilt, or similar. A SQLite-in-dev, Postgres-in-prod split is a recipe for prod-only bugs that no test catches.
6. **The setup script is tested.** A new-machine run, quarterly, or as a CI job -- something proves that the documented setup path still works. Setup scripts rot faster than almost any other code because no one runs them twice.
7. **Fast-enough tests that run on save.** Watch-mode test runners that execute relevant tests on file save give the tightest feedback loop. If the test suite is too slow for this, subsetting (run tests for changed files, run affected-test detection) is worth building.
8. **Reloadable config, reloadable code, reloadable data.** Changing a value should not require a restart cycle. Data should be regeneratable from a seed script, not a manual process. Engineers should never fear "I might break my local state" because rebuilding it is a one-liner.
9. **Error messages teach.** When setup or a local server fails, the error should tell the engineer what to do, not just what went wrong. "Port 5432 already in use -- is Postgres running? Try `docker-compose down` then re-run" beats "bind: address already in use".
10. **Treat dev tooling like a product.** Name the thing, assign owners, track issues, dogfood improvements. "DevEx" work that is implicit ("someone should fix that sometime") stays broken; explicit ("we reduced inner-loop time from 45s to 8s this quarter") gets prioritized.

## Anti-patterns
- A 50-step setup guide that sometimes works, requiring an onboarding buddy to patch the gaps in person every time.
- Local dev that requires sudo, modifies host state, or conflicts with other projects on the same machine -- usable only for this one project, at the cost of every other.
- Tests that pass locally and fail in CI because of environment differences nobody has bothered to reconcile.
- Hot reload broken for months with everyone working around it, adding 30-second waits to every edit-test cycle.
- Seed data from 2019, wildly out of date with current schema, requiring local migrations every morning.
- SQLite-in-dev with Postgres-in-prod, producing a steady stream of "works locally, broken in staging" bugs that burn review cycles.
- Setup docs in a wiki that has drifted two years behind reality, so new engineers silently learn to distrust all documentation.
- No local run at all -- the only way to test is to push to a cloud dev environment, making iteration painful and feedback remote.
- Dev environments that require licenses, VPN access, or internal services unreachable from home, blocking remote engineers.
- "Just talk to X" as the onboarding process, turning a single engineer into a permanent bottleneck for new hires.

## References
- Stripe Engineering blog -- developer productivity posts
- Shopify -- *dev* tool and blog posts on local dev at scale
- Gene Kim, Jez Humble et al. -- *The DevOps Handbook*
- GitHub devcontainer spec and VS Code Remote Containers
- Nix, asdf, mise -- reproducible dev environment tools
