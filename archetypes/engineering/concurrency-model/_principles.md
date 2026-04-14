---
schema_version: 1
archetype: engineering/concurrency-model
title: Concurrency Model
summary: Pick one concurrency model and hold it; prefer message-passing to shared state; async for IO, threads for CPU, processes for isolation.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - concurrency
  - parallelism
  - async
  - await
  - threads
  - actor-model
  - message-passing
  - shared-memory
  - goroutines
  - coroutines
  - reactive
  - event-loop
related_archetypes:
  - engineering/performance-discipline
  - engineering/interface-first-design
  - engineering/error-handling
  - engineering/observability
references:
  book: "Java Concurrency in Practice — Goetz et al."
  article: "Rob Pike — Concurrency is not Parallelism"
  book: "Designing Data-Intensive Applications — Kleppmann (replication and consensus chapters)"
---

# Concurrency Model -- Principles

## When this applies
When the system must do more than one thing at once: serve many requests, background-process while foregrounding, pipeline work across stages, saturate multi-core hardware. Concurrency is both a performance tool and a correctness minefield; most serious production bugs with the hardest reproductions are concurrency bugs. The discipline is choosing a model deliberately, not absorbing whichever model the first library you pull in happens to assume.

## Architectural placement
Concurrency is a language-and-runtime choice expressed throughout the codebase: async/await (JS, Python, C#, Rust), coroutines / goroutines (Kotlin, Go), threads with locks (Java, C++), actor model (Erlang, Akka), processes (Elixir, OS-level). The model you pick shapes every API -- return types, cancellation, error propagation, back-pressure. Mixing models within one codebase (callbacks, promises, async/await, and raw threads in the same service) is the single biggest source of accidental complexity.

## Principles
1. **Concurrency is not parallelism.** Concurrency is structure -- multiple independent flows of control; parallelism is execution -- multiple flows running simultaneously. A single-threaded event loop is concurrent but not parallel. Know which you need: concurrency handles IO waits; parallelism reduces wall-clock for CPU-bound work.
2. **Pick one primary model per service.** Async end-to-end, threads end-to-end, or actors end-to-end. Mixing -- "sync function that blocks on async work" -- destroys the whole stack's benefits and creates deadlocks at the seams. Ship one model; justify any exception loudly.
3. **Prefer message-passing to shared mutable state.** "Do not communicate by sharing memory; share memory by communicating." Queues, channels, actors isolate mutable state behind a single owner. Shared-state concurrency (locks, mutexes, condition variables) is error-prone even for experts and scales badly to larger teams.
4. **Match the model to the workload.** IO-bound at high concurrency → async / event loop. CPU-bound on multiple cores → threads or a worker pool. Strong isolation, crash-safe → processes / actors. Picking the wrong model (threads for IO-bound web server) wastes resources and limits scale.
5. **Make cancellation a first-class concept.** Long-running work must be cancellable. A request that abandons mid-flight must stop its downstream work; a shutdown must drain in-flight operations. Languages with explicit cancellation tokens (Rust, C#, Kotlin, Go contexts) make this explicit; others require discipline.
6. **Back-pressure is not optional.** A producer faster than its consumer will exhaust memory or latency somewhere. Every queue must have a bounded size; every pipeline must propagate slowness backward. Unbounded queues turn transient spikes into OOM crashes.
7. **Avoid shared mutable state; when forced, minimize scope.** If you must lock, lock the smallest possible region, for the shortest possible time, and never call out to untrusted code while holding a lock. Prefer immutable data structures passed between actors; copy-on-write is often cheaper than debugging a deadlock.
8. **Race conditions are tested with care, not caught by luck.** Thread-interleaving bugs rarely appear in unit tests. Use determinism-restoring tools (TLA+, property-based concurrency tests, deterministic schedulers, thread sanitizer). Production load is not a test for concurrency correctness; it just postpones failure.
9. **Deadlocks come from lock order; prevent structurally.** If code always acquires locks in a globally consistent order, deadlocks cannot occur among those locks. Document the order; prefer lock-free or single-owner designs so the question does not arise.
10. **Observability for concurrent systems is different.** Stack traces of async code are unhelpful without instrumentation; metrics like queue depth, lock contention, and active-task counts matter more than CPU time. Trace IDs that propagate across async boundaries are the only way to debug production behavior.

## Anti-patterns
- `Thread.sleep(100)` to fix a race condition -- the bug is still there; you just changed the timing.
- Locking inside an async function while holding another lock, without documenting or enforcing acquisition order.
- `asyncio.gather` over thousands of coroutines without any concurrency limit, DoSing downstream systems the moment a queue backs up.
- Mixing `sync` and `async` code by calling `asyncio.run` inside a library function, blocking the caller's event loop and destroying concurrency.
- "Fire and forget" goroutines / Tasks whose failures silently vanish, leaving leaked work and no error signal.
- Unbounded channels / queues as a convenience, converting transient pressure into permanent memory growth and eventual OOM.
- Shared mutable global state ("just one singleton cache") accessed from every goroutine without synchronization, passing review because "it seems to work in testing."
- Using `parallel.ForEach` or equivalent over a workload that is actually IO-bound, creating threads that mostly block and starve the rest of the process.
- Cancellation tokens accepted by signatures and never actually checked, so timeouts do nothing.
- Relying on CPython GIL / single-threaded JS semantics for correctness, then porting the code to a runtime without that guarantee and discovering every race at once.

## References
- Brian Goetz et al. -- *Java Concurrency in Practice*
- Rob Pike -- "Concurrency is not Parallelism" (blog.golang.org/waza-talk)
- Jeff Preshing -- "An Introduction to Lock-Free Programming" (preshing.com)
- Martin Kleppmann -- *Designing Data-Intensive Applications* (chapters on replication, consensus)
- Joe Armstrong -- thesis on the actor model and Erlang
