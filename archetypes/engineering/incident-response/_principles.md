---
schema_version: 1
archetype: engineering/incident-response
title: Incident Response
summary: Stop the bleeding first, root cause later; blameless postmortems; every incident ends with durable action items or it repeats.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - incident
  - outage
  - on-call
  - postmortem
  - root-cause
  - blameless
  - incident-commander
  - mitigation
  - rollback
  - paging
  - alerting
  - runbook
related_archetypes:
  - engineering/observability
  - engineering/deployment-discipline
  - engineering/documentation-discipline
  - engineering/continuous-integration
references:
  book: "Site Reliability Engineering — Beyer et al. (Google SRE Book)"
  article: "John Allspaw — Blameless PostMortems and a Just Culture"
  book: "The Field Guide to Understanding Human Error — Sidney Dekker"
---

# Incident Response -- Principles

## When this applies
When production is broken, users are affected, or an alert has fired that suggests either. Incident response is a distinct mode of operation: the normal rules of careful review and long deliberation suspend in favor of rapid triage, mitigation, and communication. Afterward, the discipline continues -- the incident is not over when the site is up; it is over when the organization has learned from it. Teams without this discipline have the same incident twice.

## Architectural placement
Incident response is the operational counterpart to everything else in engineering: the code you wrote will fail, and when it does, the quality of response determines customer impact. The discipline sits across three phases: detection (observability surfaces the problem), mitigation (stop the bleeding), and learning (postmortem, action items). Each phase has its own rules; mixing them -- debating root cause mid-incident, rushing postmortem while tired, skipping action items -- produces long outages and recurring failures.

## Principles
1. **Mitigate first; diagnose later.** During an incident, the goal is restoring service, not understanding why it broke. Roll back, feature-flag off, redirect traffic, restart -- any action that stops user impact is correct even if it erases forensic evidence. The forensics can be reconstructed from logs and metrics; the lost customers cannot.
2. **One incident commander; clear roles.** Even in small teams, declare who is coordinating (IC), who is investigating, who is communicating externally. When everyone does everything, nothing happens well. A solo responder is still IC + investigator + communicator; naming the roles keeps them from blurring.
3. **Communicate early, often, and imperfectly.** A customer-facing statusline updated every 15 minutes with "we are investigating" is better than silence with "we are still investigating" for two hours. Internal comms (chat channel, incident doc) should have a steady drumbeat of what is being tried and what the IC believes.
4. **Write it down as it happens.** Timeline notes live -- "14:32 noticed high error rate on /checkout", "14:35 rolled back deploy 4289", "14:38 errors still elevated". These notes are the backbone of the postmortem; reconstructing them from memory produces a sanitized fiction.
5. **Blameless postmortems, ruthless about systems.** The postmortem asks: what system allowed a person to cause an outage? Not: which person caused the outage? A blamed individual hides the next near-miss; a fixed system catches everyone. This is not about being nice -- it is about what produces a more reliable system.
6. **Root cause is plural.** Every incident has at least three causes: what broke (the proximate), what allowed it to break (the test gap, the review miss), and what kept it broken (the detection delay, the rollback friction). Fix all three layers; fixing only the proximate leaves the failure mode alive.
7. **Action items have owners and dates.** "Improve monitoring" is not an action item; "Alert on /checkout error rate > 2% for 5 minutes, owned by Priya, due 2026-04-28" is. Ownerless action items die in the postmortem doc; dated ones can be tracked.
8. **Runbooks are written before the incident, not after.** The 3 AM page is not the moment to figure out how to restart the service. Runbooks for known-possible failures live in the docs, are linked from alerts, and are read through in drills. Postmortems update runbooks.
9. **Severity levels with crisp thresholds.** SEV1 is "site is down or data loss imminent", SEV2 is "major feature degraded", SEV3 is "minor degradation". Clear thresholds prevent both over-escalation (everyone paged for a SEV3) and under-response (a real SEV1 treated as a weekday concern).
10. **Incident fatigue is a real failure mode.** Too many pages, too many false alarms, too many SEV1s declared for non-SEV1 events -- all train responders to ignore alerts. Review alert signal-to-noise quarterly; each noisy alert is a trust debt that will cost response time during a real incident.

## Anti-patterns
- Blaming the deployer ("Bob pushed the bad change"), ending the investigation there, and shipping no systemic fix -- guaranteeing the next deployer will make a similar mistake.
- "We will fix the root cause properly" promise in the postmortem, then no ticket, no owner, no follow-up -- six months later the same incident recurs.
- Postmortems written and filed but never read, accumulating as an org-wide shrug while incidents repeat.
- Alerts fire with no runbook link, leaving the responder to Google the error message at 3 AM.
- Mid-incident arguments about whose team owns the broken service, while customers keep hitting the error.
- Restoring service by restarting, declaring the incident over, and never finding out why the restart worked -- guaranteeing it happens again.
- A status page that reads "all systems operational" for the first 30 minutes of a known outage, because updating it is not part of anyone's muscle memory.
- SEV1 declared for every minor issue, producing paging fatigue; real SEV1s then take twice as long to take seriously.
- Postmortem tone of "here is what you should have done", turning a learning exercise into a disciplinary hearing and ensuring the next near-miss goes unreported.
- An action item backlog where "improve monitoring" and "fix the deploy pipeline" have been open for three years and counting.

## References
- Google SRE Book -- chapters on incident management and postmortems (sre.google)
- John Allspaw -- "Blameless PostMortems and a Just Culture" (codeascraft.com)
- Sidney Dekker -- *The Field Guide to Understanding Human Error*
- PagerDuty Incident Response documentation (response.pagerduty.com)
- Laura Nolan et al. -- *Seeking SRE* (collected essays)
