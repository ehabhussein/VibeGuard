---
schema_version: 1
archetype: engineering/build-and-packaging
title: Build and Packaging
summary: Reproducible builds, pinned toolchains, hermetic artifacts; one command from fresh clone to shippable binary; versioning is a contract.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - build
  - packaging
  - artifacts
  - reproducible-builds
  - toolchain
  - docker
  - container-image
  - versioning
  - semver
  - cross-compilation
  - build-cache
  - hermetic
related_archetypes:
  - engineering/deployment-discipline
  - engineering/dependency-discipline
  - engineering/continuous-integration
  - engineering/api-evolution
references:
  article: "Reproducible Builds project (reproducible-builds.org)"
  book: "Continuous Delivery — Jez Humble & David Farley"
  article: "Semantic Versioning 2.0.0 (semver.org)"
---

# Build and Packaging -- Principles

## When this applies
Any time you go from source to a shippable artifact -- binary, container image, library package, installer. Build is the moment where "it works on my machine" becomes "it works for everyone" or stays broken. A casual build process costs the team every day in mysterious failures, non-reproducible bugs, and "which version is in prod" confusion. A disciplined one is invisible when it works and easy to fix when it does not.

## Architectural placement
Build sits between source control and deployment. Its inputs are source code, dependencies, toolchain; its output is a versioned, verifiable artifact. Everything downstream -- test, deploy, release -- depends on the artifact being exactly what was built from exactly that source. Break that chain and you lose the ability to answer basic questions: what is running? can we roll back? is this bug in the source or in the build?

## Principles
1. **One command from clone to artifact.** `make build` or `./build.sh` or equivalent -- a single entry point that a new engineer can run on a fresh clone and produce a working artifact. Multi-step oral traditions ("set this env var, then run X, then Y unless Z") guarantee broken onboarding and CI flakiness.
2. **Reproducible builds.** Same source + same dependencies → same bytes. Timestamps, build paths, parallelism order, and random IDs should not leak into the artifact. Reproducibility lets you verify what is running in production and detect supply-chain tampering.
3. **Pin the toolchain.** Compiler version, interpreter version, build tool version -- all declared in a file in the repo (`.tool-versions`, `rust-toolchain`, `.nvmrc`, Docker base image SHA). "Latest" on a dev machine today is not "latest" tomorrow. The toolchain is a dependency; treat it like one.
4. **Hermetic builds are the goal.** A build should not depend on network access during compile, on files outside the repo, or on machine state. Hermeticity gets you reproducibility, reliability under network flakes, and cache-friendly incremental rebuilds. Bazel, Nix, and containerized builds exist to enforce this.
5. **Artifacts carry provenance.** The artifact knows what commit built it, what version tag, when, on what toolchain. `--version` output, image labels, binary metadata -- all populated at build time. Mystery artifacts are the root of a hundred production incidents.
6. **Versioning is a contract.** Semantic versioning (or the chosen scheme) says something real: a major bump means breaking change, a minor means additive, a patch means fixed. If bumps are arbitrary, consumers cannot trust upgrades. Pick a scheme, document it, and follow it.
7. **Separate build and ship.** Building produces an artifact; shipping deploys it. One artifact can be deployed to many environments; environment-specific settings come from config, not from a new build. Env-specific builds are a smell that creates three-way skews.
8. **Incremental builds must be correct.** A build cache that produces wrong results is worse than no cache -- it hides failures behind stale artifacts. Invest in correct cache keying or skip caching; do not land in the middle.
9. **Keep the build fast; keep it truthful.** Parallelize, cache, elide unchanged work -- but do not skip tests or elide checks just to ship faster. A fast build that ships broken code burns all the speed savings in incident response.
10. **Package minimal artifacts.** A container with the full toolchain and source checked in is a 1.5GB attack surface. Multi-stage builds, distroless bases, stripped binaries -- ship what is needed to run, not what was needed to build.

## Anti-patterns
- A build that only works on one engineer's laptop because of an undocumented env var or installed tool.
- "Works in CI, broken locally" (or vice versa), indicating implicit differences nobody has bothered to diagnose.
- Containers built `FROM ubuntu:latest` and `apt-get update` with no pinning, producing a different artifact every build and failing reproducibility completely.
- Version numbers bumped arbitrarily, making semver meaningless to consumers who now treat every upgrade as a major.
- Artifacts with no metadata -- a binary in production that does not know its own git commit, so incidents require git archaeology.
- A 10-minute build dominated by 9 minutes of `npm install` that could have been cached.
- Build scripts that download dependencies from the internet on every run, breaking in air-gapped environments and during registry outages.
- Shipping containers that include dev tools (compilers, package managers, shell utilities) because the base image already had them, expanding the attack surface unnecessarily.
- Manual release steps ("edit the CHANGELOG, tag, push, then build, then upload") with no automation, guaranteeing a mistake per release.
- Dependence on a CI-only build for "real" artifacts, so a CI outage blocks all shipping and no one can reproduce a build locally.

## References
- Reproducible Builds project (reproducible-builds.org)
- Jez Humble & David Farley -- *Continuous Delivery*
- Semantic Versioning -- semver.org
- Bazel / Nix / Please -- hermetic build systems
- Google -- *Software Engineering at Google* (chapter on build systems)
