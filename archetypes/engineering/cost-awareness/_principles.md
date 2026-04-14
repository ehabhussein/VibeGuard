---
schema_version: 1
archetype: engineering/cost-awareness
title: Cost Awareness
summary: Cloud cost is a design constraint; measure per-request cost, attribute to features, and the cheap design is often the simple one.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - cost
  - cloud-cost
  - finops
  - budget
  - cost-per-request
  - egress
  - resource-limits
  - auto-scaling
  - right-sizing
  - showback
  - chargeback
  - cost-attribution
related_archetypes:
  - engineering/performance-discipline
  - engineering/observability
  - engineering/deployment-discipline
  - engineering/yagni-and-scope
references:
  book: "Cloud FinOps — J.R. Storment & Mike Fuller"
  article: "AWS Well-Architected — Cost Optimization Pillar"
  article: "Corey Quinn — Last Week in AWS (cost newsletter)"
---

# Cost Awareness -- Principles

## When this applies
When picking an architecture, a storage choice, a data-flow pattern, or a deployment topology. Cloud bills scale with architecture decisions -- a chatty inter-service pattern, an N+1 database access, an always-on-but-rarely-used service, a cross-AZ traffic flow -- any of these can make the difference between a sustainable product and a line item the CFO asks about in every meeting. Cost is an engineering concern, not an ops tax after the fact.

## Architectural placement
Cost awareness sits alongside performance and observability as a production-operability concern. It is expressed in design choices (synchronous vs batch, single region vs multi-region, hot storage vs cold), in resource choices (instance type, memory limits, autoscaling), and in data-flow choices (egress traffic, cross-region calls, serialization format). Engineers who treat cost as "someone else's problem" produce architectures that are technically elegant and financially ruinous.

## Principles
1. **Know the per-request cost.** Sum the costs touched by a typical request: compute time, DB reads, cache hits, egress bytes, third-party APIs. If you cannot estimate it to within an order of magnitude, you do not understand your architecture well enough. Pricing pages are part of the architecture review.
2. **The cheap design is often the simple one.** A cron job that writes a file is cheaper than a Kafka topic; a synchronous call is cheaper than a message queue + worker; a single database table is cheaper than a microservice with its own store. Pick the complex option when requirements justify it, not by default.
3. **Egress is the hidden bill.** Data leaving the cloud provider (or crossing regions, or crossing AZs) is priced per GB and rarely cached in the architecture diagram. Chatty cross-region services, logging shipped to a different cloud, user downloads from the wrong region -- any of these can silently dominate the bill.
4. **Right-size everything, default to smaller.** Over-provisioned instances, headroom for "just in case", databases sized for peak plus 3x -- all these add up. Start smaller than you think; scale up on data. Autoscaling is the right answer when traffic varies; over-provisioning is the wrong answer when traffic is flat.
5. **Cold data belongs in cold storage.** Logs, backups, audit records, analytics archives -- tiered storage (S3 Standard → IA → Glacier) is cheap if used. Keeping everything on hot storage "in case we need it fast" is a tax on every byte forever.
6. **Attribute cost to features.** Tag resources by feature, team, or customer; produce reports that show which parts of the system cost what. "Everything costs $500k/month" is useless; "feature X's recommendation engine costs $150k/month" is actionable. Without attribution, nobody knows where to optimize.
7. **Cost is a dimension of observability.** Alert on cost anomalies the way you alert on latency. A deploy that quietly triples log volume, a feature flag that doubles DB queries, a bug that loops on a paid API -- all are visible in cost metrics sooner than in customer complaints.
8. **Reserved / committed capacity pays for steady-state.** If you know you will run 10 EC2 instances for the next year, reserved pricing saves 40-60% over on-demand. Finance wants this; engineers need to tell finance what to reserve. The math is boring and the savings are real.
9. **Beware the free tier and the "it will be cheap" assumption.** The free tier ends; dev traffic grows; the cheap call at 100 RPS is expensive at 100,000 RPS. Model cost at realistic scale, not at current scale.
10. **Cost-inefficient code is often latency-inefficient code.** An N+1 query is slower *and* more expensive. A bloated payload costs CPU to serialize *and* egress to ship. Optimize-for-speed and optimize-for-cost frequently coincide -- but they are not the same goal, and there are cases where they diverge (e.g., caching buys latency but costs storage).

## Anti-patterns
- An always-on production-grade cluster for a feature used twice a week, because "that is what the reference architecture says".
- Logs shipped unfiltered to a SaaS log platform, billed per GB, with retention set to "forever".
- Cross-region database replicas for a system with no latency-sensitive cross-region reads, doubling the bill for a requirement no one verified.
- A microservice architecture where every request hops through five services, each with its own pod, DB, logging stream -- the network and fixed costs dwarf the actual work.
- No resource tags at all, so the monthly bill is one gigantic number with no attribution; optimization has no target.
- Dev and staging environments running 24/7 at production scale "to match prod", costing 3x what production needs.
- A runaway process looping on a paid third-party API, racking up $40k in a weekend because nobody alerted on spend.
- Storage retention policies set to `"forever"` with no tiering, filling hot storage with data nobody has read since 2019.
- Serverless used to "save cost" on a steady high-RPS workload, where a reserved VM would have been cheaper by 10x.
- Cost discussions deferred to the FinOps team as a quarterly cleanup, rather than being part of design review up front.

## References
- J.R. Storment & Mike Fuller -- *Cloud FinOps* (O'Reilly)
- AWS -- Well-Architected Framework, Cost Optimization Pillar
- Corey Quinn -- *Last Week in AWS* newsletter and blog
- FinOps Foundation -- finops.org
- GCP -- Billing and cost management documentation
