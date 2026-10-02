# Frontend - Vite + React 19 + Tailwind CSS 4

## Architecture

Vite SPA. React Router v8 for routing. Client-side OIDC via `react-oidc-context`. Static files served by nginx in the released image.

```
src/
├── client/
│   ├── generated/          @hey-api/openapi-ts output - DO NOT HAND-EDIT
│   ├── index.ts            The client every call site uses: one typed function per endpoint
│   └── api-instance.ts     Base URL, auth/locale headers and the shared HTTP error handling
├── lib/                    Framework-free logic and shared class recipes - the layer with no React
│                           and no DOM in it, and the one `pnpm mutation` measures
├── hooks/
│   └── useApiClient.ts     React hook: the client module, with the current token from useAuth()
├── contexts/               React contexts for cross-cutting UI state - ToastContext.tsx,
│                           AuthStatusContext.tsx, QuickActionsContext.tsx,
│                           HeaderOverlayContext.tsx, OrgBreadcrumbContext.tsx
├── components/
│   ├── Header/             Header with auth state (login/logout buttons, org switcher, notifications) + optional per-page breadcrumb/action bar (see `breadcrumb` prop) - split across Header.tsx (orchestrator), DesktopHeader.tsx, MobileHeader.tsx, MobileMenu.tsx, AccountControls.tsx, NotificationDropdown.tsx, NotificationItem.tsx, OrganizationSwitcher.tsx, LanguageSelector.tsx - all exclusive to Header, nothing here is imported outside this folder
│   ├── Footer.tsx          Footer with links and social icons
│   ├── CreateOrganizationModal.tsx     Modal form for org creation
│   ├── OpportunityCard.tsx     The one opportunity card, shared by every surface that shows one (#2054) - /opportunities, the landing page's three-card preview, the org profile's "current needs" list, and the opportunity detail page's "more from this organization" rail. Takes a `headingLevel` (2|3) prop and an `item` that either DTO (`VolunteerOpportunitySummary` or the leaner `PublicOpportunitySummaryDto`) satisfies structurally - fields absent on the leaner DTO (organization identity, tags, banner) gate the blocks that need them rather than switching components. The 128px media band is gated on a `withMedia` prop instead, which is a **per-list** decision, not a per-item one: rendering it only for the items that happen to carry a `bannerImageUrl` gave one grid row two card anatomies and stretched the banner-less siblings to the tallest card's height (#2329). Pass it on the surfaces whose DTO has the field at all (browse grid, landing preview); items without an image get the brand gradient and their category glyph
│   ├── OpportunityCardSkeleton.tsx  The one loading stand-in for the card above - same shell, same blocks in the same order, same `withMedia` gate, so a grid does not re-lay itself out when the results land. Render one per card the page will hold (`pageSize`), not a fixed three (#2329)
│   ├── OpportunityActionPanel.tsx  The opportunity detail page's sign-up rail - a status strip whose tone says whether a visitor can still take part (`open`/`urgent`/`closed`, never `accent-400`: warning states belong on the amber scale), the three facts that decide it (when/how/where) as a `<dl>`, then the call to action. It holds the facts *because* they used to sit in a separate "at a glance" band restating the schedule and location sections directly below it (#2330). Keep `<dt>`/`<dd>` one level under the `<dl>` - a wrapper column for the icon puts them two deep and fails axe's `dlitem`
│   ├── VolunteerOpportunitiesList/     Paginated list (size=10), filter bar - split across VolunteerOpportunitiesList.tsx (orchestrator), CategoryGlyph.tsx, MiniCalendar.tsx, FilterDropdown.tsx, OpportunityResultsList.tsx, useVolunteerOpportunitiesData.ts, useOpportunityDateAvailability.ts, useCitySuggestions.ts - exclusive to this folder, all consuming the shared `OpportunityCard` above
│   └── CreateVolunteerOpportunityModal/  Multi-step modal form for opportunity creation - split across index.tsx (orchestrator: form state, step navigation, banner upload, recurrence), BasicsStep.tsx, FormatStep.tsx, LocationStep.tsx, DetailsStep.tsx, shared.tsx, schema.ts (zod), timeSlots.ts (recurrence expansion), DeleteSeriesSlotDialog.tsx
├── layouts/
│   ├── AppLayout.tsx       Header + <Outlet /> + Footer
│   └── ProtectedRoute.tsx  Redirects to Keycloak if not authenticated
├── pages/
│   ├── HomePage.tsx                    Landing page: hero search, LatestOpportunitiesSection preview, org CTA, founder band, FAQ
│   ├── OpportunitiesPage.tsx           /opportunities - the browse/search route that owns VolunteerOpportunitiesList
│   ├── app/OrgDashboardPage/           Customizable widget dashboard - org app shell's landing tab. `widgetCatalog.ts` holds the roster, each tile's default/minimum footprint and `DEFAULT_LAYOUT`; `WidgetCard.tsx` is the shared shell (condensed uppercase title, optional single footer affordance, vertical scroll fades). A widget is handed a `WidgetSize` of BOTH a width class and a height class - a one-row tile is a `strip` and has room for a number, not a list, so tiles that carry rows fall back to a count there (see `ToDoWidget`). Widget keys are persisted (`DashboardWidgetKey` in the backend) and in saved layouts: retitle and redesign a tile in place rather than renaming its key, and drop a retired key from the catalog so `sanitizeWidgetKey` degrades old layouts gracefully
│   ├── administration/                 Platform-admin area - AdministrationPage.tsx is the shell (sub-nav rail plus
│                                       <Outlet />), and each of the four tabs is its own module and its own lazy
│                                       chunk: AdminOrganizationsPage.tsx (list/verify organizations),
│                                       AdminUsersPage.tsx (toggle admin/enabled status), AdminReportsPage.tsx
│                                       (moderation queue), AdminAuditLogPage.tsx. They share nothing but
│                                       pageSize.ts
│   ├── PrivacyPolicyPage.tsx           Privacy policy (static)
│   └── ImprintPage.tsx                 Legal notice (static)
├── styles/global.css       Tailwind directives + custom brand theme
├── main.tsx                Entry point: AuthProvider + BrowserRouter + App, gated by ConfigGate (refuses to render the app on invalid runtime config, see components/ConfigGate.tsx) and joined by PwaUpdatePrompt (SW update prompt, components/PwaUpdatePrompt.tsx)
├── App.tsx                 React Router route declarations
└── vite-env.d.ts           ImportMetaEnv types for VITE_ variables
```

