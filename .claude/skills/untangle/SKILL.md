---
name: untangle
description: >
  Find code in afunto that is harder to change safely than the
  problem requires, and the comments that mark it or bury it - structural
  hotspots, god files, piled-up hooks, and comments that restate, narrate
  or have gone stale. Use when the maintainer types /untangle, or says a
  file has got out of hand, asks what needs refactoring or splitting, or
  wants the comments cleaned up. Reports only; it never refactors.
disable-model-invocation: true
---

# Untangle

Two questions that kept getting filed as one: is this code harder to change
than it needs to be, and are the comments around it carrying their weight?
They share their evidence - a hedge comment is often the only marker of
real fragility, and a density outlier can mean either good documentation or
tangled code - so arbitrating a boundary between two skills cost more than
it bought.

Complexity is not a defect on its own. *Unexplained or unnecessary*
complexity is.

## Before anything else

```bash
cat .claude/review/contract.md .claude/review/repo-map.md
```

Read both in full. `contract.md` holds the evidence bar, the dedup rule,
the issue cap and the filing shape; this file restates none of it.

**This skill only runs when the maintainer asks for it.** If you reached it
on your own initiative, stop and say so instead of running.

## Method

1. **Find structural hotspots.** No complexity linter is configured here -
   verify that before assuming, in `eslint.config.js` and any analyzer
   ruleset - so use heuristics and say so:
   - *Frontend*: component and function line count, JSX nesting depth, and
     hook count per component. `useState`/`useEffect`/`useMemo` piling up in
     one file is a real signal; it usually means the component is doing
     several jobs.
   - *Backend*: method length, cyclomatic proxies (count of `if`,
     `else if`, `switch` arms, `&&`, `||` per method), and long parameter
     lists as a coupling smell.
   - *Both*: conditionals or callbacks nested four levels deep, and any
     function whose name no longer describes everything it does - a `Save`
     that also sends notifications and recalculates achievements.

   Churn is the multiplier. A file that changes constantly *and* is long or
   deeply nested is the highest-value target, not the longest file in
   isolation:

   ```bash
   git log --since=60.days --name-only --pretty=format: -- backend/src frontend/src \
     | sort | uniq -c | sort -rn | head -25
   ```

2. **Read the comments as evidence.** Grep for hedge and warning language -
   `careful`, `hack`, `workaround`, `don't`, `must`, `NOTE`, `WARNING`,
   `important`, `order matters` - across `backend/src` and `frontend/src`.
   A defensive comment usually marks real fragility: an implicit ordering
   dependency, a non-obvious invariant. Read the code it sits on and judge
   whether the comment is compensating for complexity that could instead be
   removed - extract a function, name the invariant in a type, add a guard
   clause - rather than merely narrated.

3. **Find comment-density outliers.** Rough ratio per file (comment lines
   against total) across `backend/src` and `frontend/src`. Sample enough
   files first to learn this codebase's own norm, then flag what sits
   markedly above it - never a universal threshold. Backend XML doc
   comments repeated near-verbatim across many similar handlers are a
   distinct boilerplate pattern worth its own line even where no single
   file is an outlier.

   A density outlier cuts both ways: good documentation of real complexity,
   or code that needed that many words *because* it is more tangled than it
   should be. Read enough to tell which.

4. **Classify each comment candidate.**
   - **Restates the code** - `// increment counter` above `counter++`; a
     docstring re-listing parameter names in prose.
   - **Over-explains the obvious** - a paragraph where the code needs a
     clause.
   - **Narrates process, not invariant** - "we changed this because the old
     approach broke X", "added for the Y flow", "fix for #NNN". That
     belongs in the commit message; it rots the moment the code moves on.
   - **Stale** - describes behaviour the code no longer has. Cross-check
     `git blame` when the mismatch is surprising.
   - **Load-bearing** - explains a non-obvious *why*: a hidden constraint,
     a workaround, a subtle invariant. Read it, do not flag it, and note a
     couple in the report as a positive baseline rather than filing only
     complaints.

   Judge length against necessity, not a word count. A comment's length
   should track the complexity of the *why*, not the length of the code
   below it - five comment lines above a five-line function are suspect
   regardless of content. The test: would deleting this confuse the next
   reader? If not, it is a deletion candidate, not a trim candidate.

5. **Check the neighbours.** Bloat clusters. If `git blame` or a commit
   message on a candidate points at one PR or session, check the other
   files that commit touched before generalising from one file to
   "this is everywhere".

6. **Weigh against this repo's own stated preference.** The arc42 quality
   goal "Simple code: the source code should be simple enough that anyone
   can contribute" (`docs/Architecture/src/01_introduction_and_goals.adoc`)
   is a named goal, not an aspiration to ignore. Weight a finding higher
   where a newcomer would plausibly land early; lower where the complexity
   is inherent to a genuinely hard problem - the dashboard drag, resize and
   overlap logic, geocoding, timezone handling - and a simpler version
   would just be wrong.

## Verification bar

A structural finding cites the file, the specific metric (line count,
nesting depth, branch count) and how it compares to the rest of the
codebase - never a bare "this is complex". A comment finding quotes the
comment and the code it sits on, states the category, and gives a concrete
fix: delete entirely, trim to N lines, or move the one durable *why* into a
single line. Never "shorten this" or "refactor this" - recommend the
smallest concrete change.

A density finding states the actual ratio measured and the baseline it was
compared against, learned from sampling this codebase, not an arbitrary
number.

When a single comment is both a bloat finding and evidence of structural
complexity, file one finding, on whichever is the primary problem. Do not
count it twice.

## Traps

Long is not automatically complex. A component that is long because it
lists twenty plain, independent form fields is lower-risk than a short
function with five nested conditionals.

Comment volume is not a defect by default: domain-inherent complexity can
legitimately need a longer explanation. The target is a comment whose
length matches the *why*'s actual complexity, not a ceiling.

Out of scope: generated code (the NSwag client, EF migrations), test files
with intentionally repetitive setup, license headers, and the vendored
skills under `.claude/skills/` that carry their own provenance notes. A
comment explaining *why* next to genuinely non-obvious code is the target
state this repo already asks for - see `CONTRIBUTING.md`'s Code Style
section and the bar #2167 set repo-wide - so do not flag it as noise.
