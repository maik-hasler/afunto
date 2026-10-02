<div align="center">

![Afunto](frontend/public/og-image.png)

**Volunteer coordination platform matching helpers with regional needs.**

*Volunteer spontaneously. Find your local cause.*

[Architecture Docs](https://maik-hasler.github.io/afunto/Architecture.html) - [Report Bug](https://github.com/maik-hasler/afunto/issues/new/choose) - [Request Feature](https://github.com/maik-hasler/afunto/issues/new/choose)

[![Backend CI](https://img.shields.io/github/actions/workflow/status/maik-hasler/afunto/dotnet.yml?label=backend%20CI)](https://github.com/maik-hasler/afunto/actions/workflows/dotnet.yml)
[![Frontend CI](https://img.shields.io/github/actions/workflow/status/maik-hasler/afunto/frontend.yml?label=frontend%20CI)](https://github.com/maik-hasler/afunto/actions/workflows/frontend.yml)
[![Docs](https://img.shields.io/github/actions/workflow/status/maik-hasler/afunto/docs.yml?label=docs)](https://github.com/maik-hasler/afunto/actions/workflows/docs.yml)
[![License: AGPL v3](https://img.shields.io/badge/license-AGPL--3.0-blue)](LICENSE)

</div>

---

## Table of Contents

- [About](#about)
- [Features](#features)
- [Tech Stack](#tech-stack)
- [Architecture](#architecture)
- [Getting Started](#getting-started)
- [Running the Released Images](#running-the-released-images)
- [Project Structure](#project-structure)
- [Contributing](#contributing)
- [Security](#security)
- [Versioning & Releases](#versioning--releases)
- [License](#license)

---

## About

Volunteering doesn't have to mean a long-term commitment - sometimes an afternoon is enough, sometimes a week. The hard part is usually finding out where help is needed right now, whether that's a large NGO or a local sports tournament. Existing platforms tend to be too complex, too generic, or simply unused.

Afunto makes concrete needs visible: what, where, when. Organizations post opportunities with real time slots, and volunteers sign up for exactly the slot that fits their schedule - not every week, just now and then. That is where the name comes from: "af un to" is Low German for "now and then".

The app itself is served in German by default, since Afunto's primary audience is German-speaking, with English available as a secondary UI language via a language selector. Code, commits, and documentation for contributors stay in English throughout.

---

## Features

- **Browse and filter opportunities** across a list view, a mini calendar, and city/location autocomplete.
- **Sign up for a specific time slot ("engagement")** - withdraw if plans change, reactivate later within limits, get reminders, and check in on the day.
- **Rate your engagement afterwards** with a rating and comment, editable for a short window after submission.
- **Organizations post opportunities** with scheduled time slots (occurrences) and defined participation types.
- **Organizer dashboard** with a customizable widget layout: a review queue that confirms or declines sign-ups without leaving the board, the next occurrences and how full each one is, a calendar, and opt-in tiles for volunteer numbers, the team and quick check-in.
- **Organization management** covering membership, roles, a public directory of organizations, and multi-organization support with an org switcher.
- **Platform administration** to manage users and toggle admin/enabled status.
- **Notifications and a language selector** (German by default, English as a secondary language).
- **Keycloak-backed authentication** via OIDC/PKCE, with Keycloak Organizations powering org membership.
- **Achievements and badges** awarded for volunteering milestones, shown on the profile.
- **Organization invitations** to join and manage an organization's membership.
- **Reporting and moderation** for opportunities, organizations, and users, backed by a full admin audit log.
- **Image uploads** for avatars, organization logos, and opportunity banners, stored in S3-compatible object storage (RustFS).
- **Installable as a PWA** with offline support for previously visited pages.

---

## Tech Stack

| Layer | Tech |
|---|---|
| Backend | .NET 10, Clean Architecture (Api -> Application -> Domain, Infrastructure -> Domain), EF Core 10, PostgreSQL 18, CQRS-style command/query handlers, transactional outbox for domain events |
| Auth | Keycloak 26.7.4 (OIDC, JWT, Keycloak Organizations) |
| Frontend | Vite SPA, React 19, React Router v8, Tailwind CSS 4, react-oidc-context, Leaflet/react-leaflet |
| API client | TypeScript client generated from the backend OpenAPI spec by `@hey-api/openapi-ts`, and a C# one for the integration tests by NSwag - neither is hand-edited |
| Object storage | RustFS, S3-compatible (avatars, organization logos, opportunity banners) |
| Tests | TUnit + Aspire.Hosting.Testing + Respawn + NetArchTest (Application.UnitTests, IntegrationTests, ArchitectureTests), Vitest (frontend pure-logic units), Playwright + axe-core (E2E and accessibility, `backend/tests/VisualTests`) |
| CI | GitHub Actions (build and test on every PR, Docker images to GHCR on tag push) |
| Dependency updates | Renovate |

---

## Architecture

The full system architecture is documented in arc42 format and published at:

**[maik-hasler.github.io/afunto/Architecture.html](https://maik-hasler.github.io/afunto/Architecture.html)**

It is built from the AsciiDoc sources in `docs/Architecture/` via AsciiDoctor and republished to GitHub Pages on every push to `main` (`docs.yml`). Individual Architecture Decision Records live alongside it in `docs/ADRs/`.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/) - exact version pinned in `backend/global.json` (10.0.401)
- [Docker](https://www.docker.com/) - Aspire runs PostgreSQL and Keycloak as containers
- [pnpm](https://pnpm.io/) - frontend package manager

### Run everything with one command

```bash
dotnet run --project backend/src/Aspire/AppHost
```

The Aspire AppHost provisions PostgreSQL, Keycloak, the backend API, and the Vite frontend, then prints every service URL in the Aspire dashboard. On first start, databases are created automatically and the Keycloak realm is imported.

### Services

| Service | URL | Credentials |
|---|---|---|
| Frontend | http://localhost:4321 | - |
| Backend API | *dynamic port - see the Aspire dashboard* | - |
| Keycloak | http://localhost:8080 | - (no master-realm admin console login is bootstrapped locally; sign in to the app itself with a test user below) |
| pgAdmin | http://localhost:5050 | - (opens straight to the dashboard, no login prompt) |
| PostgreSQL | localhost:5432 | postgres / *auto-generated per run - see the `postgres` resource in the Aspire dashboard* |
| Mailpit | http://localhost:1080 | - (no auth, captures outgoing email) |

### Test users

These accounts are **enabled in the released Keycloak image** and these passwords are public, a deliberate decision for the maintainer's own deployment (#2308). Anyone who can reach a running instance of that image can sign in as `admin`. If you deploy this image yourself, disable or remove them first - see [`keycloak/AGENTS.md`](keycloak/AGENTS.md)'s Test Users section - and see [`keycloak/README.md`](keycloak/README.md) for creating a real administrator.

| Username | Password | Roles | Persona | Can |
|---|---|---|---|---|
| vera | vera123 | user | Volunteer Vera | Browse volunteer opportunities |
| olaf | olaf123 | user, organisator | Organizer Olaf | Browse and create opportunities |
| admin | admin123 | admin | Administrator | Full administration |

---

## Running the Released Images

Three images are published to GHCR on every tagged release (see [Versioning & Releases](#versioning--releases)): `afunto-backend`, `afunto-frontend`, and `afunto-keycloak`. Each is configured entirely through environment variables at container start - none of them need a rebuild to point at a different environment. The Keycloak image's own variables are already documented in [`keycloak/README.md`](keycloak/README.md); this section covers the other two.

### Backing services

Bring your own:

- **PostgreSQL** (18, or compatible) - the backend needs its own database, separate from Keycloak's own (see `keycloak/README.md`)
- **An S3-compatible object store** (e.g. RustFS) - organization logos, user avatars, and opportunity banners
- **An SMTP relay** - outgoing notification and reminder email
- **Keycloak** - the `afunto-keycloak` image, or any Keycloak instance importing the same realm

The backend and frontend both also make outbound HTTPS calls to OpenStreetMap Nominatim for geocoding - no configuration needed, but it must be reachable from wherever you run them.

**Important:** leave `ASPNETCORE_ENVIRONMENT` unset (defaults to `Production`) or set it to `Production` explicitly. `Development` skips the required-configuration check below entirely and unconditionally runs the same seeding that `Database__SeedOnStartup` gates outside Development - a no-op once a database already has data, but not on a fresh one (see the `Database__SeedOnStartup` row further down for exactly what that seeds).

### Backend (`ghcr.io/<owner>/afunto-backend`)

Listens on port `8080` (plain HTTP - put a TLS-terminating reverse proxy in front of it; port `8081` is also exposed by the base image but unused unless you configure a certificate yourself).

Array-valued settings (`Cors__Origins`, `Authentication__ValidIssuers`, `TrustedNetworks__Cidrs`) use ASP.NET Core's indexed environment-variable convention, not a comma-separated value - `Cors__Origins__0=https://app.example.com`, `Cors__Origins__1=https://admin.example.com`, and so on.

Required variables crash the container at startup outside Development (`RequiredConfigurationValidator`); everything else ships with a working default and the container starts even if left unset. A few "No" rows are still needed for a specific feature to actually work (Keycloak connectivity, outbound email, file uploads) - left unset, the container starts fine and that feature fails at first use instead, rather than at boot (#2207).

| Variable | Required | Purpose | Example |
|---|---|---|---|
| `ConnectionStrings__afunto` | Yes | PostgreSQL connection string for the application database | `Host=db;Database=afunto;Username=afunto;Password=secret` |
| `Keycloak__ClientSecret` | Yes | Secret for the confidential `backend` service-account client - must match the Keycloak realm's resolved `KEYCLOAK_BACKEND_SECRET` (`keycloak/README.md`) | - |
| `Authentication__Authority` | Yes | OIDC authority the backend validates access tokens against | `https://login.example.com/realms/afunto` |
| `Cors__Origins__0` | Yes | Allowed CORS origin - the frontend's own origin; add `__1`, `__2`, ... for more | `https://app.example.com` |
| `Keycloak__BaseUrl` | No | Base URL the backend calls Keycloak's API at | `https://login.example.com` |
| `Keycloak__Realm` | No | Keycloak realm name | `afunto` |
| `Keycloak__ClientId` | No | Keycloak client id for the backend's service account | `backend` |
| `Authentication__ValidIssuers__0` | No | Acceptable JWT issuer(s) - needed in practice even though not startup-validated | `https://login.example.com/realms/afunto` |
| `Smtp__Host` | No | SMTP relay hostname | `smtp.example.com` |
| `Smtp__Port` | No | SMTP port | `587` |
| `Smtp__FromAddress` | No | From-address for outgoing email | `noreply@example.com` |
| `Smtp__FromName` | No | From-name for outgoing email | `Afunto` |
| `Smtp__Username` | No | SMTP auth username | - |
| `Smtp__Password` | No | SMTP auth password | - |
| `Smtp__EnableSsl` | No | Use STARTTLS when connecting to the relay | `true` |
| `Storage__Endpoint` | No | S3-compatible endpoint the backend writes to | `http://storage:9000` |
| `Storage__AccessKey` | No | Access key for the bucket-scoped service account | - |
| `Storage__SecretKey` | No | Secret key for the bucket-scoped service account | - |
| `Storage__BucketName` | No | Bucket for avatars, logos, and opportunity banners | `afunto` |
| `Storage__PublicEndpoint` | No | Public origin uploaded files are served from, if different from `Storage__Endpoint` (e.g. an internal vs. a public hostname) - must match the frontend's `STORAGE_PUBLIC_URL` below | `https://storage.example.com` |
| `Api__PublicBaseUrl` | No | Reserved for the backend's own public base URL - bound at startup but not currently read by any request path | `https://api.example.com` |
| `TrustedNetworks__Cidrs__0` | No | CIDR(s) trusted to set `X-Forwarded-For` - the reverse proxy in front of this image; defaults cover loopback and RFC1918 private ranges | `10.0.0.0/8` |
| `Database__MigrateOnStartup` | No | Apply pending EF Core migrations automatically when the container starts | `true` |
| `Database__SeedOnStartup` | No | **Never enable outside development.** Seeds demo data - ten fake volunteer opportunities at real Leipzig addresses onto the live public browse page, two fake organizations in your production Keycloak realm, and an organizer role grant to a hardcoded placeholder account (#2211) | `false` |
| `NotificationRetention__ReadRetentionDays` | No | Days a read notification is kept before cleanup | `90` |
| `NotificationRetention__UnreadRetentionDays` | No | Days an unread notification is kept | `180` |
| `NotificationRetention__RetentionCheckIntervalHours` | No | How often the notification-retention job runs | `24` |
| `AbuseReportRetention__RetentionDaysAfterTargetDeleted` | No | Days an abuse report is kept after its target is deleted | `180` |
| `AbuseReportRetention__RetentionCheckIntervalHours` | No | How often the abuse-report retention job runs | `24` |
| `RateLimiting__Read__AuthenticatedPermitLimit` | No | Read requests per window for authenticated users | `200` |
| `RateLimiting__Read__AnonymousPermitLimit` | No | Read requests per window for anonymous users | `60` |
| `RateLimiting__Read__WindowSeconds` | No | Read rate-limit window | `60` |
| `RateLimiting__Write__PermitLimit` | No | Write requests per window | `100` |
| `RateLimiting__Write__WindowSeconds` | No | Write rate-limit window | `60` |
| `Outbox__BatchSize` | No | Domain events processed per outbox poll | `20` |
| `Outbox__PollIntervalSeconds` | No | Outbox poll interval | `5` |
| `EngagementReminder__MaxBatchSize` | No | Max engagement reminders sent per poll | `500` |
| `EngagementReminder__PollIntervalHours` | No | Engagement reminder poll interval | `1` |
| `OutputCaching__LongPublicReadSeconds` | No | Cache duration for long-lived public reads | `3600` |
| `OutputCaching__ShortPublicReadSeconds` | No | Cache duration for short-lived public reads | `60` |

### Frontend (`ghcr.io/<owner>/afunto-frontend`)

Static SPA assets served by nginx on port `80` (plain HTTP - put a TLS-terminating reverse proxy in front of it, same as the backend). Unlike a typical Vite app, the variables below are read at container start, not only baked in at build time: `docker-entrypoint.d/99-runtime-config.sh` substitutes the `VITE_`/`OPERATOR_` ones into `config.js` (read by the app at runtime as `window.__APP_CONFIG__`), derives the Content-Security-Policy's allowed origins from the same origin values, and substitutes `BACKEND_UPSTREAM`/`DNS_RESOLVER` into the nginx config itself - so one built image runs anywhere.

| Variable | Required | Purpose | Example |
|---|---|---|---|
| `VITE_API_URL` | Yes | Backend API origin the SPA calls; also the CSP's `connect-src` origin | `https://api.example.com` |
| `VITE_KEYCLOAK_AUTHORITY_URL` | Yes | Keycloak realm issuer URL for the OIDC login flow; also the CSP's `connect-src`/`frame-src` origin | `https://login.example.com/realms/afunto` |
| `VITE_KEYCLOAK_CLIENT_ID` | No | Public OIDC client id registered in Keycloak for the SPA | `frontend` |
| `STORAGE_PUBLIC_URL` | No | Public origin uploaded avatars/logos/banners are served from - the CSP's `img-src` origin; must match the backend's `Storage__PublicEndpoint` above (or `Storage__Endpoint` if that isn't set) | `https://storage.example.com` |
| `OPERATOR_NAME` | No | This deployment's legally responsible party (DDG §5 imprint, GDPR Art. 13 controller) | `Musterverein Rettungsdienst e.V.` |
| `OPERATOR_ADDRESS` | No | The same party's postal address | `Musterstraße 1, 12345 Musterstadt, Germany` |
| `OPERATOR_EMAIL` | No | Contact address shown on the imprint, privacy policy and contact pages | `legal@example.com` |
| `OPERATOR_SITE_URL` | No | This deployment's own public URL, shown on the imprint and privacy policy | `https://example.com` |
| `BACKEND_UPSTREAM` | No | Backend origin `/sitemap.xml` and the social-crawler meta routes proxy to - only needed if your backend isn't reachable as `http://backend:8080` on the frontend container's network | `http://backend:8080` |
| `DNS_RESOLVER` | No | DNS resolver used to re-resolve `BACKEND_UPSTREAM` on every request - only needed outside a Docker user-defined network (whose embedded resolver is `127.0.0.11`) | `127.0.0.11` |

`OPERATOR_NAME`/`OPERATOR_ADDRESS`/`OPERATOR_EMAIL`/`OPERATOR_SITE_URL` are all-or-nothing: the image ships with none of them set, and the imprint and privacy policy show a visible "operator not configured" notice instead of anyone else's details until all four are provided.

CORS must be configured on the backend (`Cors__Origins` above) to allow this image's own origin - API calls are cross-origin, there is no server-side proxy.

---

## Project Structure

```
afunto/
├── backend/        .NET 10 Clean Architecture API
├── frontend/       Vite SPA + React 19 + Tailwind CSS 4
├── keycloak/       Custom Keycloak image + realm config
├── docs/           arc42 architecture docs + ADRs
└── .github/        CI workflows + issue templates
```

---

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for Conventional Commits, branch naming (`feat/`, `fix/`, `docs/`, `chore/`), the PR process, and code style. Please also read the [Code of Conduct](CODE_OF_CONDUCT.md).

---

## Security

Found a vulnerability? See [SECURITY.md](SECURITY.md) for how to report it privately.

---

## Versioning & Releases

Afunto uses a single unified SemVer tag across the whole monorepo, published to GHCR - see [VERSIONING.md](VERSIONING.md) for the tag format and release process. Release notes are auto-generated on [GitHub Releases](https://github.com/maik-hasler/afunto/releases).

---

## License

Afunto is intentionally licensed under the [GNU Affero General Public License v3.0](LICENSE).

It doesn't matter who develops the project further. The benefit to society comes first. No profit, no closed source, no lost knowledge.

Practically, this means: if you self-host a modified version of Afunto and let others interact with it over a network, you are obligated to make your modified source available to them too.

---

<div align="center">

Built for the moments when an afternoon of help is exactly enough.

</div>
