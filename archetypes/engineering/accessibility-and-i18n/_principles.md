---
schema_version: 1
archetype: engineering/accessibility-and-i18n
title: Accessibility and Internationalization
summary: Design for the full range of users and locales from day one; semantic markup, keyboard paths, externalized strings, and testable a11y.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - accessibility
  - a11y
  - i18n
  - l10n
  - internationalization
  - localization
  - wcag
  - aria
  - screen-reader
  - keyboard-navigation
  - rtl
  - unicode
related_archetypes:
  - engineering/naming-and-readability
  - engineering/api-evolution
  - engineering/testing-strategy
  - engineering/documentation-discipline
references:
  article: "WCAG 2.2 — Web Content Accessibility Guidelines"
  book: "Inclusive Design Patterns — Heydon Pickering"
  article: "W3C — Internationalization (i18n) Activity"
---

# Accessibility and Internationalization -- Principles

## When this applies
From the first screen, the first string, the first form. Retrofitting accessibility or i18n is an order of magnitude more expensive than building them in -- the architecture, component library, and string handling all bake in assumptions that are painful to unpick. The discipline is treating "users have different abilities" and "users speak different languages" as default truths, not special cases.

## Architectural placement
Accessibility and internationalization are cross-cutting concerns that touch the UI layer most visibly but extend deep: data models carry locale-aware fields, APIs return translatable error codes, emails use templates with locale selection, logging respects user-facing locales distinctly from operator locales. Treating them as "the frontend's problem" fails the moment an error message from the backend needs to be translated or a date in a database needs to respect a user's timezone.

## Principles
1. **Semantic markup first.** Native HTML elements carry accessibility for free: `<button>` is focusable, announceable, and keyboard-activatable; a `<div onclick>` is none of those. Use the element that matches the meaning; add ARIA only to fill gaps the platform cannot.
2. **Keyboard paths everywhere.** Every action reachable by mouse must be reachable by keyboard. Tab order follows visual order; focus is visible; focus traps inside modals release on escape. This is also the single best proxy for screen-reader compatibility.
3. **Color is decoration; meaning is text.** Red-on-green graphs convey nothing to color-blind users and nothing to screen readers. State, severity, and category must be expressed in text or icon in addition to color. Check color contrast against WCAG AA at minimum.
4. **Externalize every user-facing string.** Strings in source code are strings that will never be translated. Extract to a message catalog (`gettext`, ICU, FormatJS, etc.) from the start, even if only English is shipped -- the discipline of the catalog surfaces concatenation bugs and gendered-language assumptions early.
5. **No string concatenation for sentences.** "You have " + count + " items" breaks in every language that does not follow English word order. Use message formatters with placeholders: `{count, plural, one {# item} other {# items}}`. The platform handles plural forms, gender, and ordering.
6. **Unicode everywhere.** UTF-8 by default, in storage, in transport, in logs. Byte-based string functions (`substr`, `length`) will produce garbage on multibyte characters. Normalize forms (NFC) at boundaries.
7. **Locale is per user, not per server.** Server-generated content (emails, PDFs, error responses) should respect the requesting user's locale, not the server's environment. Pass locale explicitly through the request; never rely on process-level `LC_*`.
8. **Dates, numbers, and currencies are formatted, not concatenated.** `Intl.DateTimeFormat`, `Intl.NumberFormat`, or platform equivalents. Handling these by hand reinvents bugs that platforms have solved for years (DST transitions, thousands separators, Arabic-Indic digits).
9. **RTL is a first-class layout.** If the product will support Arabic, Hebrew, or Persian, RTL is not "flip the CSS"; it is a mirror of the whole layout. CSS logical properties (`margin-inline-start` instead of `margin-left`) generalize the common cases; component library must be tested in RTL from early.
10. **A11y is testable.** Automated tools (axe, Lighthouse, pa11y) catch a large fraction of defects; integrate into CI as regression checks. Manual testing with a screen reader (NVDA, VoiceOver) and keyboard-only navigation catches the rest. Neither replaces the other.

## Anti-patterns
- Buttons implemented as `<div onclick>`, invisible to screen readers and unreachable by keyboard. A link to "do not submit" for assistive tech users.
- Forms with labels not associated to inputs (`<label>` without `for=`), forcing screen-reader users to guess which input belongs to which label.
- Error messages expressed only by color ("red border means invalid"), leaving color-blind and screen-reader users with no signal at all.
- String concatenation in source: `"Welcome, " + user.name + "!"` shipped to translation without placeholder semantics, producing "¡Bienvenido, Ehab!" or worse depending on translator interpretation.
- Hardcoded `en-US` locale assumed throughout the codebase, surfacing as bugs when the first non-US customer reports a date parsing error.
- Fixed-width UI that overflows in German, truncates in Chinese, and breaks utterly in Arabic -- because "English fits".
- Accessibility addressed in a one-week sprint before launch, producing cosmetic ARIA labels on a structurally inaccessible app.
- Byte-based string operations on user input, chopping emoji in half and corrupting non-Latin text.
- Automated a11y passing because the tool cannot see that the `<div role="button">` does not actually do anything on Space/Enter.
- "We will internationalize when we expand to another market" -- by which time the string concatenation, date formatting, and RTL assumptions are buried in hundreds of files.

## References
- W3C -- Web Content Accessibility Guidelines (WCAG) 2.2 (w3.org/TR/WCAG22)
- Heydon Pickering -- *Inclusive Design Patterns* and *Inclusive Components*
- W3C Internationalization Activity (w3.org/International)
- MDN -- Accessibility and Internationalization guides
- Deque axe-core -- automated a11y testing
