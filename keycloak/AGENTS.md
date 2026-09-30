# Keycloak - Identity & Access Management

## Overview

Custom Keycloak 26.7.4 image with the `afunto` realm pre-baked. Built and published to GHCR via the `publish-keycloak` job in `.github/workflows/publish.yml`; `.github/workflows/keycloak-realm-import.yml` guards that the committed realm still imports cleanly on that Keycloak version before the image is ever built.

```
keycloak/
├── Dockerfile              Multi-stage build (builder + optimized runtime)
├── README.md               Runtime env vars documentation
├── realms/
│   └── afunto-realm.json   Realm config - source of truth for auth setup
└── themes/afunto/login/    Custom login theme (see "Login Theme" below)
```

## Login Theme

**Directory:** `themes/afunto/login/`, selected by the realm's `loginTheme`. `parent=base`, so any template not overridden here falls through to Keycloak's own - which is how the pages a real signup walks through ended up rendering stock markup inside this theme's card (#1758).

The theme now overrides every template a visitor can reach with this realm's settings:

| Template | Reached by |
|---|---|
| `login.ftl` | sign-in |
| `register.ftl` | `/registrations` |
| `login-reset-password.ftl` | "forgot password" |
| `login-verify-email.ftl` | every registration (`verifyEmail: true`) |
| `login-update-password.ftl` | after email confirmation, and from the reset mail |
| `login-update-profile.ftl` | UPDATE_PROFILE required action |
| `terms.ftl` | TERMS_AND_CONDITIONS required action (not enabled today) |
| `info.ftl` / `error.ftl` | action-token outcomes, expired links |
| `login-page-expired.ftl` | an idle login form |
| `logout-confirm.ftl` | RP-initiated logout without an `id_token_hint` |

**There is no password field on the registration form, and that is Keycloak's design, not a bug.** With `verifyEmail: true`, `RegistrationPassword.buildPage` deliberately omits it and sets UPDATE_PASSWORD once the address is confirmed instead. `register.ftl` renders the password fields whenever `passwordRequired` *is* set, so turning `verifyEmail` off restores them - and `KeycloakThemeTests.Register_OmitsPasswordFields_AndSaysWhy` fails, which is the correct signal that the explanatory lead needs to go too.

Two constraints that are easy to trip over:

- **The `frontend` client has no `baseUrl`.** Base's `error.ftl`, `info.ftl` and `logout-confirm.ftl` all gate their only way out on `${client.baseUrl}`, so all three rendered with nothing to click. The overrides fall back to `properties.siteUrl` (declared in `theme.properties`) instead. Keep that fallback on any new template with an exit.
- **Keycloak's stock German is "Sie"; the product is "du".** Any base message that reaches a user has to be overridden in `messages/messages_de.properties` (and its English twin) or the funnel mixes both registers on one screen.

Colors, radii, shadows and the control recipes are mirrored from the frontend's `@theme` block and `lib/formClasses.ts` / `lib/surfaceClasses.ts` - change them there first, then here. `resources/img/logo.svg` and `favicon.svg` are byte-identical copies of `frontend/public/`; re-copy rather than hand-editing.

Covered by `backend/tests/VisualTests/KeycloakThemeTests.cs`, which drives Keycloak's origin directly and creates throwaway users to reach the required-action pages (`AspireFixture.CreateThrowawayUserAsync`). Not covered, and deliberately: the TOTP/WebAuthn/identity-provider/consent templates, none of which this realm can reach. They fall back to base markup, over the class-hook mappings in `theme.properties` and the fallback rules at the end of `afunto.css`, so they degrade to plain rather than to unstyled.

## Realm Configuration

**File:** `realms/afunto-realm.json`  
Imported on container startup. This file IS the auth configuration - edit here, not in the Keycloak UI (UI changes don't persist across container restarts in dev).

### Roles (realm-level)

| Role | Purpose |
|---|---|
| `user` | Default - can browse opportunities |
| `organisator` | Can create and manage volunteer opportunities |
| `admin` | Full admin access - composite role, includes `user` + `organisator` so admin tokens also satisfy `AfuntoDefaultUserPolicy`/`AfuntoOrganisatorPolicy` |

The realm's `defaultRole` (`default-roles-afunto`, composite over `user`) is what Keycloak grants automatically to every newly created user, including self-registrations through the public `/protocol/openid-connect/registrations` form (#1723 - without it, self-registered accounts got no realm role at all and every `AfuntoDefaultUserPolicy`-gated endpoint 403'd for them). Deliberately does not also compose Keycloak's built-in `offline_access`/`uma_authorization`/account-client roles the way a full UI export would - this realm's import is a hand-authored partial file, and those built-ins are not yet provisioned at the point a partial import resolves `roles.realm` composites, so referencing them there fails `--import-realm` outright (confirmed via `keycloak-realm-import.yml` against the pinned Keycloak version: `Unable to find composite realm role: uma_authorization`). None of the app's clients request the `offline_access`/`uma_authorization` scopes anyway, so nothing is lost by leaving them out. It does **not** apply to the three seeded test users above or `service-account-backend` - those are created via this file's `users` array during realm import, which bypasses Keycloak's normal user-creation code path and only grants the `realmRoles`/`clientRoles` listed explicitly on each entry.

