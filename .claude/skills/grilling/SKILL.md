---
name: grilling
description: >
  Stress-test a plan before any of it is built. Use before starting a new
  vertical slice, feature or user-facing flow in this repo - a new folder under
  backend/src/Application, a new page under frontend/src/pages, a new entity
  plus its migration - and whenever the user asks to grill, interrogate,
  pressure-test or poke holes in an idea, plan or decision. Not for reviewing
  code that already exists: that is /self-review for a diff, or one of
  this repo's review skills for the shipped app.
license: Complete terms in LICENSE (MIT, vendored from github.com/mattpocock/skills)
---

# Grilling

## Why this exists here

Four complete vertical slices were built in this repo and then deleted:
#1979 (60 files, 3,076 lines), #1834 (18 files, 1,223), #1696 (14 files, 817),
#1881 (16 files, 803) - roughly 5,900 lines across 108 files. Each removal was
itself a day's work: aggregate plus command/query/endpoint, an EF migration,
frontend, locale keys in both languages, tests, arc42 docs.

The stated reasons were "no clear use case for it", "isn't evidence of
anything", and "not ready to ship yet". Every one of those was answerable
before a line was written. That is what this skill is for, and it is the only
thing it is for. If the answer to "what decides whether this ships?" is already
written down, skip it and build.

## The method

Interview the user relentlessly until you reach a shared understanding. Map this
as a **design tree**: every decision branches into the decisions that hang off it.

Work the tree in **rounds**. The **frontier** is every decision whose
prerequisites are already settled: the questions you can ask _now_ without
guessing at answers you haven't heard yet. Ask the whole frontier in one round:
number each question and give your recommended answer. Then wait for the user's
answers before the next round.

Format a round like so:

```
❓ **Q1** - **<question title>**: <question body, might be multiple paragraphs, including multiple choices>

➡️ <your recommended answer>

---

❓ **Q2** - **<question title>**: <question body, might be multiple paragraphs, including multiple choices>

➡️ <your recommended answer>
```

Each round the user answers reshapes the tree: settled decisions push the
frontier outward and unblock questions that depended on them. Recompute the
frontier and ask the next round. A question whose answer depends on another
question still open in this round belongs to a _later_ round, not this one.

Finding _facts_ is your job, never the user's. When a frontier question needs a
fact from the environment (filesystem, tools, the GitHub API), dispatch a
sub-agent to find it; don't ask the user for anything you could look up
yourself. Don't block on it: a running exploration is an unsettled prerequisite,
so only the questions downstream of it wait for the sub-agent to report; ask the
rest of the frontier now. The _decisions_ are the user's: put each to them and
wait.

The session is done when the frontier is empty: every branch of the design tree
visited, nothing left silently assumed. Do not act on it until the user confirms
you have reached a shared understanding.

## Questions this repo has paid for

Ask these before the tree's own branches, because the four deletions above each
turned on one of them:

1. **What observation says someone wants this?** A filed issue, a support
   question, a persona in `docs/`. "It would be nice" is the answer that
   preceded #1834's removal.
2. **What would make you delete it again?** Name the condition now. If it is
   already true, stop here.
3. **Can it ship behind something smaller?** A field on an existing aggregate
   beats a new one; a section on an existing page beats a new route.
4. **What does it cost to carry?** Every slice here is aggregate + handler +
   endpoint + migration + frontend + `en.json`/`de.json` + tests + arc42. An EF
   migration in particular is not free to undo once it has run anywhere.
5. **Who maintains it?** This is a single-maintainer repo. A feature nobody
   returns to is the one that gets deleted.

## Boundaries

- Report and question only - never write code, open a branch, or file an issue
  from a grilling session. The output is a shared understanding the user
  confirms; building is a separate, deliberate act afterwards.
- Do not grill a change that is already scoped: a bug fix, a dependency bump, a
  copy change, a refactor with no user-visible surface.
- For work that already exists, this is the wrong skill. `/self-review` reviews
  a diff before a PR; the six review skills (`/walkthrough`, `/bug-hunt`,
  `/dead-weight`, `/docs-drift`, `/untangle`, `/gates`) review the shipped
  repository.

## Provenance

Vendored from [`mattpocock/skills`](https://github.com/mattpocock/skills)
(`skills/productivity/grilling`), MIT, full terms in `LICENSE` alongside this
file. "Why this exists here", "Questions this repo has paid for" and
"Boundaries" are afunto-specific; the method above is upstream's, kept
close to verbatim so it can be re-synced.
