# Afunto

English-source UI strings and code. See `CONTRIBUTING.md`'s Language Convention for the full breakdown.

## Monorepo Structure

```
afunto/
├── backend/        .NET 10 Clean Architecture API        → backend/AGENTS.md
├── frontend/       Vite SPA + React 19 + Tailwind CSS 4  → frontend/AGENTS.md
├── keycloak/       Custom Keycloak image + realm config  → keycloak/AGENTS.md
├── docs/           arc42 architecture docs + ADRs        → docs/AGENTS.md
└── .github/        CI workflows + issue templates        → .github/AGENTS.md
```

## Tech Stack (quick ref)

See README.md's Tech Stack table for the full breakdown.

## Development Setup

Required: .NET SDK **10.0.401** (enforced via `backend/global.json`).

```bash
dotnet run --project backend/src/Aspire/AppHost
```

Aspire AppHost provisions Postgres, Keycloak, backend API, and the Vite frontend. URLs surface in the Aspire dashboard. See README.md's Services and Test users tables for the full list.

## Key Conventions

- Feature folders: `{Layer}/{Domain}/{Feature}/v1/` in the backend (`Api/`, `Application/` and `Domain/` all repeat the same module folders). The frontend is cut by artifact kind instead (`pages/`, `components/`, `hooks/`, `lib/`), with organizer routes grouped under `pages/app/` - see chapter 5 of the arc42 docs
- Routes: `/v{version:apiVersion}/...`, namespaces: `.v1`
- Commits, commands/queries/DTOs, and async conventions: see `CONTRIBUTING.md`'s Code Style and Commit Messages sections
- Domain vocabulary: `docs/Architecture/src/12_glossary.adoc` is canonical for the domain nouns (Engagement, Occurrence, Time Slot, Volunteer Opportunity) - check it before naming a type, an i18n key, or a UI string
- **Never use Unicode dashes** (U+2013 en dash, U+2014 em dash) in source files - write plain ASCII hyphens (`-`) instead; CI rejects non-ASCII dashes. The one exception is German user-facing content - `frontend/src/locales/de.json` and `backend/src/Infrastructure/Email/Templates/de.json` - which uses the en dash (Gedankenstrich) as German typography requires; see `CONTRIBUTING.md`'s Language Convention
- **Tab indentation is the default** (`.editorconfig`'s `[*]` rule) - shell scripts, AsciiDoc (`.adoc`), and PlantUML (`.puml`) all use tabs. Only `.md`, `.json`, `.yml`/`.yaml`, and `.py` (PEP 8) are overridden to spaces. CI's `editorconfig` job enforces this; when writing `.adoc` prose keep paragraphs on one unwrapped line rather than hand-wrapping with space-indented continuation lines

## Claude Code Configuration

`.claude/` holds the self-review setup for this repo (all report-only, never
edit on your own initiative):

- **Agents** (`.claude/agents/`) - proactive subagents invoked automatically
  when their trigger condition matches, no explicit request needed:
  `nswag-check` (endpoint/DTO changes vs. generated clients),
  `ef-migration-check` (entity changes vs. EF Core migrations),
  `architecture-check` (Clean Architecture layer/naming/rate-limiting rules),
  `a11y-check` (frontend components vs. the a11y conventions in
  `frontend/AGENTS.md` - scoped to only what ESLint's `jsx-a11y` ruleset
  can't already catch, see the agent file for why),
  `i18n-check` (`en.json`/`de.json` translation key parity - a local
  pre-flight for `frontend-checks.yml`'s `pnpm i18n:check`, which already
  enforces parity plus placeholder, plural and unused-key rules in CI; the
  agent's value is naming the offending key from the diff before a push
  spends a CI round on it).
- **Skills** - `.claude/skills/self-review/` (`/self-review`, its frontmatter
  description covers what it does; run it before opening a PR).
  Six **review skills**, each invoked by name and only by name
  (`disable-model-invocation: true` - they file GitHub issues, so none of
  them starts on its own): `/walkthrough` (use the running app as a person
  would, in German, then anchor every finding in source - this is the one
  that converts best, and it needs a reachable instance plus a browser),
  `/bug-hunt` (trace one backend slice end to end, plus the security smells
  no CI gate covers), `/dead-weight` (what can be deleted, with the
  exhaustive search that proves it), `/docs-drift` (does the documentation
  fit its reader, and is it still true), `/untangle` (code harder to change
  than it needs to be, and the comments around it), `/gates` (what actually
  catches a regression: CI coverage, test gaps, pipeline cost). All six are
  report-only - GitHub issues, never code, never a PR - and all six share
  one contract: `.claude/review/contract.md` holds the evidence bar, the
  dedup rule, the issue cap and the filing shape, and
  `.claude/review/repo-map.md` is the only place a repo fact may live. A
  skill states method and cites those two; it never restates them.
  `.claude/skills/frontend-design/` (vendored from `anthropics/skills`,
  Apache-2.0, `LICENSE` alongside it) pushes frontend redesign work
  toward a deliberate, non-generic visual direction - typography, color
  theming, motion, spatial composition - instead of generic AI-layout
  defaults; load it before visual/layout changes to frontend components
  or pages. `.claude/skills/grilling/` (vendored from `mattpocock/skills`,
  MIT, `LICENSE` alongside it) stress-tests a plan *before* it is built -
  load it before starting a new vertical slice, feature or user-facing
  flow, the class of work where #1979, #1834, #1696 and #1881 shipped and
  were then deleted for reasons answerable up front. Report-only, like the
  other two: it asks, it never builds.
- **Hooks** - `.claude/hooks/protect-generated-clients.sh` blocks Edit/Write
  on the generated clients (`frontend/src/client/generated/*` from
  `@hey-api/openapi-ts`, `backend/tests/IntegrationTests/ApiClient.cs` from NSwag, and
  `backend/src/Api/wwwroot/openapi-v1.json` from GenerateOpenApiDocuments - not
  NSwag, which only reads it; see README.md's Tech Stack table, "API client"
  row).
  `.claude/hooks/pre-stop-verify.sh` (`Stop` hook) runs once before ending a
  turn if anything under `backend/src`/`frontend/src` changed (committed,
  uncommitted or untracked, measured against the merge-base with `main`):
  `dotnet build` of `IntegrationTests`/`ArchitectureTests`/`Application.UnitTests`
  - which covers all of `src` plus every test project but `VisualTests`,
  left out because its build installs Playwright browsers - then
  `pnpm lint`+`check`, then the pinned `editorconfig-checker` over just the
  changed files. It blocks only on an actual failure (capped at 2 blocks per
  session so it fails open rather than risk a loop) and stays silent when the
  binary cannot be fetched - a safety net since this routine has no human
  review before a PR goes out. The `SessionStart` hook
  (`.claude/scripts/session-start.sh`) installs the .NET SDK version pinned
  in `backend/global.json` via `dotnet-install.sh` whenever `dotnet` is
  absent from `PATH` (see this file's Development Setup for the SDK
  requirement itself), then - every session, unconditionally - runs
  `dotnet build` on `backend/tests/IntegrationTests/IntegrationTests.csproj`
  to regenerate both the OpenAPI document and the C# client (that project
  references Api, so one build refreshes both) and `pnpm format:write` in
  `frontend/`. Both can rewrite tracked
  files, so check `git status` before assuming a dirty tree is your own
  doing.
- **Plugins** - the `dotnet/skills` marketplace (`dotnet-aspnetcore`,
  `dotnet-test`, `dotnet-nuget`, `dotnet-data`) plus `csharp-lsp`,
  `typescript-lsp`, and `playwright` (live browser control) are enabled in
  `.claude/settings.json`. **MCP tool grants don't propagate to an
  `Agent`-tool subagent** - drive browser sessions (e.g. a design review)
  in the current session directly, never delegate them. Availability can
  also vary turn-to-turn even in the main session - `ToolSearch` for
  `browser_navigate` first.
- **Removed on purpose** - read the linked PR before proposing any of these
  back; each was tried here and cost something. `playwright-skill` (#141 -
  superseded by the `playwright` plugin above). `issue-triage` (#788 - it
  shipped fixes for findings nobody had reviewed end to end, which is why
  the review skills are report-only). `persona-simulation` and
  `deep-lens-review` (#788 - one shape wearing three names, merged into the
  `lens` skill, which the six review skills above later replaced). The OKF
  wiki and its `ingest`/`query`/`lint` skills (#1705 - a second source of
  truth beside `docs/` that drifted from it). `live-verify` and the personas
  lens (#2165 - both drove a staging site this repo no longer owns, see
  ADR-7). The `lens` skill itself and four of its twelve lenses: an outcome
  audit found only ~28 of the 522 issues carrying its label were its own
  output, and that the shared contract restated per lens had already drifted
  - the same false claim was fixed in one copy (#1879) and survived 37 days
  in another. `contributor-dx` went with it: it simulated an arrival journey
  that exactly one outside human has walked in 1,194 commits. `repo-hygiene`
  survives only as `/dead-weight`'s first step, its standard-files half
  having been closed by its own earlier findings. `ci` and `test-gaps` merged
  into `/gates`; `dead-features` into `/dead-weight`; `comment-bloat` and
  `complexity` into `/untangle`; `accessibility` into `/walkthrough`, where
  the driven pass it needs actually happens.

## Sandbox Limitations (Claude Code on the web)

- **No reliable Docker** - `dotnet run --project backend/src/Aspire/AppHost`, the `IntegrationTests` project (Aspire), and the `VisualTests` project (Aspire + Playwright) all need real container networking. Don't try to run them locally in a web/cloud session, even if `docker info` succeeds - Aspire/DCP orchestration still fails. Verify locally with `dotnet build` + `Application.UnitTests` + `ArchitectureTests` (no Docker needed); CI's `dotnet.yml` runs the full suite including `IntegrationTests`/`VisualTests` on a real runner.
- **Direct pushes to `main` are blocked** by the git proxy (working-branch only) - always commit to the designated `claude/...` branch and open a PR, even if an instruction says to work "directly on main".
- **Run the `editorconfig` check the way CI does** - fetch the pinned release asset directly (`curl -sSL https://github.com/editorconfig-checker/editorconfig-checker/releases/download/v4.0.1/editorconfig-checker-linux-amd64.tar.gz | tar xz`, then run `./editorconfig-checker -config .editorconfig-checker.json`); keep the version in step with `EC_VERSION` in `.github/workflows/lint.yml`. Do not reach for `npx editorconfig-checker` - the wrapper resolves its binary through the GitHub releases API, which the sandbox's git proxy answers with 403, and it pins nothing. `.claude/hooks/pre-stop-verify.sh` now runs this automatically over the changed files under `backend/src`/`frontend/src` before a turn ends, so the common case is covered; run it by hand when a change touches a `.adoc`, `.puml` or shell file outside those two trees, which the hook does not look at.
- **This repo ships fast** - `git fetch origin main` and skim recent commits before any review/analysis task, not just implementation work; assuming last week's state is current wastes most of a review's effort re-finding what already shipped.

## Releases (autonomous from Claude Code on the web)

Releases are driven by tags. The Claude Code on the web git proxy blocks tag pushes (working-branch only), so **do not** ask the user to `git push` a tag - push a `release/vX.Y.Z[-rc.N]` branch instead and let `.github/workflows/release-rc.yml` promote it. Full flow + the one-time `RELEASE_TOKEN` setup are documented in `.github/AGENTS.md` under "Cutting a release from Claude Code on the web". A release ends at published GHCR images and a GitHub Release - nothing in this repository runs or hosts the app.
