---
name: dead-weight
description: >
  Find what can be deleted from afunto with zero behaviour change -
  unreferenced symbols, unreachable routes and endpoints, features nothing
  triggers any more, unused locale keys and assets, and junk that should
  never have been tracked. Use when the maintainer types /dead-weight, or
  asks what can be deleted, what is dead or unused, what is still wired up,
  or wants a cleanup sweep. Each finding carries the exhaustive search that
  proves it.
disable-model-invocation: true
---

# Dead weight

Two questions with one answer: what can be deleted, and what proves it.
Symbols and capabilities are the same hunt from different ends - a handler
nobody dispatches and a feature nobody can reach both delete the same way -
so they are one run, not two.

## Before anything else

```bash
cat .claude/review/contract.md .claude/review/repo-map.md
```

Read both in full. `contract.md` holds the evidence bar, the dedup rule,
the issue cap and the filing shape; this file restates none of it.
`repo-map.md` holds the traps - especially the DI trap and the dynamic-key
trap, both of which produce confident false positives here.

**This skill only runs when the maintainer asks for it.** If you reached it
on your own initiative, stop and say so instead of running.

## Step 1 - The cheap pass, always first

Seconds of work, and it has caught real things twice in the last month:

```bash
git ls-files | grep -iE '\.(log|tmp|bak|orig|rej|DS_Store)$|(^|/)(bin|obj|dist|node_modules)/'
git ls-files -z | xargs -0 du -b | sort -rn | head -20
```

Flag build artifacts, editor and OS junk, caches, stray archives, and
anything secret-shaped. Then check `.gitignore` adequacy against a real dev
day - build outputs, IDE directories, Aspire artifacts: are they all
covered, and is anything tracked *despite* matching a rule?

A "should be ignored" finding names the tracked path AND the ignore rule
that ought to cover it.

## Step 2 - Dead code

**Frontend, tooling-backed.** `pnpm install`, then `npx knip` for unused
files, exports and dependencies. knip has no config in this repo, so treat
its output as a candidate list and verify every hit by hand. Then:

- Unused locale keys: run the repo's own i18n checker first - it is the
  authority - then check dynamic key construction for anything it flags.
- Unused assets in `frontend/public/`: grep each filename across `src/` and
  `index.html`.
- CSS selectors never used in any `className`. Class strings are composed
  in places, so trace the composition before flagging.
- Dependencies knip flags: cross-check the vite config, the eslint config
  and the package scripts. Build-time usage does not show up as an import.

**Backend, static.** Enumerate public types
(`grep -rn "public \(class\|record\|interface\|enum\)" backend/src`), then
run an exhaustive reference search across all of `backend/` for each
candidate. Mind the DI trap: handlers, validators and endpoints are
dependency-injection entry points, so a handler is dead only if its
*request type* is never constructed by any endpoint. Also hunt unreachable
branches - conditions on enum values that no longer exist, flags that are
constant, `if` arms the type system already excludes.

Unused NuGet packages are **Likely at best** without a build: analyzers and
implicit MSBuild behaviour consume packages invisibly. Say so in the
evidence.

**Repo-wide.** Scripts in `frontend/scripts/` that nothing invokes - search
the workflows, the package scripts, the docs and the hooks. Agents or
skills under `.claude/` that nothing references - check the `AGENTS.md`
files before flagging.

## Step 3 - Dead features

The unit here is a *capability*, not a symbol.

1. **Route reachability**: extract the route table from the router setup.
   For each route, is it linked from any navigation, page or redirect?
   Orphans are candidates - but check deep-link legitimacy first, because a
   QR or scan flow is entered externally by design.
2. **Endpoint to UI reachability**: list the backend endpoints, map each to
   its generated-client method, then to call sites in `src/`. A client
   method with zero call sites means the feature exists server-side with
   nothing triggering it. Report it as dead-from-UI and say so plainly: the
   API is public, so "delete" versus "document as API-only" is the
   maintainer's call, not yours.
3. **Write-only data**: entity properties set but never read in any
   handler, projection or display. Trace both directions.
4. **Toggles and config**: flags, switches or environment variables that
   are read but constant in every environment. The disabled arm is a dead
   feature.
5. **Half-features**: UI that renders but leads nowhere - buttons without
   handlers, modals with no open-trigger - and locale strings for flows
   that do not exist.
6. **Keycloak surface**: realm flows, clients, roles and theme templates
   the app's auth setup never exercises.

## Verification bar

Every finding shows the exhaustive search: the exact commands, and either
"zero hits" or the full hit list explained away. Reachability claims are
graph claims, so show the traversal - "route X appears at App.tsx:NN;
searched `to=`, `navigate(` and `href` across src/, zero references."

If you cannot search exhaustively because of dynamic dispatch or
reflection, the finding is Likely and the evidence names the assumption you
could not check. Anything resting on "probably nobody uses this" is a
Hypothesis, which the contract does not let you file.

Give an estimated deletion size per finding - files and lines. It is what
lets the maintainer sequence the cleanup.

## Traps

Admin-only surfaces look orphaned from the normal navigation - check
role-gated routes before flagging. External entry points reach routes with
no in-app link: QR codes, notification emails and Keycloak redirects.
Search the email and notification templates for URLs before declaring a
route orphaned.

The generated API client is committed on purpose and hook-protected - it is
not junk. There is no root `package.json` and no root `scripts/` directory;
both were removed deliberately, so their absence is not a finding. Nothing
in this repo deploys or hosts the app, so a missing compose file or deploy
workflow is not a gap either.