## Auth Flow

```
User clicks "Anmelden"
→ auth.signinRedirect() (react-oidc-context)
→ Keycloak login (PKCE, handled by oidc-client-ts)
→ Redirect back to /callback
→ AuthProvider processes code exchange, fires onSigninCallback (strips params)
→ auth.isAuthenticated = true, auth.user.access_token available
→ ProtectedRoute renders children
```

- `auth.user?.profile` - decoded id_token claims (sub, email, name, preferred_username, roles)
- `auth.user?.access_token` - Bearer token for API calls
- `active-org` cookie - last-opened organization id (set by `OrgAppLayout` on every successful org load; read by `HomePage`'s org-app resolution to skip straight to the right dashboard, see `lib/activeOrg.ts`)
- Roles: `auth.user?.profile?.roles` - flat string array from Keycloak custom mapper

## API Client

`src/client/generated/` is generated from `backend/src/Api/wwwroot/openapi-v1.json` by `@hey-api/openapi-ts` (`openapi-ts.config.ts`). Never edit it manually - changes will be overwritten.

It is **not** regenerated by the backend build, unlike the OpenAPI document and the integration tests' C# client. After any endpoint or DTO change run `pnpm client:generate` and commit the result; `pnpm check:client-current` regenerates into a scratch directory and fails CI when the committed output differs.

`client:generate` is two steps. `scripts/normalize-openapi.js` runs first and narrows the `["integer", "string"]` unions ASP.NET emits for every numeric property - 112 of them - down to the numeric type, then the generator reads that. Those unions describe `JsonNumberHandling.AllowReadingFromString` (the server *accepts* `"42"` on the way in) and say nothing true about a response, and generating them verbatim leaves every numeric read in the app choosing between a cast and threading a union through its arithmetic. The script explains the reasoning and fails loudly if it ever finds nothing to narrow.

Use the `useApiClient()` hook in components. It returns the client with the current bearer token in place, and every operation takes one options object:

```ts
const api = useApiClient();
const details = await api.getVolunteerOpportunityDetails({ path: { opportunityId } });
const page = await api.getVolunteerOpportunities({ query: { pageNumber: 1, pageSize: 10 } });
await api.createInvitation({ path: { organizationId }, body: { email } });
```

Two things that are deliberately not what the generator ships by default, both in `src/client/index.ts`:

- **The body comes back directly.** The generated SDK resolves to `{ data, request, response }`; `index.ts` unwraps it, so `await api.getUserProfile({})` is the profile. Nothing in the app reads the `Request` or `Response` back - everything that looks at one already happens in `api-instance.ts`'s `fetch`.
- **A failure rejects with the parsed `ProblemDetails`**, carrying `status`, `errorCode`, `detail` and `errors`. That is the shape every `catch` and every helper in `lib/apiError.ts` reads.

`date-time` fields come back as `Date` objects (a generated transformer, configured in `openapi-ts.config.ts`). Query parameters that take a date want an ISO string.

For endpoints with many optional query params, a named-options wrapper is still worth writing - see `lib/volunteerOpportunities.ts`'s `fetchVolunteerOpportunities`.

## Server State

Server data lives in a TanStack Query cache, not in a component's `useState`. `src/queryClient.ts` holds the defaults and argues each of them; `src/lib/queryKeys.ts` is the only place a query key is spelled, so a mutation and the queries it invalidates cannot drift apart. ADR-9 has the reasoning.

```tsx
const api = useApiClient();
const { data, error, isPending } = useQuery({
	queryKey: queryKeys.organizations.detail(organizationId),
	queryFn: ({ signal }) => api.getOrganizationDetails({ path: { organizationId }, signal }),
});
```

Three things worth knowing before writing one:

- **Do not poll by hand.** `refetchInterval` already stops while the tab is hidden, which is what the `setInterval` + `visibilitychange` + `AbortController` scaffolding in `useAccountMenu` and `useAchievementNotifier` used to do - twice, identically.
- **`hooks/useCachedFetch.ts` is for the call sites that predate the cache.** It returns the `[data, setData, error]` tuple `useSharedOrgFetch` did, backed by the query cache. New code calls `useQuery` directly.
- **`hooks/useLoadMore.ts` still hand-rolls pagination** (page, items, has-more, out-of-order guard). It has not moved to `useInfiniteQuery` yet; when it does, its fifteen call sites move with it.

Tests do not change shape: they mock `hooks/useApiClient` as before, and `src/test/render.tsx` wraps every render in a fresh `QueryClient` with retries, focus refetching and cross-test cache sharing turned off.

## Environment Variables

Dev values are defined in `.env.development` - see that file for the current list. Exposed client-side via Vite (must use `VITE_` prefix) and accessed via `import.meta.env.VITE_*`.

## Role Checks

Roles come from `auth.user?.profile?.roles` - a flat string array put there by a custom Keycloak mapper, and typed `unknown` by `oidc-client-ts`. Don't unwrap it at the call site; `lib/authRoles.ts` does it defensively in one place.

```tsx
const roles = useAuthRoles();
const isOrganisator = roles.has("organisator");
```

Outside a component (or where the `User` is already in hand), use `hasRole(claim, role)` / `readRoles(claim)` from `lib/authRoles.ts` directly - `layouts/ProtectedRoute.tsx` is the example.

Known roles: `user`, `organisator`, `admin` (`KNOWN_ROLES`, and the `KnownRole` type the helpers take, so a typo is a compile error).

A role check here decides what the UI offers, never what the API allows: every endpoint re-checks its own authorization policy (see `backend/AGENTS.md`). Treating it as a security boundary is the mistake to avoid.

## Routing

Routes declared in `src/App.tsx`. Add new routes there, following the file's existing pattern: lazy-load the page component (`const MyPage = lazy(() => import("./pages/MyPage"))`) and declare the route (`<Route path="/my-page" element={<MyPage />} />`, wrapped in `<ProtectedRoute>` if it requires login) - see the file's own top-of-file comments for why pages are lazy-loaded (per-route build chunks, PWA precache size, Vite's `INEFFECTIVE_DYNAMIC_IMPORT` warning) and which pages are the deliberate exceptions.

**Note:** During development, new page code may use `(api as any)` until the OpenAPI document is rebuilt and `pnpm client:generate` regenerates the client with the new operation (see API Client above).

## Scripts

```bash
pnpm dev             # dev server on :4321
pnpm build           # build to dist/ (static files)
pnpm preview         # preview production build
pnpm check           # tsc --noEmit
pnpm test            # vitest run - the fast local loop; CI runs test:coverage instead
pnpm test:watch      # vitest in watch mode, for local development
pnpm test:coverage   # vitest run --coverage - CI hard gate (frontend-checks.yml's test job), enforces vitest.config.ts's thresholds
pnpm mutation        # StrykerJS over src/lib - report-only, minutes, never a gate
pnpm mutation:since  # only files changed since origin/main
pnpm mutation:components  # StrykerJS over the design-system primitives (stryker.components.json)
pnpm lint            # eslint, zero warnings allowed
pnpm format:write    # apply Prettier formatting - run before every commit
pnpm format:check    # check Prettier formatting (used by CI)
pnpm i18n:check      # verify en.json/de.json key parity - CI hard gate, run before committing locale changes
pnpm size            # size-limit against .size-limit.cjs - CI hard gate, needs a `pnpm build` first
pnpm check:deps      # import cycles, orphans, unresolvable relative imports (scripts/check-module-graph.js)
```

The `check:*` scripts are all plain node scripts under `scripts/`, all wired
into `frontend-checks.yml`'s `lint` job, and all report the offending file by
name. A deliberate choice over reaching for a tool per question: the repo pins
`engines.node` to whatever is current and every workflow reads it via
`node-version-file`, so a tool that follows the node.js release cycle cannot
run here on an odd-numbered line - which is what happened to
`dependency-cruiser` and is recorded in ADR-11.

## Unit Tests

Vitest (`vitest.config.ts`, jsdom environment) covers three things, colocated next to what they test:

- **Pure logic in `src/lib/`**, as `*.test.ts` (e.g. `src/lib/activeOrg.test.ts`).
- **Component and page behaviour**, as `*.test.tsx` next to the component or page (e.g. `src/pages/app/OrgSettingsPage.test.tsx`) - rendered copy, form state, which branch a component takes for a given prop or a given rejected promise.
- **Component accessibility**, as `*.a11y.test.tsx` (e.g. `src/components/ConfirmDialog.a11y.test.tsx`) - see "Accessibility (a11y)" below.

What stays in the Playwright suite in `backend/tests/VisualTests/` is what a browser is genuinely needed for: rendered layout (jsdom has no layout engine, so anything measuring a width, a computed colour or a bounding box cannot move), real keyboard and pointer input, real focus movement, a real Keycloak round trip, real offline, page-level axe scans, and multi-page journeys that are not any one component's output. #2148 moved everything else down; `backend/AGENTS.md`'s Visual tests section owns the current counts, and `docs/TDRs/2_slow_ci_pipeline.adoc` has the per-category breakdown of what is left and why.

**Vitest Browser Mode: evaluated, not adopted (2026-09).** It would close the two gaps jsdom structurally cannot cover - `color-contrast`, which needs a layout engine and computed styles, and real focus movement - by running the component suite in a real browser instead. The reason not to is not the feature; it is where this project's pain already is. `docs/TDRs/2_slow_ci_pipeline.adoc` records `VisualTests` growing from 117 to 542 cases, a median backend gate of 21 min 58 s and a 53 % failure rate before it was sharded, and #2148 moved 328 cases *out* of the browser in response. Browser Mode would add a third tier that needs its own Chromium download in the one frontend job that currently finishes in about a minute, to assert things the Playwright suite already asserts - `AccessibilityTests.cs` runs page-level axe scans with contrast enabled, and `CalendarToolbarContrastTests.cs` exists for exactly the contrast case jsdom drops. Revisit if `VisualTests` is ever migrated off Aspire, at which point Browser Mode stops being a third tier and becomes a cheaper second one.

Two porting hazards are worth knowing before moving anything else down, both of which produced green-but-vacuous tests during #2148: **Playwright matches accessible names by substring, RTL matches them exactly** (so a `getByRole("button", { name: "Remove" })` that worked end-to-end matches nothing here when the real name is an `aria-label` reading "Remove Vera Volunteer"), and **an `alt=""` image has no `img` role** (query it by tag, and say why in a comment).

`src/test/` holds the shared harness for the component suites and is not itself a test target:

| File | What it gives you |
| ---- | ----------------- |
| `render.tsx` | `renderWithProviders(ui, { lng, route, auth })` - wraps the tree in the three providers every non-trivial component assumes (i18n, router, auth). Defaults to English, `/`, signed out |
| `i18n.ts` | A synchronous i18n instance preloaded from the shipped `en.json`/`de.json`. The app's own `src/i18n.ts` loads locales through a *dynamic import*, so a component rendered in the same tick still shows raw keys - and an accessible name of "opportunities.signUp" is not the name a user gets |
| `a11y.ts` | `expectNoA11yViolations(target?)` - see below |
| `apiMock.ts` | `createApiMock()` - a Proxy standing in for the generated `ApiClient`, where every endpoint resolves to a `vi.fn()` on first access. A page pulls from five or more endpoints across its own effects and its children's; listing each by hand makes a test fail on a call it never cared about every time an unrelated fetch is added |
| `setup.ts` | Registers jest-dom matchers and Testing Library cleanup (this config has `globals: false`, so RTL's own auto-cleanup never registers), plus the jsdom stubs axe needs |

Conventions used across the existing suite:

- Mock a module's own dependencies with `vi.mock(...)`, not the module under test itself. Values referenced inside a `vi.mock` factory must go through `vi.hoisted(...)` (mock factories are hoisted above imports, so a plain top-level `const` isn't initialized yet when the factory runs).
- Prefer computing the expected value with the same underlying call (e.g. `new Date(iso).toLocaleString(...)`) over hardcoding a formatted string, when the result depends on the host timezone or locale data.
- For module-level singletons/config computed at import time (e.g. `lib/runtimeConfig.ts`, `lib/keycloakRegistration.ts`), call `vi.resetModules()` in `beforeEach` and re-`import()` the module inside each test to get a fresh instance.
- `vi.spyOn` on an already-spied method (e.g. `console.error` spied in a previous test) returns the *same* mock and keeps its call history - restore with `vi.restoreAllMocks()` in `afterEach` rather than only resetting the fake in `beforeEach`.

## Coverage

`pnpm test:coverage` is the CI gate (`frontend-checks.yml`'s `test` job) and enforces the per-directory thresholds in `vitest.config.ts`. They are a ratchet on what is covered today - each number sits about two points under the measurement it was taken from - not a target anyone should be chasing. The slack is deliberate: at zero margin the gate goes red on a rounding difference, and the first thing anyone does then is delete it.

Per-directory rather than one global number on purpose: `lib/` is pure logic and cheap to cover (94 %), while `components/` (71 %) carries the ones that structurally cannot be covered here. `SingleMarkerMap` needs a layout engine and `ImageCropModal` needs a canvas; both have `backend/tests/VisualTests` cases, and a jsdom test for either would assert that a mock was called. One average would hide both facts.

Raise a threshold when a change raises coverage. Lowering one is a decision, and it belongs in the diff that needs it with a reason.

## Mutation Testing

`pnpm mutation` runs StrykerJS over `src/lib/**` (and `pnpm mutation:components` over the design-system primitives, see below) and reports a mutation score - how many deliberate defects the suite actually catches, which is the question line coverage cannot answer. Report-only, never a gate (`thresholds.break: 0`), same posture as the backend's Stryker.NET (#2147). CI equivalent: `mutation-tests.yml`'s `mutation-tests-frontend` job, `workflow_dispatch` only.

- **Baseline: 80.95 %** (897 generated / 840 scored / 680 killed / 126 survived / 34 no-coverage / 57 ignored). Recorded on #2148's head, and confirmed by CI [run `32479000847`](https://github.com/maik-hasler/afunto/actions/runs/32479000847) at the same commit. Comparable only against another StrykerJS 10.0.0 run of the same `mutate` scope - a version bump or a scope change makes the number a different number.
- **Runtime: 28 min 59 s on a GitHub runner**, 36 min 24 s on a contended 4-core sandbox. Either way it is the slow leg - the two .NET legs are 4 min 23 s and 7 min 34 s - which is why the component tier stays on demand.
- **`concurrency: 2` is reproducible here, unlike on the .NET side.** Two runs of this config on very different hardware (a 4-core sandbox and a clean `ubuntu-latest`) produced 80.95 % with a bit-for-bit identical per-file table. That matters because `backend/tests/Application.UnitTests/stryker-config.json` pins `concurrency: 1` precisely *because* Stryker.NET's preview MTP runner invents kills at 2 (#2147). Do not copy that constraint over here by analogy - it is a different runner with a different bug, and n=2 agreement is the evidence for leaving this at 2.
- **Scoped to `src/lib/**` deliberately.** All of `src/**` was 182 files / 14 851 mutants at #2148's head, against a suite that renders components in jsdom - a full pass is hours, where the backend's 2 690 mutants over pure functions take ~12 minutes. `src/lib/**` (37 files, 897 mutants at that commit) is the tier that is cheap to mutate and comparable to the backend's.
- **Score one component on demand:** `pnpm mutation --mutate "src/components/OpportunityCard.tsx"`. That is the useful granularity - a single component's survivors point at specific unasserted branches, where a whole-app average points at nothing. Measured: `OpportunityCard.tsx` 39.68 %, `NotificationItem.tsx` 57.69 %, `Footer.tsx` 4.76 %.
- **`pnpm mutation:components` (`stryker.components.json`) covers the design-system primitives** - `Button`, `Chip`, the three banners, `EmptyState`, `RouteState`, `Skeleton`, `RequiredMark`. A separate config rather than an addition to the default `mutate` scope, for the reason in the bullet above this one: the default run is already the slow leg at ~29 minutes, and a primitive's survivors are read one file at a time anyway. Everything else about the two configs is identical, so their numbers are comparable.
- **Read the survivors, not the number.** Only 5 of `OpportunityCard`'s 76 survivors sat on a `className` line - the rest were real branches the tests never exercise (`capacity.isFull`, the `unlimited` case, the `FEW_SPOTS_THRESHOLD` tone). The score is a pointer to those, not a target to raise.
- **`plugins` in `stryker.config.json` must name `@stryker-mutator/vitest-runner`.** pnpm's strict `node_modules` stops Stryker discovering it by convention; without it the error reads "no TestRunner plugins were loaded", which looks like a missing install.
- **`ignoreStatic: true` changes the denominator, and buys less than Stryker's warning implies.** A static mutant sits in module-level code, so killing it needs a full test-file reload rather than a re-run of the covering tests; Stryker warns that 111 of `src/lib`'s 897 mutants (12 %) are static and estimates them at 69 % of the run. Two things measured against that estimate: the flag actually excluded **57** mutants, not 111, and the run still took 36 minutes. The pass without the flag was killed at 35 minutes before finishing, so the saving is real but unquantified - what is certain is that it does not make the run cheap. Keep the flag (module-scope initialisation is excluded from the score as a result), and do not read the 69 % as headroom anyone can reclaim.
- `reports/` and `.stryker-tmp/` are gitignored.

## Key Dependencies

| Package              | Purpose                              |
| -------------------- | ------------------------------------ |
| `react-router` v8    | Client-side routing                  |
| `react-oidc-context` | Keycloak OIDC (wraps oidc-client-ts) |
| `oidc-client-ts`     | PKCE flow, token management          |
| `@tailwindcss/vite`  | Tailwind CSS 4 via Vite              |
| `leaflet`            | Map renderer (OpenStreetMap tiles)   |
| `react-leaflet`      | React bindings for Leaflet           |

## Linting

The CI `lint` job runs `format:check` and will fail if any Prettier violations exist - causing `build` to be skipped and a follow-up fix commit.

```bash
pnpm lint
```

Rules enabled: `@typescript-eslint/strict`, `react-hooks/rules-of-hooks`, `react-hooks/exhaustive-deps`, `jsx-a11y/recommended`, `tailwindcss/classnames-order`, `tailwindcss/no-contradicting-classname`, `tailwindcss/no-unnecessary-arbitrary-value`, `tailwindcss/enforces-negative-arbitrary-values`.

- No non-null assertions (`!`). Use `as Type` or type narrowing (`if (!x) return`).
- If `api` is intentionally excluded from `exhaustive-deps`, suppress with `// eslint-disable-next-line react-hooks/exhaustive-deps` and keep it consistent with the existing pattern.
- `prettier-plugin-tailwindcss` sorts `className` values automatically as part of `pnpm format:write` - don't hand-order classes. `eslint-plugin-tailwindcss` (`tailwindcss/*` rules above) is the CI-enforced backstop for anything that slips through unformatted, plus `no-contradicting-classname` (e.g. `mt-2 mt-4` on the same element) and `no-unnecessary-arbitrary-value`/`enforces-negative-arbitrary-values` (flags e.g. `w-[480px]` where the equivalent scale utility `w-120` exists). Both plugins resolve the theme from `src/styles/global.css` (`.prettierrc.json`'s `tailwindStylesheet`, `eslint.config.js`'s `settings.tailwindcss.cssConfigPath`), so custom tokens like `brand-700` are recognized.

## Design System

Shared UI primitives live in `src/components/` and `src/lib/` (no separate design-system package). Reuse these instead of re-deriving their markup/classes - each exists because the same visual pattern had drifted into 2-4 slightly different hand-rolled versions across pages (see the rationale comments in `Button.tsx` and `ErrorBanner.tsx` for two examples); the Tailwind lint rules above only catch class-order and arbitrary-value drift, not this kind of duplication.

| Primitive     | File                          | Use for                                                                                                                               |
| ------------- | ------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------- |
| `Button`      | `components/Button.tsx`      | Every clickable action, button or link - `variant` (`primary`/`secondary`), `size` (`sm`/`md`/`lg`), `fullWidth`                     |
| `formClasses` | `lib/formClasses.ts`         | `inputClass`, `textareaClass`, `labelClass` for every form control, plus `checkboxClass`/`radioClass` for every checkbox/radio - a native checkbox or radio takes its checked fill from `accent-color` and nothing else, so `text-brand-*` on one is inert and it paints the browser's default blue (#2329). `formClasses.test.ts` scans the sources and fails on any checkbox, or non-`sr-only` radio, that does not use the matching class. Also `heroSearchInputClass` - the pill-shaped, borderless search field inside a translucent hero band (landing page, `/opportunities`, `/organizations`) |
| `DatePicker`/`DateTimePicker` | `components/DatePicker.tsx`/`components/DateTimePicker.tsx` | Every date/date-time field - a calendar-grid popover (same keyboard model as `VolunteerOpportunitiesList/MiniCalendar.tsx`: arrow keys/Home/End/PageUp/PageDown, roving tabindex) instead of a native `<input type="date">`/`type="datetime-local">`, whose own calendar popup is unstyled OS/browser chrome outside the design system's control. Value shape matches the native input it replaces exactly ("yyyy-MM-dd" / "yyyy-MM-ddTHH:mm"), so callers keep whatever date-math they already had. `DateTimePicker` pairs the grid with a native `<input type="time">` for the minute - that stepper isn't the chrome being replaced |
| `TwoColumnPageLayout` | `components/TwoColumnPageLayout.tsx` | The sidebar+content grid a "page with a rail" route uses (legal docs' `DocumentOutline`, Administration/MyEngagements/ProfileOverview's sub-nav rail) - two `variant`s (`documentOutline`/`subNav`) hold the two widths/gaps that were previously hand-copied into five page files |
| `RequiredMark` | `components/RequiredMark.tsx` | The product's only required-field marker - an aria-hidden `*` appended by the component, never written into a translation string, plus the `RequiredFieldsLegend` that explains it (one per form). The control still needs its own `required`/`aria-required`: that is the accessible half |
| `ErrorBanner` | `components/ErrorBanner.tsx` | Inline "action failed" message - the one boxed style every page's error state should share                                          |
| `SuccessBanner` | `components/SuccessBanner.tsx` | Inline "action succeeded" message - the `ErrorBanner` twin, same box style in green                                                |
| `WarningBanner` | `components/WarningBanner.tsx` | Inline advisory that isn't a failure (e.g. approaching a churn/rate limit) - the `ErrorBanner` twin, same box style in amber, `role="status"` instead of `"alert"` |
| `EmptyState`  | `components/EmptyState.tsx`  | "Nothing here yet" placeholder with an optional CTA button                                                                           |
| `RouteState`  | `components/RouteState.tsx`  | The four ways a route can fail to show what was asked for - `variant` (`notFound`/`forbidden`/`offline`/`error`), each with its own glyph, tone and allowed actions. `inline` for a state replacing one section of a page rather than the whole route. Retry is honoured for `error` only, by design (see #1774) |
| `Skeleton`    | `components/Skeleton.tsx`    | Loading placeholders (`animate-pulse` block)                                                                                         |
| `Modal`       | `components/Modal.tsx`       | Every dialog - backdrop-button a11y pattern (see Accessibility below), focus trap, Escape-to-close, portals out of `inert` ancestors |
| `OrgAvatar`   | `components/OrgAvatar.tsx`   | Every organization avatar (logo image, or an initials fallback) - the one avatar treatment for orgs, used by the header nav, `/organizations`, Administration, an org's profile view and `OpportunityCard`. `size` (`sm`/`md`/`lg`/`xl`/`2xl`/`3xl`) covers every pixel size those surfaces need (#2244) |
| `lockScroll`  | `lib/scrollLock.ts`          | Locking the page behind any blocking overlay (dialog, mobile menu) for as long as it is open - reference-counted, so nested dialogs work. Targets the **root** element, not `body`: `global.css`'s `html { overflow-x: clip }` stops a body-level lock from ever reaching the viewport (#1787) |
| `inlineLinkClass` | `lib/linkClasses.ts`     | Every link sitting inside a block of prose - the one recipe that keeps a rest-state underline, so the link is not distinguished from the text around it by colour alone (WCAG 1.4.1; the brand green against body grey measures 1.37:1). Five static pages had each re-declared the identical string, and the copies that had not - "Contact the organization" on `/my-signups` - were what axe reported as `link-in-text-block` (#2327). Not for standalone links that already read as buttons or nav items |
| `surfaceClasses` | `lib/surfaceClasses.ts`   | `cardClass`, `cardSubtleClass` for every bordered content panel (list items, result cards, widget panels, info panels) - not media cards whose padding lives on an inner wrapper around an edge-to-edge image/map |
| `opportunityCapacity` | `lib/opportunityCapacity.ts` | Every surface stating how full an opportunity is - `getOpportunityCapacity` resolves `VolunteerOpportunitySummary.totalMaxParticipants`'s tri-state (`null` unlimited / `0` no time slots / `> 0` capped) into an explicit state, `getCapacityFromTimeSlots` does the same from the detail page's per-slot rows - hand it only the slots a visitor can still book (an ended slot's seats can never be taken) and note that it counts bookings from each row's `bookedCount`, never from the viewer-relative `currentParticipantCount`, which the API strips the caller's own engagements out of (#2318) - `FEW_SPOTS_THRESHOLD` is the shared scarcity cut-off. Wording for the organizer-facing count is `formatSignUpCount` in `lib/format.ts`. Don't re-derive the mapping inline: three surfaces each did, each handled only two of the three cases, and all three silently rendered nothing for the third (#1777) |

**Tokens:** the brand color scale (`brand-50`...`brand-900`) and `accent-400` are defined once in `src/styles/global.css`'s `@theme` block - use them instead of ad hoc hex values or arbitrary colors. `brand-700` is the single rendered "brand green" - it's also what `public/logo.svg`, `public/favicon.svg` and `index.html`/`vite.config.ts`'s `theme_color` are pinned to, so a control's rendered color, the installed app's chrome color and the logo agree (#2244). `accent-400` has exactly one job: decoration (glow blobs, background texture) - never a call-to-action surface or anything semantically "warning"/"pending" (that's Tailwind's built-in `amber-*` scale, e.g. `WarningBanner`, `Chip`'s `warning` variant). `rounded-xl` is the default corner radius for interactive surfaces (buttons, inputs); cards, panels and modals use the separate `rounded-card` (16px) token instead - see `surfaceClasses.ts` and `Modal.tsx`. `rounded-md` is reserved for `Skeleton` loading blocks.

## Date Formatting

One numeric convention (`27.08.2026, 09:00`), plus one documented relative
exception - no other date register (#2047). All helpers live in
`lib/format.ts` and take `lng` as `i18n.language` ("de"/"en"), not an Intl
locale (see `resolveDateLocale`):

| Helper                | Use for                                                                                                             |
| --------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `formatDateTime`      | Any single point-in-time timestamp ("27.08.2026, 09:00") - the default                                             |
| `formatDateTimeRange` | Any start/end range (time slots, calendar events) - collapses a same-day range to one date with a hyphen-joined time range ("27.08.2026, 09:00-17:00") instead of repeating the date on both ends; falls back to two full `formatDateTime`s joined by " - " once the range crosses a calendar day. Always use this for ranges instead of hand-joining two `formatDateTime` calls - that's the exact duplication #2047 was filed about |
| `formatDate`          | Date-only, no time-of-day (deadlines like `ValidUntil`, "created on"/"sent on" timestamps)                          |
| `formatPostedAgo`     | The one relative-date exception ("Vor 5 Tagen veroeffentlicht") - pair the relative text with the absolute `formatDateTime` in an `sr-only` sibling span, so the exact timestamp reaches everyone; `title` alongside it is a harmless bonus for mouse users (see `VolunteerOpportunityDetailPage.tsx`'s posted-on span). **Not** an `aria-label` on the wrapping span, which is what this used to say: `aria-label` is prohibited on a role-less element, so the absolute date was silently dropped - and nothing catches it, since jsx-a11y has no rule for it and axe reports `aria-prohibited-attr` as inconclusive once the element has text of its own |
| `formatFullDate`      | Screen-reader-only accessible names for calendar day cells whose visible label is a bare number (`MiniCalendar`, `CalendarWidget`) - not a visible register, so it doesn't compete with the above |

There is no long-form date helper (spelled-out month, e.g. "13. August
2026") - it was retired as a third competing register; use `formatDate`
even for lower-frequency "created on" timestamps.

## Copy Conventions

- **"Zeitslot" vs "Termin"** (opportunity-detail page, `/my-signups`): both
  words describe the same `TimeSlot` entity, but the split is a deliberate
  register shift, not drift (#1920). "Zeitslot" (`opportunities.availableTimeSlots`,
  `opportunities.joinWaitlist`, etc.) is used while a slot is still one of
  several open options to choose from - an inventory listing. Once the
  volunteer has committed to one, both the opportunity-detail page and
  `/my-signups` switch to "Termin" (`myEngagements.scheduledFor`) - the
  volunteer's own confirmed appointment. Keep this pairing when touching
  either surface; don't rename one into the other to "fix" the apparent
  inconsistency.
- **"Anmelden" vs "eintragen"** (German copy, #2237): "Anmelden"/"Anmeldung"
  is reserved for authentication - signing in, signing out, the Keycloak
  login flow - and nothing else. Signing up to participate in a
  `ScheduledSlots` Einsatz uses "eintragen" instead (`opportunities.joinWaitlist`
  "Für Zeitslot eintragen", `signUp.submitWaitlist` "Verbindlich eintragen",
  `opportunities.loginPrompt` "Trag dich ein, um mitzumachen."), and the
  resulting record is an "Eintragung", not an "Anmeldung", through the
  confirmation dialog, toasts, notifications and transactional emails
  (`backend/src/Infrastructure/Email/Templates/de.json`). This does not
  apply to `IndividualContact` opportunities, which already use their own
  unambiguous "Interesse bekunden" / "Interessenbekundung" wording (#2228) -
  don't fold that into "eintragen". Adding a new German string that says
  "sich anmelden" for anything other than logging in reintroduces the exact
  ambiguity #2237 fixed.

## Accessibility (a11y)

`eslint-plugin-jsx-a11y` is enabled with the recommended ruleset. All violations are errors - CI will fail on any a11y lint issue.

Key conventions:

- **Modal dialogs**: Use the backdrop-button pattern. Separate the clickable backdrop (`<button aria-hidden="true" tabIndex={-1}>`) from the dialog container (`<div role="dialog" aria-modal="true" aria-labelledby="...">`) inside a neutral wrapper div. Handle Escape via `useEffect` on `document`.
- **Clickable cards**: Use a stretched `<Link className="absolute inset-0">` inside a `relative` `<li>` rather than putting `onClick` on the `<li>` directly. Any secondary links inside the card get `relative z-10` to sit above the stretched link.
- **Dropdown panels are disclosures, not menus or listboxes** - a trigger with `aria-expanded` opening a `<ul aria-label>` of plain `<button>`s, marking the active one with `aria-current`. That is what `RowActionsMenu.tsx`, `OrganizationSwitcher.tsx` and `Header/LanguageSelector.tsx` all do. Don't reach for `role="menu"`/`"menuitem"` or `role="listbox"`/`"option"` (or the matching `aria-haspopup`) unless you actually implement that pattern's keyboard model - arrow keys, Home/End, and either roving tabindex or `aria-activedescendant`. Claiming the role without the keys tells a screen-reader user to press arrows and gives them nothing, and putting `role="option"` on an `<li>` that *wraps* a `<button>` is an axe `nested-interactive` violation (serious) on top (#1772). `Dropdown.tsx` is the one real listbox in this repo - use it when you need to pick a form value; `LocationSearchInput.tsx` is the one real combobox.
- **Images**: All `<img>` tags need an `alt` attribute. Purely decorative images use `alt=""`.
- **SVG icons**: Decorative SVGs get `aria-hidden="true"`. Meaningful standalone SVGs need a `<title>` or `aria-label`.
- **Form labels**: Every form control must have an associated `<label htmlFor="...">` or `aria-label`.
- **Color contrast (text)**: `text-gray-400` (2.6:1 on white) fails the WCAG AA 4.5:1 floor for text - reserve it for decorative icons and input placeholders only, never for body copy, labels, timestamps, or other real content. Use `text-gray-500` (4.9:1) or darker for content; an interactive control's resting label needs to clear at least the 3:1 UI-component floor too.
- **Non-modal popovers close when focus leaves them**: `useDismissableOverlay` dismisses on outside click, Escape *and* `focusin` outside its container, which is what keeps a panel from floating over the control the user has tabbed to (WCAG 2.2 SC 2.4.11 Focus Not Obscured). It listens for `focusin`, never `focusout` - removing the focused node (picking an option) moves focus to `body` and fires `focusout` with nothing gaining focus, which would tear the overlay down mid-interaction. When the hook's container spans more than one popover - the opportunity filter bar wraps all six chips so that clicking a sibling chip is not an "outside" click - the individual popover needs its own focus-out close on top, see `FilterDropdown.tsx` (#2327).
- **Target size**: WCAG 2.5.8 sets a 24x24 CSS px floor for any pointer target, enforced by `backend/tests/VisualTests/TargetSizeTests.cs` - a `Chip` that is a link carries `min-h-6`/`min-w-6` for exactly this reason. Header and mobile-drawer controls are held to the more comfortable 44x44 instead (`Button`'s `md` size, the drawer's `min-h-11` rows, the burger, the mobile notification bell, the language switcher, the org switcher, the logo link) - they are a phone's primary navigation (#2327).
- **A visually hidden control still needs a visible focus ring**: an `sr-only` `input[type=file]` takes keyboard focus while its own ring is drawn around a 1x1 clipped box, so focus disappears for a tab stop. `global.css` projects the ring onto the paired `<label>` (`label:has(+ .sr-only:focus-visible)`, plus the wrapping-label variant) - keep new file pickers to that label-plus-`sr-only`-input shape and they inherit it (#2327).
- **Color contrast (non-text)**: WCAG 1.4.11 sets the same 3:1 floor for meaningful icons and interactive-control borders (outline-button/chip borders, checkbox/radio boundaries) - axe-core's `color-contrast` check doesn't evaluate this, so it slips past CI silently (#2048). `-200`/`-300` border tokens and `brand-400`/`brand-500` icon fills all measure under 3:1 against a white or brand-50 background - use `border-gray-500`+, `border-red-500`+, `border-brand-600`+ for borders, and `text-brand-600`/`text-brand-700` for functional icons instead. See `Button.tsx`'s `outline`/`dangerOutline` variants and `FilterDropdown.tsx`'s active/inactive icon colors for the reference values.

Automated axe-core checks run at two altitudes (#2148). Both apply the same gate - fail on any "serious" or "critical" violation, plus a short list of escalated "moderate" rules - so the split is about scope, not about standard:

| | Where | Covers | Run by |
| - | ----- | ------ | ------ |
| **Component** | `src/**/*.a11y.test.tsx`, via `expectNoA11yViolations` from `src/test/a11y.ts` | Roles, accessible names, labelling, ARIA validity, interactive nesting - per component and per component *state* (open/closed, loading, empty, error, validation-rejected) | `pnpm test`, seconds, no browser |
| **Page** | `backend/tests/VisualTests/AccessibilityTests.cs` | Landmark structure, heading order, **colour contrast**, real focus movement, tab order - one scan per distinct layout and palette | `dotnet.yml`'s `visual-tests` shards, minutes, real Chromium + Aspire |

Two consequences worth knowing before adding a test:

- **jsdom has no layout engine and no canvas**, so axe can only ever report `color-contrast` as *inconclusive* there. It is disabled outright in `src/test/a11y.ts` rather than left to look like a passing check. The page scans are the only place contrast is genuinely evaluated - which is why the contrast rules above are stated as conventions here rather than left to CI to discover.
- **Page-scope rules cannot be judged from a fragment** (a component has no `<main>`, no page `<h1>`, no `lang`), so they are disabled in the component harness too and stay the page scans' job.

So: a new component or component state gets a colocated `*.a11y.test.tsx`; a new page/route additionally gets a scan in `AccessibilityTests.cs`. `a11y-check` flags a missing one of either. `src/test/a11y.test.tsx` is the harness's own negative control - it asserts the scanner can still *fail*, and is the first place to look if the a11y suite goes green through a change that should have broken it.

## Released image

Static files in `dist/` served by nginx. `nginx.conf.template` handles SPA routing via `try_files $uri /index.html`; `docker-entrypoint.d/99-runtime-config.sh` renders it (and `config.js`) into their final form at container start via `envsubst`, filling in the Content-Security-Policy's `connect-src`/`frame-src` origins from the same `VITE_API_URL`/`VITE_KEYCLOAK_AUTHORITY_URL` env vars used for runtime config, plus `img-src`'s object storage origin from a separate `STORAGE_PUBLIC_URL` env var (matching the backend's `Storage__PublicEndpoint` - uploaded org logos/opportunity banners/avatars are served from there, not the API origin), so one image works across environments. `img-src` also allows the `blob:` scheme unconditionally (no env var needed) since avatar/org-logo/opportunity-banner pickers preview the selected file via `URL.createObjectURL()` before it's ever uploaded. The CSP string itself is defined once via an nginx `map` (`$csp_header`) and referenced from all four location blocks - `frontend/scripts/check-nginx-csp.js` (`pnpm check:nginx-csp`, wired into CI) guards against a location block silently falling out of sync with that definition.

**Important:** CORS must be configured on the backend to allow the frontend origin, since API calls are now cross-origin (no server-side proxy).