### Clients

**`frontend`** (public OIDC client)
- Authorization Code + PKCE (S256 enforced) flow only
- ROPC disabled (`directAccessGrantsEnabled: false`) - use `frontend-test` for integration tests
- Redirect URIs / web origins / post-logout redirect URIs: the committed realm carries the `${KC_FRONTEND_URL}` placeholder, resolved from an env var of that name at container start, and nothing else - the same file ships baked into the released Keycloak image (`Dockerfile`), so a `http://localhost:*` entry here would be live wherever that image runs (#1190). `AppHost.cs` overlays `http://localhost:*` (and `webOrigins: ["*"]`, since Aspire's dynamic port can't be matched by a fixed origin) back in for local Aspire/Playwright runs only - see its comment above the realm-patching block
- Protocol mappers:
  - `realm-roles` - injects `roles: [...]` into id_token, access_token, userinfo
  - `realm-name` - injects hardcoded claim `realm: "afunto"` (used by backend auth policies)
  - `backend-audience` - adds `backend` client to audience in access tokens

**`frontend-test`** (public OIDC client, integration tests only)
- `enabled: false` in the committed realm - it ships in the same realm baked into the released image (#1167: a public client with ROPC enabled there turns credential stuffing into a single scriptable `grant_type=password` request, no browser, no PKCE, no redirect-URI constraint). `backend/src/Aspire/AppHost/AppHost.cs` flips it back to `enabled: true` in the dev-only realm copy it writes before import, since that's the only path that ever needs it live - see below
- ROPC enabled (`directAccessGrantsEnabled: true`) - used by `IntegrationTestFixture.GetAccessTokenAsync` and `VisualTests/AspireFixture.SignInAsync`, both of which boot Keycloak through the Aspire AppHost, never the baked image
- Redirect URIs: `http://localhost:*` only (never the released image, which ships this client disabled)
- Same protocol mappers as `frontend` (roles, realm-name, backend-audience)

**`backend`** (confidential service account)
- Client secret: `backend-secret` for local Aspire dev/tests (`AppHost.cs` overwrites whatever is in the realm JSON before import, so the checked-in value is irrelevant there). In the committed realm the `secret` is the placeholder `${KEYCLOAK_BACKEND_SECRET}` - Keycloak's realm import resolves `${VAR}` placeholders in any JSON value from an env var of the same name at container startup - so whoever runs the image supplies the real value, rather than it being a committed literal that would be usable the moment it lands in git history
- No user login flows - server-to-server only
- Service account permissions: `view-realm`, `manage-users`, `manage-organizations` - deliberately not `manage-realm` (full realm-admin), which the backend never needs and would let a leaked secret reconfigure clients, auth flows, and other realm settings
- Used by `KeycloakOrganizationService` in the backend to manage org membership

### Test Users

See `README.md`'s Test users table for the username/password/role list. These credentials are stored **pre-hashed** (PBKDF2-SHA256) in the realm file, not as plaintext `value`. The realm's `passwordPolicy` (`upperCase(1)`, `length(8)`) rejects these short dev passwords, and Keycloak validates plaintext credentials against the policy during `--import-realm` - a fresh import (CI, a clean local stack, a first run) crashes with `invalidPasswordMinUpperCaseCharsMessage` and never starts. Pre-hashed credentials skip that validation, so **do not** replace them with plaintext `value` fields. To rotate one: import the realm, set the password in the UI, then partial-export the user.

These accounts ship **`"enabled": true`** in the realm file baked into the published Keycloak image, and their passwords are public (`README.md`'s Test users table). That is a deliberate repo-owner decision, not drift: they shipped `enabled: false` from #2284 as the safety default #2197 established (an enabled `admin` with a published password in a released image is a live takeover, no exploitation required), and #2308 re-enabled them for the owner's own deployment, in full knowledge that the passwords are published. **Do not flip them back to `false`** - that breaks that deployment - and do not remove them from the realm file, without checking with the repo owner first. Treat anyone who can reach a running instance of this image as able to sign in as `admin`.

The pre-hashed credentials above are what let these short dev passwords survive `--import-realm`, independent of the `enabled` flag; the realm-import CI job proves it on every `keycloak/**` change. `AppHost.cs` still flips all three to `enabled: true` in the dev-only realm copy it writes before import - a no-op for these three since #2308, kept so local dev (#1166) and `IntegrationTests`/`VisualTests` stay correct whatever the committed flag says. `frontend-test` above is the client that genuinely does ship disabled (#1167); do not confuse the two.

### Events (authentication + admin audit trail)

`eventsEnabled`, `adminEventsEnabled` and `adminEventsDetailsEnabled` are on (#2217) - without them, `bruteForceProtected` blocks credential-stuffing attempts but leaves no queryable record of who logged in, from where, or when, nor of admin-console actions (password resets, role grants, user edits) - which turns a GDPR Art. 33 breach assessment (72 hours to establish scope) into guesswork. `enabledEventTypes` is a deliberate allowlist rather than "log everything": the login/session/account lifecycle events reachable through this realm's flows (`LOGIN`, `LOGOUT`, `REGISTER`, `RESET_PASSWORD`, `UPDATE_PASSWORD`, `UPDATE_PROFILE`, `UPDATE_EMAIL`, `VERIFY_EMAIL`, `DELETE_ACCOUNT`, plus each one's `_ERROR` counterpart), not the high-volume OAuth plumbing events (`REFRESH_TOKEN`, `CODE_TO_TOKEN`, `USER_INFO_REQUEST`, ...) that fire on every token refresh and would drown the trail in noise no breach assessment needs. `eventsExpiration` (15552000s = 180 days) is a deliberately chosen retention period, not a default - it applies to both the user-event and admin-event stores, since Keycloak's `RealmRepresentation` has no separate `adminEventsExpiration` field. That period is also stated in the privacy policy the frontend ships (`frontend/src/locales/{en,de}.json`'s `privacyPolicy.section7Body2`) - changing it here means updating that copy too. `eventsListeners` keeps the `jboss-logging` default so events also reach the container's stdout logs, in addition to being queryable via the Admin Console's Events tab / Admin REST API. `adminEventsDetailsEnabled` is safe to leave on: Keycloak strips credential/secret fields (`StripSecretsUtils`) from the admin event's logged representation before persisting it, so a password reset performed through the Admin Console doesn't land in the event store in plaintext - the one upstream gap this doesn't cover is regenerating a *client's* secret via the Admin Console's "Regenerate" button (keycloak/keycloak#31892), which isn't part of this realm's normal operation (the `backend` client's secret is supplied via `${KEYCLOAK_BACKEND_SECRET}` at import time, never regenerated through the UI).

### Organizations Feature

Keycloak organizations are enabled (`"organizationsEnabled": true`). The backend delegates all org membership management to Keycloak - organizations are **not** duplicated in the application database.

## Docker Image

Multi-stage Dockerfile:
1. **Builder stage**: `quay.io/keycloak/keycloak:26.7.4` - runs `kc.sh build` with PostgreSQL provider
2. **Runtime stage**: Copies optimized build, runs with `--optimized` flag

See `README.md` for the required runtime environment variables.

The Aspire AppHost (`backend/src/Aspire/AppHost/AppHost.cs`) launches Keycloak with `KC_DB=dev-file` for local dev - Keycloak owns its own embedded H2 store there. The shared Postgres container hosts only the application `afunto` database.

## Updating the Realm

The committed JSON stays the source of truth; the UI is only a convenient
editor for producing a valid export, which is why step 3 writes it back.

1. Make changes in the running Keycloak UI at http://localhost:8080
2. Export the realm: Admin UI → Realm Settings → Action → Partial Export (include clients, groups, roles)
3. Replace `realms/afunto-realm.json` with the export
4. Restart the Keycloak container to verify the import works

## Release Tagging

A single repo-level tag (`vX.Y.Z`, optionally `-rc.N`) releases every component
at once, this image included - there is no per-component tag. See `VERSIONING.md`
for the tag format and `.github/AGENTS.md`'s publish workflow section.
