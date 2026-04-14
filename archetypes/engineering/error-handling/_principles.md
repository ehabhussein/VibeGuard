---
schema_version: 1
archetype: engineering/error-handling
title: Error Handling
summary: Distinguish expected failures from bugs; fail fast on invariant breaks, recover deliberately on known failure modes, never swallow errors silently.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - error-handling
  - exceptions
  - result-types
  - failure-modes
  - fail-fast
  - defensive-programming
  - error-propagation
  - panic
  - recovery
  - validation
  - invariants
  - try-catch
related_archetypes:
  - engineering/observability
  - engineering/interface-first-design
  - engineering/api-evolution
  - engineering/testing-strategy
references:
  book: "Release It! — Michael T. Nygard (failure modes, bulkheads)"
  article: "Joe Duffy — The Error Model (Midori)"
  article: "Rust Book — Recoverable and Unrecoverable Errors"
---

# Error Handling -- Principles

## When this applies
Every function that can fail, every boundary that accepts untrusted input, every call that crosses a process or network. Error handling is not a decoration you sprinkle at the end; it is a design decision made per function. The discipline is knowing the difference between a bug (programmer violated an invariant -- crash loudly) and a failure (the world disagreed with your expectations -- handle deliberately). Confusing the two produces software that hides its bugs and panics on its failures.

## Architectural placement
Error handling is the contract between a function and its caller. It sits at every seam: public APIs declare what can fail and how; internal functions surface failures upward until someone can act on them; the outermost layer (UI, HTTP handler, CLI) turns errors into user-visible outcomes. The model you pick -- exceptions, result types, error codes, panics -- is a choice that shapes the whole codebase. Mixing models or leaving the choice unspoken leads to ad-hoc handling that rots over time.

## Principles
1. **Separate bugs from failures.** A bug is an invariant violation -- "this list should never be empty here", "this pointer must be non-null". Failures are expected outcomes of interacting with an imperfect world -- network timeout, disk full, invalid user input. Bugs should crash loudly with a stack trace; failures should be returned as data and handled by the caller. Confusing them means either bugs get silently papered over or ordinary failures crash the process.
2. **Fail fast on invariant breaks.** When a precondition is violated, stop immediately at the scene of the crime. `assert`, `panic`, or throw -- do not continue with corrupt state. The further execution travels from the point of the error, the harder the debugging.
3. **Recover deliberately on known failure modes.** Every expected failure has a named handling strategy: retry with backoff, fall back to a cache, return a default, surface to the user, fail the request. "Handled" means a conscious decision was made, not that the error was caught and logged somewhere.
4. **Never swallow errors silently.** An empty `catch` block or an ignored return value is a trap. If you truly know the error is irrelevant, comment *why* -- "best-effort cleanup; failure here means the caller already failed". Otherwise, propagate.
5. **Errors are data; model them.** A `FileNotFoundError` that says "file missing" is less useful than one that carries the path, the attempted operation, and the OS error code. Rich error values enable programmatic handling upstream (retry on transient, abort on permanent) and actionable user messages.
6. **Errors cross boundaries by translation, not leakage.** An SQL `UNIQUE_VIOLATION` should not surface to the HTTP caller as a database error. Each layer translates errors into its own vocabulary: the persistence layer's "conflict" becomes the service layer's "duplicate user" becomes the API's `409 Conflict`. Leaking lower-layer errors breaks encapsulation and often leaks secrets.
7. **Pick one error model and hold it.** Exceptions everywhere, result types everywhere, errors-as-values everywhere -- each works. Mixing them (half the codebase throws, half returns `Result`) creates constant impedance mismatches and forgotten error paths.
8. **Validate at boundaries, trust the interior.** External input is validated at the edge (HTTP handler, message consumer, CLI arg parser) and converted to well-typed domain values. Interior code operates on trusted types and does not re-validate. Defensive programming at every layer is paranoia that clutters code without improving safety.
9. **Errors must be actionable or aggregated.** An error message is for a human who will see it at 3 AM. "Operation failed" is not actionable; "failed to update user 12345: connection timeout to db-primary after 30s" is. If an error cannot be made specific, at minimum group it under a clear category so dashboards and alerts can distinguish volumes.
10. **Test failure paths.** Happy-path-only test suites mean the error paths have never executed outside production. Mock the dependency that fails, inject the timeout, fill the disk in a container -- exercise the code that runs when things go wrong, because that code is always less tested and more load-bearing than it looks.

## Anti-patterns
- `catch (Exception e) { log.debug(e); }` -- silently swallowing every error class because the developer did not want to think about which were recoverable.
- Returning `null`, `-1`, or empty strings to mean "error" -- callers forget to check, and the bad value propagates into later computation as if real.
- Re-throwing as a generic exception (`throw new Exception("something went wrong")`), losing the original type, message, and stack trace.
- Using exceptions for normal control flow -- "throw `NotFound` to signal a cache miss" -- making performance and readability worse.
- Catching errors at every level "just in case", producing nested try/catch chains that obscure real logic and re-log the same error at every layer.
- Panicking on recoverable failures -- a Go service that calls `log.Fatal` on every DB error, taking the whole process down for a transient blip.
- Error messages that leak sensitive data -- stack traces with SQL query text, file paths with usernames, tokens in log output.
- Ignoring async errors (`fire-and-forget` promises, `go`routines without recovery), causing failures to vanish with no trace.
- Retrying indefinitely without backoff or a cap, turning a transient failure in one dependency into a self-inflicted DoS.
- Wrapping every call in validation that restates the type system -- `if (user == null) throw NullPointer` when the language and type already disallow null.

## References
- Michael T. Nygard -- *Release It!* (Pragmatic Programmers) -- failure modes, stability patterns
- Joe Duffy -- "The Error Model" (joeduffyblog.com/2016/02/07/the-error-model/) -- Midori's design
- Steve Klabnik & Carol Nichols -- *The Rust Programming Language* (chapter on Error Handling)
- Rob Pike -- "Errors are values" (blog.golang.org/errors-are-values)
- Raymond Chen -- "Cleaner, more elegant, and wrong" (exception handling in C++)
