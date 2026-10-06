---
name: gates
description: >
  Ask what actually catches a regression in afunto before it ships -
  which CI workflows gate which dimension, where a PR can break something
  silently, where the test net is thin relative to risk, and where CI is
  slow or wasteful. Use when the maintainer types /gates, or asks whether
  CI covers something, why the pipeline is slow, what is untested, or where
  the safety net has holes.
disable-model-invocation: true
---

# Gates

One question: what stands between a mistake and `main`? The workflows and
the test suites are two halves of that answer, and looking at either alone
produces the wrong verdict - a gap in CI is often covered by a test nobody
runs in CI, and a thorough suite that no workflow gates is measurement, not
protection.

## Before anything else

```bash
cat .claude/review/contract.md .claude/review/repo-map.md
```

Read both in full. `contract.md` holds the evidence bar, the dedup rule,
the issue cap and the filing shape; this file restates none of it.

**This skill only runs when the maintainer asks for it.** If you reached it
on your own initiative, stop and say so instead of running.

Size the job first rather than trusting any number written down:

```bash
ls .github/workflows | wc -l
cat .github/workflows/*.yml | wc -l
```

## Part 1 - What CI gates

1. **Gate completeness.** Enumerate the dimensions from the workflows
   themselves, never from a list in a file like this one - it goes stale
   faster than the workflows do. For each: which workflow enforces it, on
   which trigger? Build the matrix. Holes are things a PR can break
   silently. Cross-check against what runs *before* CI in
   `.claude/hooks/pre-stop-verify.sh`: a gate mirrored there fails earlier
   and cheaper, and a gate that exists only in CI is a candidate to mirror.
2. **Trigger and path-filter correctness.** Do the `paths:` filters match
   the actual layout? A filter that misses a file the job depends on lets
   relevant changes skip CI entirely. The reverse too: workflows running on
   changes that cannot affect them.
3. **Cross-workflow duplication.** The same logic re-implemented in several
   workflows. Checkout and setup blocks are fine; duplicated *logic* -
   version derivation, build scripting - is drift risk. The largest
   workflow is the prime suspect; assess whether it should decompose into
   reusable workflows or composite actions.
4. **Failure semantics.** `continue-on-error` masking real failures, jobs
   whose failure blocks nothing, missing `concurrency` groups leaving
   superseded pushes running.
5. **Action hygiene.** Pinning strategy - tag against SHA, where
   consistency matters more than dogma - what Renovate manages, and any
   deprecated actions or runners.

## Part 2 - What the tests cover

6. **Inventory the suites.** For the backend unit tests: enumerate tested
   handlers against all handlers per feature - mechanically countable, so
   produce the table. For the integration tests: which endpoints and flows
   are exercised? For the architecture tests: which rules exist, and do
   they encode the layering the docs claim? For the visual tests: which
   pages, which viewports.

   The frontend has Vitest gated in CI alongside tsc, eslint and the visual
   suite, so enumerate it the same way - count the test files against
   `src/lib/` and `src/components/`, and read `frontend/AGENTS.md`'s Unit
   Tests section for what belongs where.

7. **Risk-rank what is uncovered.** Cross the gap map with mutating
   endpoints, data-integrity logic (check-in, invitations, achievements),
   auth-sensitive paths, and the churn and fix-density hotspots. An
   untested pure formatter is Low; an untested state transition on
   Engagements is High.

8. **Sample assertion quality.** Read about ten existing tests across
   projects. Hunt tests without meaningful assertions, tests asserting that
   mocks were called rather than that outcomes happened, copy-paste tests
   covering the same thing, and test names that lie about their content.

   Mutation score is the oracle here and it is already computed:
   `pnpm mutation:since` scores what a diff touched, and the mutation
   workflow runs the .NET side. Note that it is deliberately report-only
   with no threshold - measurement, not a gate - which is itself worth
   weighing in part 1.

## Part 3 - What CI costs

9. **Measure, do not guess.** The GitHub API gives run durations and
   conclusions (`/actions/runs?per_page=50`), and per-run `/jobs` gives
   step timing for the slowest. Identify the slowest workflow, the slowest
   recurring step, and the failure rate.
10. **Caching.** NuGet, the pnpm store, Docker layers - cached where it
    pays, keyed on a lockfile, or keyed so it never hits?
11. **Wasted work.** Full builds where a path filter would skip, matrix
    entries nobody consumes, artifacts uploaded and never downloaded.

## What this skill can run locally

The unit and architecture suites need no Docker and can be executed here -
running them turns a claim into evidence. The integration and visual suites
need real container networking and cannot run in a web session; see
`AGENTS.md`'s Sandbox Limitations. Where you could not execute, say so and
cap the claim at Likely.

## Verification bar

Health findings cite the workflow file and line. Coverage gaps come from
the enumeration table, which makes them Confirmed. Assertion-quality
findings quote the test as `path:line` and state the outcome it fails to
pin down. Performance findings cite measured durations from the API; a
caching suggestion without a measured or clearly reasoned cost is a
Hypothesis, which the contract does not let you file.

Recommendations name concrete first test cases, never "add more tests".
Estimate saved minutes per month where you can - the maintainer decides
with numbers.

## Traps

Workflow length is a smell, not a finding; the finding is the concrete
duplication or coupling inside it.

Some redundancy is deliberate defence in depth - the same check running
locally in the Stop hook and again in CI is the design, not a duplication
finding.

Standard runners on public repos have unlimited free minutes, so "wasted
minutes" arguments are about feedback latency, not money. Say which one you
mean.
