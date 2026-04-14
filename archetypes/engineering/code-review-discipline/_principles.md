---
schema_version: 1
archetype: engineering/code-review-discipline
title: Code Review Discipline
summary: Review for correctness, design, and tests first; style last; be specific and kind; review is knowledge transfer, not gatekeeping.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - code-review
  - pull-request
  - peer-review
  - review-feedback
  - nits
  - approvals
  - mentorship
  - knowledge-transfer
  - review-checklist
  - bikeshedding
  - review-tone
  - async-review
related_archetypes:
  - engineering/commit-hygiene
  - engineering/testing-strategy
  - engineering/continuous-integration
  - engineering/documentation-discipline
references:
  article: "Google Engineering Practices — Code Review Developer Guide"
  book: "The Pragmatic Programmer (on review and collaboration)"
  article: "Gergely Orosz — How Code Reviews Work at Facebook, Google, Microsoft"
---

# Code Review Discipline -- Principles

## When this applies
Every PR. Every change that is about to land. Code review is the primary mechanism by which knowledge spreads across a team, design decisions are examined with fresh eyes, and the first-author blind spots are caught before production. A team that skips reviews ships faster for six months and then stalls as the codebase diverges into personal styles, undocumented decisions, and unshared context. A team that reviews poorly -- only for style, only for blockers, or only as political theater -- gets the slowdown without the benefit.

## Architectural placement
Code review sits between implementation and merge. It is a conversation between the author (who knows what they intended) and the reviewer (who reads with fresh eyes). Done well, it catches defects, improves design, spreads context, and mentors; done poorly, it is a queue of nitpicks that delay shipping without improving quality. The discipline is a team norm more than a tool: the same GitHub PR process can be fast and useful or slow and performative depending on how reviewers engage.

## Principles
1. **Review for correctness, design, tests, then style.** In that order. Correctness: does it do what it says? Design: does it fit the rest of the system? Tests: is the behavior proved? Style: does it match the house conventions? Reversing the order -- leading with style nits and never getting to design -- produces clean-looking code with bad architecture.
2. **The goal is improved code, not displayed seniority.** A good review leaves the change better and the author more capable. A bad review leaves a queue of trivial demands and a frustrated author. If a comment does not improve the code or teach something worth teaching, skip it.
3. **Be specific; point to the problem and the fix.** "This is confusing" is useless; "the name `process` does not distinguish this from `process_async` 20 lines up; consider `enrichLead` / `enrichLeadAsync`" is a real comment. Specificity costs reviewer time and saves author time -- the right side of the trade.
4. **Distinguish blockers, suggestions, and nits.** "Blocker: this has an SQL injection." "Suggestion: consider extracting this branch to a helper." "Nit: trailing whitespace." Different weights deserve different tags. Flattening them to "every comment must be addressed" makes review a gauntlet.
5. **Automated checks catch mechanical; humans catch judgment.** Linter, formatter, type checker, test runner -- all in CI. A human reviewing for missing semicolons is wasted human. A human reviewing whether the new abstraction carries its weight is irreplaceable.
6. **Small PRs get better reviews.** A 2,000-line diff receives a rubber stamp; a 200-line diff receives engagement. Authors should split; reviewers should ask for splits. One logical change per PR mirrors one logical change per commit.
7. **Respond to review the way you want to be reviewed.** Engage with comments seriously, even the wrong ones -- explaining why the suggestion does not apply teaches more than dismissing. "Good catch" and "hadn't thought of that" are signs of a healthy review culture.
8. **Reviewers are not owners; owners are not unreviewable.** Every change, including those from the tech lead, gets reviewed. Making code immune to review -- through seniority, speed pressure, or implicit deference -- creates pockets of unreviewed code that become the worst parts of the system.
9. **Review is knowledge transfer.** A junior reviewer of a senior's code learns; a senior reviewer of a junior's code teaches. Rotate reviewers so context spreads; do not let one person become the sole reader of a subsystem.
10. **A review that blocks for days costs more than a review that misses something.** Make review a first-class task, not a between-thing: review within one working day is a reasonable norm for most teams. Urgent PRs get explicit "this is urgent" signals, not social pressure to notice.

## Anti-patterns
- PRs with 40 comments, all spelling or style, none engaging with the design. The author learns the reviewer cares about commas, not correctness.
- Approvals inside 90 seconds on a 500-line PR. The approver did not read it; the green check is a lie.
- Review-as-gatekeeping: the senior reviewer blocks every PR with "have you considered" comments that are really "prove you thought about this the way I would".
- Silent merges bypassing review because "this is just a config change" -- until the config change took prod down.
- Review comments phrased as accusations ("why did you do this?") rather than inquiries ("what is the reason for this choice?"), producing defensive authors and superficial fixes.
- The rubber-stamp reciprocal agreement: "I approve yours if you approve mine", with no actual review occurring in either direction.
- Reviewer refuses to explain their objection ("just fix it"), forcing the author to guess, often wrongly, and rework the same code three times.
- PRs with no description, or a description that just says "see ticket" -- the reviewer must reconstruct intent from the diff alone.
- Endless bikeshedding on naming when the structural problem -- this should have been two classes -- goes undiscussed because it is harder.
- Review that never mentions tests, leaving coverage-free code to ship because no one made tests a review criterion.

## References
- Google Engineering Practices -- "Code Review Developer Guide" (google.github.io/eng-practices/review/)
- Gergely Orosz -- "Pragmatic Engineer" on review practices across companies
- *The Pragmatic Programmer* -- Hunt & Thomas (collaboration chapter)
- Karl Wiegers -- *Peer Reviews in Software: A Practical Guide*
- Brian W. Kernighan & Rob Pike -- *The Practice of Programming*
