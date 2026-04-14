---
schema_version: 1
archetype: engineering/data-modeling
title: Data Modeling
summary: Model the domain before the storage; identifiers, timestamps, and invariants first; normalize by default, denormalize with evidence.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - data-modeling
  - schema-design
  - entity
  - relationship
  - normalization
  - denormalization
  - primary-key
  - foreign-key
  - event-sourcing
  - immutable-data
  - domain-model
  - invariants
related_archetypes:
  - engineering/data-migration-discipline
  - engineering/module-decomposition
  - engineering/api-evolution
  - engineering/interface-first-design
references:
  book: "Designing Data-Intensive Applications — Martin Kleppmann"
  book: "Domain-Driven Design — Eric Evans"
  book: "SQL and Relational Theory — C. J. Date"
---

# Data Modeling -- Principles

## When this applies
Before the first table, document, or event schema. Data models outlive code -- a service can be rewritten in a weekend, a schema with five years of production data is forever. Model mistakes compound: the wrong key choice becomes every query's problem, the missing timestamp becomes every debugging session's problem, the ambiguous nullable becomes every migration's problem. Invest early or pay forever.

## Architectural placement
The data model sits between the domain (what the business actually does) and the storage (how the database stores bytes). A good model is first a clear expression of the domain -- entities, relationships, invariants -- and second a choice of storage that fits access patterns. Skipping straight to storage (ORM tables, JSON schemas, protobuf messages) without thinking about the domain produces models that survive only until the first feature that the initial author did not imagine.

## Principles
1. **Model the domain, then the storage.** Start with the concepts the business uses -- Order, Customer, Invoice, Shipment -- and the rules that relate them. Only after the domain is clear, choose how to persist it. Storage choices (relational, document, columnar, event log) should serve the domain, not dictate it.
2. **Identifiers are decisions, not defaults.** Pick identifier strategy deliberately: auto-increment int (simple, leaks volume, not portable), UUID v4 (opaque, random, no ordering), UUID v7 / ULID (sortable, time-embedded), domain-natural keys (ISBN, SKU -- rarely stable enough). Each choice shapes distribution, migration, and debuggability. Write down the choice.
3. **Timestamps are not optional.** Every row, event, or document should carry `created_at` and `updated_at` at minimum. Without them, debugging production requires a time machine. Store timestamps in UTC with timezone information; timezone bugs at storage time are unfixable downstream.
4. **Invariants belong in the model, not the UI.** "A user has exactly one primary email" is a schema concern (unique constraint + nullable foreign key or a `primary_email_id`). Enforcing it only in application code means the invariant breaks the moment any other code path writes to the table. The database is the last line of defense.
5. **Normalize by default.** Third normal form is the starting point: each fact stored once, updated in one place, referenced elsewhere by key. Normalization prevents update anomalies, shrinks storage, and clarifies semantics. Denormalize only with measured evidence -- specific slow queries that cannot be made fast any other way.
6. **Foreign keys and constraints are documentation the database enforces.** A declared FK tells readers that `order.customer_id` refers to `customer.id` *and* stops the database from accepting orphans. "We'll enforce it in application code" is a promise the next developer will not know about.
7. **Soft-delete is a choice with costs.** A `deleted_at` column is easy to add and creates invisible complexity: every query must remember to filter, every unique constraint must include it, every JOIN partner inherits the consideration. Prefer hard delete + audit log when you can; reach for soft-delete when undelete or audit truly needs it, then be rigorous.
8. **Events versus state: pick per aggregate.** Some domains are naturally event-sourced (financial ledgers, audit trails, game moves); others are naturally state-based (user profile, config). Do not apply one pattern globally. Event sourcing buys perfect history and temporal queries at the cost of complexity; use it where the history *is* the point.
9. **Schema evolves; plan for it from the start.** Even the best initial model will change. See `engineering/data-migration-discipline` for how to move; at the modeling stage the lesson is: add fields more easily than removing them, avoid optional-everything-in-case, and keep semantic fields separate from denormalized caches.
10. **Nullable means "can be unknown", not "will be filled later".** A nullable column is a semantic claim: this attribute may legitimately have no value. Do not use nullable as a lazy migration path for required fields; the next author will not know which nulls are meaningful and which are placeholders.

## Anti-patterns
- Stringly-typed columns -- storing dates, enums, and JSON-encoded structures as free-form `TEXT` because "it's simpler". Every query then validates and parses.
- A single `metadata` JSON column on every table, becoming a dumping ground for half-considered fields with no schema and no validation.
- Auto-increment integer IDs leaked into public URLs, revealing business volume ("customer #7 sees that customer #1000 exists") and hindering sharding.
- No timestamps anywhere, making production forensics require application log archaeology.
- Boolean columns for states that will obviously grow: `is_active`, `is_cancelled`, `is_refunded` -- a `status` enum on day one would have been better.
- Denormalization without measurement, producing duplicated fields that go out of sync and a week of bugs when someone updates the canonical place.
- Foreign keys declared at code review time then removed "for performance" with no measurement, reintroducing orphaned rows immediately.
- Arbitrary soft-delete that no query actually filters, showing deleted users in autocomplete and every support chat.
- A "users" table that is actually `users + profiles + sessions + preferences` merged into a 60-column monster, because the first version only needed a login form.
- Schemas generated by ORM migrations with no human review, landing with no indexes, no constraints, and no comments.

## References
- Martin Kleppmann -- *Designing Data-Intensive Applications* (O'Reilly)
- Eric Evans -- *Domain-Driven Design: Tackling Complexity in the Heart of Software*
- C. J. Date -- *SQL and Relational Theory*
- Vaughn Vernon -- *Implementing Domain-Driven Design*
- Pat Helland -- "Life beyond Distributed Transactions" (identifiers, boundaries)
