---
schema_version: 1
archetype: engineering/dependency-discipline
title: Dependency Discipline
summary: Every dependency is a liability; prefer standard library, pin versions deliberately, own the code you cannot afford to lose.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - dependencies
  - libraries
  - packages
  - npm
  - pip
  - nuget
  - cargo
  - version-pinning
  - lockfile
  - transitive-dependencies
  - vendoring
  - build-your-own
related_archetypes:
  - engineering/yagni-and-scope
  - engineering/api-evolution
  - engineering/deployment-discipline
  - engineering/continuous-integration
references:
  article: "Go Proverb — A little copying is better than a little dependency"
  incident: "event-stream (npm) supply chain compromise, 2018"
  article: "Rachel Kroll — On choosing dependencies"
---

# Dependency Discipline -- Principles

## When this applies
Every time you add a dependency. Every time the dependency tree grows by transitive inclusion. Every version bump. Every CVE in a package you did not know you had. Dependencies are the largest and least-reviewed portion of every modern codebase; a typical Node or Python project has more lines of third-party code than first-party by an order of magnitude. The discipline is treating each addition as a permanent commitment, not a convenience.

## Architectural placement
Dependencies are code you have decided to trust without review -- for their correctness, their security, their maintenance, their license, and their roadmap. They sit as a layer between your code and the runtime: updating them can break you, failing to update them can leave CVEs, forking them is expensive. Dependency discipline is the engineering counterpart to the security supply-chain archetype: the security side asks "is this dep malicious?"; the engineering side asks "is this dep worth the cost of having?".

## Principles
1. **Every dependency is a liability.** Bugs you will debug, security patches you will apply, breaking changes you will absorb, transitive trees you will audit, licenses you will track. The library that saves 50 lines of code costs ten times that in attention over its lifetime. "Free" dependencies are not free.
2. **Prefer the standard library.** The language's own library has the longest maintenance commitment, the best documentation, the widest understanding across engineers, and zero supply-chain risk. Reach for third-party only when the standard library genuinely does not suffice -- not when it is merely slightly less ergonomic.
3. **A little copying is better than a little dependency.** Needing three functions from a 2,000-function library is a strong signal to just copy the three functions with attribution. Copied code is versioned, reviewed, and under your control; a dependency brings the other 1,997 functions and all their transitive baggage.
4. **Pin versions, review updates.** Lockfiles exist for a reason. An unpinned dependency is a production defect waiting for a transitive publisher to release a broken patch. Updates are PRs -- reviewed, tested, deployed -- not automatic background noise.
5. **Shallow trees over deep trees.** A dependency with 40 transitive deps brings 40 supply-chain risks, 40 update cadences, 40 potential version conflicts. Given two libraries that solve the same problem, pick the one with the smaller tree, even if the other has a slightly nicer API.
6. **Audit the things that matter.** For any dep in a security path (auth, crypto, input parsing, deserialization), read the code before adopting. You cannot outsource security; the dep's bugs become your bugs. For non-critical deps, at least check maintenance signals: commits per month, open issues, last release, funding.
7. **Vendoring is valid; own the version you ship.** For high-stakes or rarely-updated code, check the dependency into your own repo. Build reproducibility improves, supply-chain risk drops, and you cannot be broken by an upstream rewrite. The cost is maintenance -- applied only when warranted.
8. **Licenses matter and compound.** GPL, AGPL, commercial licenses -- each has consequences for the project. Track the license of every direct and transitive dep. "We'll figure it out at ship time" has killed products. Run a license scanner in CI.
9. **One library per concern.** Two HTTP clients, three logging frameworks, four date libraries -- each is a fragmentation tax: two documentation systems, two APIs to learn, two bug tails. Pick one, refactor toward it, and remove the others when the migration is done.
10. **Removing a dependency is a task, not a dream.** Every team has "we should drop lib X" in its folklore forever. Treat removal as real work with a ticket, estimate, and owner. Left to drift, deprecated deps become permanent.

## Anti-patterns
- `leftpad`-style micro-dependencies: importing a package to invert a boolean or pad a string, inheriting a maintainer, an update cadence, and a supply-chain risk for zero code savings.
- Adding a framework to solve a 30-line problem -- "just use this form library" -- and paying for the framework's assumptions in every subsequent change.
- No lockfile, or a lockfile not committed to version control, turning builds into moving targets.
- Transitive deps accumulating over years with no audit, leaving a supply chain of 800 packages no single person has ever reviewed.
- "We'll upgrade when we have time" -- the upgrade becomes impossible after three years of deferred breaking changes pile up.
- Copy-pasting a snippet from a library's source into your code, removing attribution, and severing the ability to pick up bug fixes.
- Choosing a dependency because it was trending on social media last month, with no check of maintenance signals or fit for the actual problem.
- Vendoring code and then never updating it, shipping an eight-year-old copy with known CVEs because "it has been stable".
- Two libraries with overlapping purpose both active in the same codebase forever, because "changing is too risky" -- so the doubled surface stays doubled.
- Skipping license review because "it's open source" -- then learning about AGPL's obligations when a customer's legal team asks.

## References
- Go Proverb -- "A little copying is better than a little dependency" (Rob Pike, Go talks)
- Ken Thompson -- "Reflections on Trusting Trust" (the deepest supply-chain paper)
- Russ Cox -- "Our Software Dependency Problem" (research.swtch.com/deps)
- Snyk, GitHub Dependabot, Renovate -- automated update tooling
- OWASP -- Software Component Verification Standard
