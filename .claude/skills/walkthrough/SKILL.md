---
name: walkthrough
description: >
  Use afunto as a real person would, in German, then prove every
  suspicion against the source before filing it - the pass that finds
  duplicate notifications, dead-end states, inconsistent copy and
  accessibility failures no linter can see. Use when the maintainer types
  /walkthrough, or asks to go through the app, try it as a volunteer or
  organiser, check a flow end to end, or look for UX and copy problems.
  Needs a reachable instance and a browser; ask for the URL.
disable-model-invocation: true
---

# Walkthrough

The highest-converting review this repo has ever run: 29 issues filed on
2026-08-12, 22 of them cited by a closing keyword in a merged commit. It
had no written method until now. The maintainer reproduces it by hand -
31 findings in German on 2026-09-11 - which is the clearest possible signal
that it is worth having.

What makes it convert is not the browsing. It is the second phase.

## Before anything else

```bash
cat .claude/review/contract.md .claude/review/repo-map.md
```

Read both in full. `contract.md` holds the evidence bar, the dedup rule,
the issue cap and the filing shape; this file restates none of it.

**This skill only runs when the maintainer asks for it.** It drives a real
instance and can create, cancel and withdraw real data. If you reached it
on your own initiative, stop and say so instead of running.

## What you need, and what to do without it

- **A reachable instance.** Ask the maintainer for the URL. Do not
  hard-code one: this repository deliberately does not describe or drive a
  deployed environment (ADR-7), so the address is theirs to supply, not
  this file's to remember. Locally, the Aspire AppHost serves the same
  purpose.
- **A browser.** `ToolSearch` for `browser_navigate` (the `playwright`
  plugin). Drive it in the current session - MCP grants do not propagate to
  a subagent, and one asked to drive a browser comes back empty-handed.
- **Credentials.** The demo accounts and their roles are in README's Test
  users table.

If no instance is reachable or no browser resolves, **stop and say so**.
Do not silently degrade into a static code read: that is what the other
five skills are for, and a walkthrough that never saw the app is just a
worse `/bug-hunt`.

## Phase 1 - Use it

Pick ONE persona and ONE journey per run. The personas are in README;
in practice: a volunteer looking for something to do, or an organiser
running an opportunity.

Use the app in **German**. Half the findings that converted were copy
findings, and they are invisible in English.

Walk the journey the way a person would, not the way a test would - follow
what the interface suggests next rather than a script. Candidate journeys,
one per run:

- Find an opportunity, read its detail page, sign up for a slot, then
  withdraw again.
- Create an organisation, create an opportunity with time slots, invite a
  member, review who signed up.
- Arrive signed-out on a deep link, sign in, and see where you land.
- Switch language mid-flow and carry on.
- Do the whole thing on a phone-width viewport.

As you go, note anything that makes you pause. The categories that
converted:

- **The same thing twice.** Two notifications for one action; a title
  printed twice; a count that disagrees with the list below it.
- **Dead ends.** A button that looks clickable, refuses, and never says
  why. A zero-count card that still links into an empty list. An error
  state that collapses three different causes into one generic screen.
- **Inconsistency next to better work.** Three conventions for marking a
  required field. Three different right edges on one page. A card whose
  date slot means something different from the card beside it.
- **German that is off.** A calque, a formal register where the rest says
  "du", one verb doing two unrelated jobs, a native browser validation
  bubble in English on a German page.
- **State that does not survive.** A page behind a modal that still
  scrolls. A menu that stays open after navigating. A form that silently
  keeps or drops what you typed.
- **Accessibility you can only find by doing it.** Tab through a form or
  modal with the keyboard alone: does focus order match visual order, is
  the focused element ever invisible, does Escape close what it should, is
  focus returned when a modal closes? Where the browser is already driving
  the page, inject `axe-core` against a state the CI suite never reaches -
  a modal, edit mode, an error state - and treat a hit as Confirmed.
  Check contrast on *rendered* output, not class names: text over banner
  images, disabled text, placeholder text.

Capture as you go: a screenshot, the exact route, and the network or
console evidence. A claim without them cannot be filed.

## Phase 2 - Anchor every finding in source

**This is the phase that makes the difference, and it is not optional.**
In the run this method comes from, the anchor pass killed one finding
outright, inverted the proposed fix on another, shrank two, and turned up
three defects the browsing had never seen.

For every suspicion from phase 1:

1. Find the component, handler or locale key responsible. Name it as
   `path:line`.
2. Re-derive the behaviour from the code. Does the source actually say what
   you think you saw? Screens lie - stale caches, a service worker, a
   half-applied migration, your own session state.
3. Decide what the fix would touch. A finding whose fix you cannot locate
   is a finding you do not understand well enough to file.
4. Look at the neighbours. The code that produced one instance usually
   produced several; a finding filed with all its locations is worth more
   than three filed separately, and the contract requires grouping them.

A finding that survives phase 2 is **Confirmed** and carries both halves:
what you saw, and the line that causes it. A finding that only ever existed
on screen is **Likely** at best - say which assumption you could not check.

## Do no harm

You are driving a real instance with real data. Before any destructive beat
- blocking a user, removing a member, cancelling an opportunity, granting
or stripping a role - stop and ask the maintainer. Read-and-create is fine;
destroy-and-revoke is theirs to authorise. Prefer data you created in this
run over anything that was already there.

## Verification bar

Every finding names the persona, the route, and what you did to reach it,
plus the `path:line` from phase 2. State how it was checked - observed,
keyboard-driven, axe-injected, or contrast-measured on rendered output -
because the confidence bar differs per method.

"A screen reader user would struggle" without having driven the flow is a
Hypothesis, which the contract does not let you file.

## Traps

Do not re-report what the automated gates already guarantee: `jsx-a11y`
runs on every PR, and there are two axe layers - component suites under
`frontend/src/**/*.a11y.test.tsx` and page scans in the visual tests.
Confirm the specific rule does not cover your case before writing it up,
not merely that the page's own test passes. It may pass because the
violation lives in a state the test never reaches.

A component-library default - a native select's styling, a browser's own
focus ring - is not a finding just because it looks plain.

Retry with a wait before calling anything broken. A slow first paint, a
cold container and a service-worker cache all look exactly like a bug for
the first two seconds.

Deep links reached from a QR code, a notification email or a Keycloak
redirect are entered from outside by design. "Nothing links here" is not a
finding until you have searched the email and notification templates.
