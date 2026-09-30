---
name: bug-hunt
description: >
  Trace one backend feature slice end to end hunting defects that produce
  wrong behaviour or wrong data, including the security smells no CI gate
  covers. Use when the maintainer types /bug-hunt, or asks to hunt bugs,
  audit a slice, check correctness, or look for security problems in
  afunto - naming a slice (Engagements, Organizations,
  VolunteerOpportunities, ...) if they have one in mind. Not for reviewing
  a diff (that is /self-review) and not for anything the running app would
  show you (that is /walkthrough).
disable-model-invocation: true
---

# Bug hunt

Defects that produce wrong behaviour for users or wrong data. Not style,
not design taste, not layout.

## Before anything else

```bash
cat .claude/review/contract.md .claude/review/repo-map.md
```

Read both in full before you start. `contract.md` holds the evidence bar,
the dedup rule, the issue cap and the filing shape; this file deliberately
restates none of it, so a run that skips that read will file wrong.
`repo-map.md` holds the repo facts - slice names, traps, tooling - that
this file deliberately does not hard-code.

**This skill only runs when the maintainer asks for it.** If you reached it
on your own initiative, stop and say so instead of running.

## Scope rule - one vertical slice

The codebase cannot be bug-hunted whole. Pick ONE feature slice;
`ls backend/src/Application/` is the authoritative list (every folder but
`Common/`). Take the maintainer's choice if they named one, otherwise the
slice with the highest recent churn and fix density:

```bash
git log --since=21.days --name-only --pretty=format: -- backend/src/Application \
  | sort | uniq -c | sort -rn | head -20
git log --since=60.days --oneline | grep -i '^[0-9a-f]* fix'
```

State the chosen slice and the reason at the top of the run.

## Method - trace the slice end to end

Read in this order, building a model of intended behaviour as you go:

1. **Domain**: entity invariants, value objects, state transitions. Write
   down the invariants the code *implies* ("an engagement cannot be checked
   in twice").
2. **Application**: each command/query handler and validator of the slice.
   Compare validator rules against the invariants from step 1 - the gaps
   between the two are prime bug territory.
3. **Infrastructure**: the slice's EF configuration and queries. Hunt
   missing `Include` before navigation access, unbounded queries, tracking
   vs no-tracking misuse, transaction boundaries around multi-entity
   writes.
4. **Api**: the slice's endpoints. Hunt request-to-command mapping losses,
   status-code semantics, and error responses leaking internals. Do **not**
   spend the run on authorization conventions: `ArchitectureTests` already
   asserts that every endpoint declares its auth, that `/admin/` routes
   carry the admin policy, and that handlers taking a requesting user check
   ownership. A violation there fails CI before you see it. What is still
   yours is whether the *right* rule was declared for this operation's
   sensitivity.
5. **Frontend**: pages and components consuming the slice through the
   generated client. Run the typecheck and lint first - compiler findings
   in the slice are free evidence. Then hunt state updates after unmounted
   async calls, optimistic UI without rollback, error paths that swallow
   failures silently, and whether both locales are handled.

## Cross-cutting checks for the slice

- **Time**: opportunities and engagements carry dates. Check timezone
  handling end to end - Postgres `timestamptz`, client `Date` parsing,
  "today" boundaries.
- **Concurrency**: double-submit on the slice's mutating endpoints - a
  check-in twice, an invitation accepted twice. What enforces idempotency:
  a DB constraint, a domain check, or nothing?
- **Object-level authorization**: can user A act on user B's resource by
  swapping an ID? The convention test proves a check exists; only reading
  it proves the check is *correct*. Trace one mutating endpoint explicitly.

## Security smells, where CI does not already look

A smell review, not a pentest: no exploit development, no attack payloads
in the report. Three of the six areas this used to cover are now gated -
endpoint authorization and ownership by `ArchitectureTests`, the serving
headers by the `check-nginx-*` scripts, dependency advisories by
`pnpm audit` in CI. Spend the time on what nothing checks:

1. **Secrets in tracked files**: pattern scan for `secret`, `token`,
   `apikey`, `password`, `-----BEGIN`, JWTs, connection strings. Judge hits
   against context - the Keycloak realm's dev credentials and `${...}`
   placeholders are expected (see `repo-map.md`'s traps). A real literal
   secret is a finding, and so is one still reachable in git history after
   rotation.
2. **Input handling**: raw or interpolated SQL (`FromSql`, `ExecuteSql`),
   file and upload handling, redirect targets taken from user input,
   check-in payload validation.
3. **Frontend token handling**: where Keycloak tokens live, whether the API
   client attaches them safely, and whether anything sensitive rides in a
   `VITE_`-prefixed variable, which ships to the bundle and is public.
4. **Workflow permissions**: `permissions:` blocks broader than the job
   needs, and any `pull_request_target` usage.
5. **Keycloak themes**: FTL templates echoing user input unescaped.

Every security finding names the concrete exposure and who can trigger it.
If dev-only context plausibly defuses a hit, say so and grade accordingly -
crying wolf on dev credentials erodes the whole report.

## Verification bar

A bug finding carries a **repro narrative**: concrete input, the code path
taken as files:lines in order, and the wrong outcome. "This could be a
race" without that narrative is a Hypothesis, which the contract does not
let you file. Compiler and linter output counts as Confirmed.

## Traps

A validator gap may be covered by a database constraint - read the EF
configuration before reporting missing validation. Frontend "missing error
handling" may be caught by a global boundary or the toast bus - trace the
actual failure path before calling it swallowed.
