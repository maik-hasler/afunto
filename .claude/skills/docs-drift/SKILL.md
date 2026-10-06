---
name: docs-drift
description: >
  Judge afunto's documentation by whether it fits the reader who
  actually uses it, then whether it is still true - README, CONTRIBUTING,
  the arc42 docs, ADRs and TDRs, and every AGENTS.md in the tree. Use when
  the maintainer types /docs-drift, or asks whether the docs still match
  the code, whether AGENTS.md has got too long, or wants the documentation
  reviewed. Not for writing docs, only for finding what is wrong with them.
disable-model-invocation: true
---

# Docs drift

Documentation sized and aimed at the reader who actually uses it, not
documentation that merely accumulated. Audience fit first; accuracy is a
compact second step, not the bulk of the run.

A bloated `AGENTS.md` loads in full every session and measurably degrades
how reliably instructions get followed. A wrong README claim costs a
contributor minutes once caught. Both are findings, weighted by that real
cost - not by which is easier to grep for.

This is the highest-converting review this repo has: the run of
2026-08-14 filed five issues and all five became merged PRs within two
days.

## Before anything else

```bash
cat .claude/review/contract.md .claude/review/repo-map.md
```

Read both in full. `contract.md` holds the evidence bar, the dedup rule,
the issue cap and the filing shape; this file restates none of it.

**This skill only runs when the maintainer asks for it.** If you reached it
on your own initiative, stop and say so instead of running.

## Method

1. **Sort by audience before judging anything.** Arriving human (README,
   CODE_OF_CONDUCT.md, SECURITY.md), contributing human (CONTRIBUTING.md),
   architecture stakeholder (`docs/Architecture`, ADRs, TDRs), AI agent
   (every `AGENTS.md`/`CLAUDE.md` in the tree, loaded automatically).
   Content aimed at the wrong reader is a finding on its own, independent
   of accuracy: setup trivia an agent never needs to re-derive, or process
   detail duplicated between README and CONTRIBUTING instead of one linking
   to the other.

2. **arc42 docs - fit over completeness.** Does each populated chapter earn
   its place, or restate the code without adding a decision or constraint
   (arc42's own "as much as necessary, as little as possible")? Section 3:
   is the business and domain perspective the default view, with technical
   context only where docs.arc42.org calls for it? Section 1.2: top three
   to five quality goals, concrete, stakeholder-tied, buzzword-free - not a
   wishlist? Section 9 and the ADRs: Nygard shape
   (Context/Decision/Status/Consequences), explaining *why* rather than
   restating *what* the code already shows. An ADR the code has since
   abandoned needs a superseding note, not silence - still the sharpest
   finding class here. Do the building-block and runtime views tie back to
   the goals in 1.2, or float free of them?

3. **README, CONTRIBUTING and the community-health files - findability and
   non-duplication.** Does the README answer what this is, who it is for
   and how to start, above the fold (standard-readme's ordering; optional
   sections may be dropped, not padded in to match a template)? Does
   CONTRIBUTING own the process detail with the README only linking to it?
   Are CODE_OF_CONDUCT.md, SECURITY.md and the issue and PR templates each
   doing one job without overlapping (GitHub's Community Health Files
   split)? Restated content between README, CONTRIBUTING and a component
   `AGENTS.md` is a finding - name both locations and say which one should
   keep it.

4. **`AGENTS.md`/`CLAUDE.md` at every level - the reader is an agent.**
   These load in full at every session start, through the `@AGENTS.md`
   import in each directory's `CLAUDE.md`. Nothing is silently truncated,
   but length has a documented cost. Run `wc -l` on every one of them and
   check against the official target - "target under 200 lines per
   CLAUDE.md file. Longer files consume more context and reduce adherence"
   (code.claude.com/docs) - flagging overage in proportion to how far over.
   Then judge signal density: is every line something an agent could not
   otherwise derive - a real constraint, a non-obvious command, a trap that
   has actually bitten a run - or prose it could infer from the code, or a
   restatement of README in a different voice? Flag generic
   verification-reminder filler ("always run the tests", "double-check your
   work"): it spends tokens without adding information a capable model does
   not already default to. Prefer pointers (`file:line`, a linked
   reference, a named skill) over inlined code blocks that belong next to
   the code.

5. **Accuracy - compact, evidence-gated.** Claim-test README, CONTRIBUTING
   and the `AGENTS.md` files against the source of truth: commands exist
   and are spelled right, prerequisite versions match `global.json` and the
   package manifests, the services and ports table matches AppHost, the PR
   process CONTRIBUTING describes matches what the workflows actually
   enforce, and referenced agents, hooks, skills and paths exist. Internal
   links resolve. Most external domains are unreachable from a sandbox -
   list them unverified rather than guessing.

   Include **config drift** here: environment variables actually read in
   code against what AppHost, the workflows and `keycloak/README.md`
   document - searching both naming conventions, per the trap in
   `repo-map.md` - and whether ports and credentials agree between the
   README table and AppHost.

6. **Prose quality - Low or Medium, and last.** Bury-the-verb
   constructions, filler, walls of text, a headline that says nothing,
   inconsistent terminology. `docs/Architecture/src/12_glossary.adoc` is
   the canonical vocabulary: is it stable across docs and UI? Report
   patterns with two or three examples, never a line-by-line copy edit.

## Verification bar

Every finding names the document, quotes at most one line of it, and states
the norm it fails: arc42.org or docs.arc42.org by section, standard-readme,
GitHub's Community Health Files, or the CLAUDE.md size guidance. Never
"this reads long" or "this feels thin".

Length findings show the actual `wc -l` against the ~200-line target and
the adherence-cost rationale - not a claim that content is dropped.
Accuracy findings cite the contradicting source as `path:line` and say
which side is presumably right. Style findings stay Low or Medium and never
outnumber audience-fit and accuracy findings combined.

## Traps

Docs may describe intended future state. Check git history: if the doc
predates the divergent code it is drift; if it postdates it, it may be a
roadmap statement - say which.

`docs/Architecture` is AsciiDoc built to HTML for human readers. Judge the
rendered result's fit, not raw source line count.

A short doc is not automatically better. The ~200-line target is an
adherence guideline, not a technical cutoff - these files load in full
regardless, so cutting real signal to land under the number is its own
finding. Do not confuse it with Claude Code's separate auto-memory file,
which genuinely is hard-truncated and is not part of this repo.

The review skills under `.claude/skills/` and the shared files in
`.claude/review/` are a third category: agent-audience, but loaded only
when a skill is invoked rather than at session start. No line-count target
applies to them - judge them on signal density and on whether a repo fact
has leaked out of `.claude/review/repo-map.md` into a skill body, which is
the specific failure this repo has already paid for twice.

Remember whose budget you are spending: this is a one-person project, and a
recommendation that assumes a team is not actionable. Weight findings by
what one maintainer can carry.
