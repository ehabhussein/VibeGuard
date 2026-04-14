---
schema_version: 1
archetype: engineering/performance-discipline
title: Performance Discipline
summary: Measure before optimizing; know your budget; optimize the algorithm before the constant; readable code is the default, fast code is justified.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - performance
  - optimization
  - profiling
  - benchmarking
  - latency
  - throughput
  - big-o
  - algorithmic-complexity
  - allocations
  - memory
  - hot-path
  - premature-optimization
related_archetypes:
  - engineering/observability
  - engineering/testing-strategy
  - engineering/naming-and-readability
  - engineering/dry-and-abstraction
references:
  book: "Systems Performance — Brendan Gregg"
  article: "Donald Knuth — Structured Programming with go to Statements (premature optimization)"
  article: "Martin Thompson — Mechanical Sympathy"
---

# Performance Discipline -- Principles

## When this applies
When you have a measured problem, a known SLO, or code on a hot path (request handler, inner loop, batch job's critical section). Performance is a feature with a cost -- readability, complexity, time -- and every optimization should be justified by data, not hunch. The discipline is the dual of YAGNI: do not optimize what has not been measured to be slow; do optimize seriously when the measurements say you must.

## Architectural placement
Performance is a cross-cutting concern that lives at every layer: algorithmic choice (Big-O), data structure choice (cache friendliness, allocation pattern), system architecture (caching, sharding, async vs sync), and platform concerns (language runtime, hardware). Getting it right means knowing which layer your bottleneck is on and fixing it there -- algorithmic wins dwarf micro-optimizations, but a mis-chosen data structure can waste any algorithmic gain.

## Principles
1. **Measure first, always.** Profile before optimizing. The bottleneck is rarely where intuition suggests. Wall-clock time, allocations, cache misses, IO waits -- different tools reveal different truths. Optimizing code that is not on the hot path is pure waste and leaves a more complex codebase with no benefit.
2. **Set a budget, not a wish.** "Fast" is not a target. "P99 latency under 200ms at 1000 rps" is. Without a budget, optimization has no stopping condition and no definition of done. Budgets also tell you when to stop -- if you are already well under budget, the next optimization has no business value.
3. **Algorithm before constant.** A quadratic loop processing 10k items is a real problem; a slightly slow hash function inside a sane algorithm is not. Fix O(n²) → O(n log n) before rewriting tight loops in unsafe code. The reverse is common and wastes weeks.
4. **Allocations are often the cost.** In managed languages, GC pressure frequently dominates CPU cost. A function that allocates in a tight loop will outperform its simpler-looking peer that reuses buffers -- but only if you measured. Understand the allocation profile of your language and tools.
5. **Amortize, batch, cache -- in that order of preference.** Amortization (compute once, use many) is cheapest. Batching (one round-trip instead of N) is next. Caching (store results) adds invalidation complexity, which is a whole problem of its own. Reach for caching last, not first.
6. **Mechanical sympathy pays at the edges.** For high-performance code, understand the hardware: cache lines, branch prediction, memory hierarchy, false sharing. Most code does not need this. The 1% that does needs it deeply -- and that is a conscious choice, not a default.
7. **Readability is the default; performance code is justified.** Clever bit-twiddling, unsafe casts, manual memory management -- each is a cost paid by every future reader. Only pay it when the measurement justifies the complexity. Leave a comment explaining *why* the ugly code is ugly, with a benchmark reference.
8. **Measure the whole system, not just your function.** A 2x speedup in one function means little if the caller blocks on IO for 90% of the request. Profile end-to-end; optimize the actual bottleneck. Micro-benchmarks can lie about production behavior (cache effects, concurrency, JIT warmup).
9. **Regressions are features you broke.** If you have a perf budget, you have perf tests. Benchmarks in CI, alarms on P99, canary analysis before rollout -- all of these protect budget. A fast-enough system without regression alarms is one release from being slow-enough.
10. **Know your language's sharp edges.** Every runtime has traps: boxing in generics, virtual dispatch overhead, reflection cost, GC pause modes, async context capture. Performance-sensitive code in a given language demands fluency with that language's performance model.

## Anti-patterns
- Premature optimization: rewriting a helper function in assembly because "loops should be fast", when the whole function is called twice per request.
- Claiming a change is a "performance improvement" with no benchmark before or after. The change might be a regression -- nobody knows.
- Caching the first thing you think of, without measuring whether the underlying call was actually slow, inviting all the cache-invalidation complexity for no gain.
- Micro-benchmarks that measure dead code (the JIT elided it), infinite inlining (the benchmark compiler cheated), or warm-cache scenarios that never happen in production.
- Optimizing for throughput when latency is the requirement, or vice versa -- the two are often in tension and require different designs.
- Sprinkling `parallel` / `concurrent` / `async` pixie dust over code with no analysis of whether the work is parallelizable, creating synchronization overhead that is slower than the original serial code.
- Scaling horizontally as a substitute for diagnosing an O(n²) bug -- buying a bigger machine every month while the real fix is a 10-line algorithmic change.
- Optimizing code that is not on the hot path because "it feels slow," complicating maintenance for zero user-visible benefit.
- Removing error handling or validation in the name of performance, producing fragile code that crashes under load instead of running slightly slower.
- Rejecting a feature because "it might be slow" without any measurement, then shipping a workaround that is slower than the rejected design would have been.

## References
- Brendan Gregg -- *Systems Performance* (Addison-Wesley)
- Donald Knuth -- "Structured Programming with go to Statements" (1974) -- coined "premature optimization is the root of all evil" (in context)
- Martin Thompson's blog on mechanical sympathy (mechanical-sympathy.blogspot.com)
- *The Art of Computer Programming* -- Knuth, for algorithmic baselines
- Scott Meyers -- *Effective C++* chapters on performance cost of abstractions
