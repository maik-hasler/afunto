# Backend - .NET 10 Clean Architecture API

## Layer Overview

```
Api → Application → Domain
          ↓
    Infrastructure → Domain
```

Enforced by `tests/ArchitectureTests/`. Breaking layer deps = failing CI.

## Directory Structure

```
src/
├── Api/                        Endpoints, auth config, program entry
│   ├── Program.cs              DI wiring, JWT auth, CORS, OpenAPI, migrations on startup
│   ├── Common/
│   │   ├── Authentication/AuthorizationPolicies.cs   Policy name constants + role constants
│   │   └── Endpoints/IEndpoint.cs + EndpointExtensions.cs   Auto-discovery pattern
│   ├── Organizations/          Feature folders: {Feature}/{UseCase}/v1/
│   └── VolunteerOpportunities/
│
├── Application/                Business logic only - no EF Core, no HTTP
│   ├── ServiceCollectionExtensions.cs   Reflection-based handler registration
│   ├── Common/
│   │   ├── Messaging/          ISender, ICommand<T>, IQuery<T>, IPipelineBehavior<T,R>, IPublisher, INotificationHandler<T>
│   │   ├── Persistence/        IApplicationDbContext, IUnitOfWork, IAggregateRepository<T,TId>
│   │   ├── Keycloak/           IKeycloakOrganizationService
│   │   ├── Pagination/         PagedList<T>
│   │   ├── Exceptions/         ResultFailureException, ResultExtensions (GetValueOrThrow/ThrowIfFailure)
│   │   └── PipelineBehaviors/  TransactionPipelineBehavior, PerformancePipelineBehavior
│   ├── Organizations/
│   └── VolunteerOpportunities/
│
├── Domain/                     Zero external dependencies
│   ├── Primitives/             AggregateRoot<TId>, Entity<TId>, DomainEvent, Result/Error/ErrorType, IValueObject, INotification
│   ├── Common/                 Address (shared value object, used by both Organization and VolunteerOpportunity)
│   ├── Organizations/          Organization (aggregate), OrganizationId (value object)
│   ├── VolunteerOpportunities/ VolunteerOpportunity (aggregate), Occurrence, ParticipationType, IPinGenerator
│   ├── Engagements/            Engagement (aggregate), EngagementStatus
│   └── Users/                  UserId (Keycloak user reference)
│
└── Infrastructure/             Implements Application interfaces
    ├── ServiceCollectionExtensions.cs   EF Core, Keycloak HTTP client, repositories
    ├── DomainEventDispatcher.cs         IDomainEventDispatcher -> IPublisher (see "Domain events" below)
    ├── BackgroundJobs/                  IHostedService jobs, incl. OutboxProcessorJob (domain event dispatch)
    ├── Persistence/
    │   ├── ApplicationDbContext.cs       EF Core DbContext + IUnitOfWork
    │   ├── Configurations/               Fluent API entity mappings
    │   ├── Interceptors/                 AuditableEntityInterceptor (created_on/modified_on), ConvertDomainEventsToOutboxMessagesInterceptor
    │   ├── Outbox/                       OutboxMessage (transactional outbox row for domain events)
    │   ├── Repositories/                 AggregateRepository<T,TId>, read repositories
    │   └── Migrations/                   EF Core migrations
    └── Keycloak/                         KeycloakOrganizationService (HttpClient wrapper)

tests/
├── Application.UnitTests/      Handler tests, NSubstitute mocks, no DB
├── IntegrationTests/           Aspire.Hosting.Testing (the AppHost's own Postgres + Keycloak + RustFS + Mailpit), Respawn
├── ArchitectureTests/          NetArchTest layer rules + naming conventions
└── VisualTests/                TUnit.Playwright + Aspire, E2E and axe-core a11y - largest, slowest suite
```

## Adding a Feature (canonical pattern)

```
1. Domain (if domain logic involved)
   └── Domain/Organizations/Organization.cs  - add method

2. Application
   └── Application/Organizations/{UseCase}/v1/
       ├── {UseCase}Command.cs / {UseCase}Query.cs
       └── {UseCase}CommandHandler.cs / {UseCase}QueryHandler.cs

3. Api
   └── Api/Organizations/{UseCase}/v1/
       ├── {UseCase}Request.cs    - request body record (omit if no body)
       └── {UseCase}Endpoint.cs  - implements IEndpoint, maps route, calls ISender

4. The OpenAPI document regenerates on any `dotnet build` of Api (its own GenerateOpenApiDocuments
   target - not NSwag). The integration tests' C# client regenerates on any build of IntegrationTests,
   which references Api, so building that one project refreshes both.
   The frontend's TypeScript client does NOT come from that build - run
   `pnpm --dir frontend client:generate` as well (`pnpm check:client-current` fails CI otherwise)
```

Reference implementations (newest first): `Organizations/RemoveMember/`, `Organizations/GetOrganizationDetails/`.

## Key Patterns

### IEndpoint auto-discovery
Any class implementing `IEndpoint` is auto-registered via `Api/Common/Endpoints/EndpointExtensions.cs` (assembly scan into DI) and mapped under the versioned route group that `EndpointExtensions.MapEndpoints` builds once (`v{version:apiVersion}`) - individual endpoints map their own path relative to that group, they don't repeat the version prefix themselves. `Api/Organizations/RemoveMember/v1/RemoveMemberEndpoint.cs` is a real, current example: route mapping, auth policy, rate limiting, and CQRS dispatch all in one file.

### CQRS dispatch
`ISender.Send(request, cancellationToken)` (`Application/Common/Messaging/ISender.cs`) from the endpoint handler, e.g. `await sender.Send(command, cancellationToken)` in `RemoveMemberEndpoint.cs` above.

### Handler registration
Auto-scanned from Application assembly - no manual DI registration needed.  
Add a class implementing `ICommandHandler<,>` or `IQueryHandler<,>` and it's picked up.

### Error handling (Result pattern)
Domain and Application logic signals failure with `Result`/`Result<T>` (`Domain/Primitives/Result.cs`), not exceptions. A domain method that can fail returns `Result` (or `Result<T>` when it also produces a value) built from `Error.Validation/NotFound/Conflict/Forbidden(code, description)` - see `Domain/Engagements/Engagement.cs`'s `Confirm()`/`Cancel()` for real, current examples, including the `IsAnonymized` guard every mutating method on `Engagement` starts with.

Command/query handlers convert a `Result` to an exception at the Application boundary with `Application/Common/Exceptions/ResultExtensions.cs`:
- `result.ThrowIfFailure()` for a plain `Result`
- `result.GetValueOrThrow()` for a `Result<T>`, returning `T` on success

Both throw `ResultFailureException(Error)`, caught by `Api/Common/ExceptionHandlers/ResultFailureExceptionHandler.cs` and mapped to a `ProblemDetails` response (`Validation`->400, `NotFound`->404, `Conflict`->409, `Forbidden`->403) with `errorCode` + `traceId`. Only use this Application-boundary throw; don't invent a second convention for new endpoints, and don't thread `Result` all the way to the endpoint layer.

### Domain events
Aggregates raise events via `AddEvent(...)` (see `EngagementConfirmedDomainEvent` etc. in `Domain/Engagements/`). Dispatch is transactional-outbox based (#828), not direct:

1. `Infrastructure/Persistence/Interceptors/ConvertDomainEventsToOutboxMessagesInterceptor.cs` collects an aggregate's events during `SavingChangesAsync` (before the write) and converts each into an `OutboxMessage` row (`Infrastructure/Persistence/Outbox/OutboxMessage.cs`) added to the same `DbContext` - so it's part of the *same* DB transaction as the triggering command, all-or-nothing.
2. `Infrastructure/BackgroundJobs/OutboxProcessorJob.cs` polls for unprocessed `OutboxMessage` rows on its own timer, in its own DI scope/DbContext, well after the triggering command's transaction has committed. It deserializes each message back to a `DomainEvent` and calls `IDomainEventDispatcher.DispatchAsync`, which invokes `IPublisher`/`Publisher` -> the registered `INotificationHandler<T>`(s).

Because dispatch now happens in a fresh scope after commit (not inline inside the triggering `SaveChangesAsync`), an `INotificationHandler<T>` **can** safely call `ISender.Send(...)` or write through its own injected `IApplicationDbContext` - there's no outer open transaction to conflict with.

### Pipeline behaviors (run in this order)
1. `TransactionPipelineBehavior` - wraps commands in a DB transaction
2. `PerformancePipelineBehavior` - logs slow requests

### Authorization policies
| Policy constant | Role |
|---|---|
| `AfuntoAdminPolicy` | `admin` |
| `AfuntoDefaultUserPolicy` | `user` |
| `AfuntoOrganisatorPolicy` | `organisator` |

## Organization domain model

`Organization` aggregate fields: `Id`, `Name`, `Description?`, `ContactEmail?`, `ContactPhone?`, `Website?`, `Address?` (`Domain.Common.Address`), `CreatedOn`, `ModifiedOn`.

`IKeycloakOrganizationService` methods: `CreateOrganizationAsync`, `AddMemberAsync`, `RemoveMemberAsync`, `AssignOrganizerRoleAsync`, `GetMembersAsync`, `FindUserByExactMatchAsync`, `SearchUsersAsync`, `DeleteOrganizationAsync`. `FindUserByExactMatchAsync` is the only one safe to expose in an API response (exact email/username match, at most one result) - `SearchUsersAsync` is a realm-wide infix match, for internal filtering only (#2205). Which organizations a user organizes is answered from the local `organization_membership` table (`IApplicationDbContext.GetOrganizerOrganizationsAsync`), not Keycloak.

## Implemented endpoints (Organizations)

| Method | Route | Auth | Handler |
|---|---|---|---|
| GET | `/v1/organizations` | DefaultUser | `GetOrganizations` |
| POST | `/v1/organizations` | DefaultUser | `CreateOrganization` |
| GET | `/v1/organizations/{id}` | Organisator | `GetOrganizationDetails` |
| PUT | `/v1/organizations/{id}` | Organisator | `UpdateOrganization` |
| DELETE | `/v1/organizations/{id}/members/{userId}` | Organisator | `RemoveMember` |

## Database

- `UseSnakeCaseNamingConvention()`
- Migrations in `Infrastructure/Persistence/Migrations/`
- Add migration: `dotnet ef migrations add <Name> -p src/Infrastructure -s src/Api`
- Apply migrations: runs automatically on startup in Development; `dotnet ef database update` otherwise

## Testing

### Unit tests (`Application.UnitTests`)
- Mock with NSubstitute, assert with AwesomeAssertions
- Test handlers in isolation - no DB, no HTTP

### Integration tests (`IntegrationTests`)
- `IntegrationTestFixture` (IAsyncLifetime): spins up Postgres + Keycloak containers
- `Respawn` resets database state between tests (not between runs)
- `ApiClient.cs` is NSwag-generated by this project's own `GenerateApiClient` target - **do not hand-edit**
- Get auth tokens via `GetAccessTokenAsync(username, password)` in fixture

### Architecture tests (`ArchitectureTests`)
- `ArchitectureLayerTests.cs` - layer dependency rules
- `EndpointConventionTests.cs` - endpoint naming/structure rules
- `MessagingConventionTests.cs` - handler/command/query naming rules
- `ResponseDeclarationConventionTests.cs` - every endpoint declares the responses it can
  actually return. The committed OpenAPI document is the only input both generated clients
  are built from, so an undeclared response is invisible to every caller (#2158)
- Run these if you rename namespaces or move files between layers, or if you add an endpoint

### Visual tests (`VisualTests`)
- TUnit.Playwright + Aspire: boots the full stack (Postgres, Keycloak, backend API, frontend) and drives it through a real browser - E2E flows plus axe-core a11y checks
- Still the slowest suite, and the one to think twice before adding to: #2148 (plus #2162's reconciliation of its residue) moved 328 cases out of it into Vitest/RTL and `IntegrationTests`, and the 214 that remain are there because they genuinely need a browser (rendered layout, real input, a real Keycloak round trip, a page-level axe scan, or a multi-page journey that is not any one component's output). Before writing a new case here, check whether the assertion needs any of those - see `docs/TDRs/2_slow_ci_pipeline.adoc` for the per-case breakdown and the named list of critical journeys kept as real E2E. Sharded across four jobs in `dotnet.yml`
- `AccessibilityTests.cs` - the **page-level** axe-core gate: one scan per distinct layout/palette plus the handful of assertions that need a real browser (skip-link focus, the Leaflet marker's accessible name, tab order). Component-level scans live in `frontend/src/**/*.a11y.test.tsx` under `vitest-axe` (#2148) - add a case *here* for a new route, and a component suite *there* for a new component or component state. See the class summary for the full division of labour
- `AuthHelper.cs` - `LoginAsync` drives the real Keycloak login UI; `FastSignInAsync` seeds a minted token straight into `localStorage` to skip the redirect round trip for tests that only need an authenticated session as a precondition

### Mutation testing (`Stryker.NET`, manual)

Report-only, never a merge gate (#2147). Exists as the falsifiable guardrail for the E2E rebalance (#2148): record the score, move a wave of `VisualTests` cases down the pyramid, re-run - if the score held, the coverage that was removed was redundant. Line coverage cannot answer that question, because a deleted redundant test and a deleted load-bearing one look identical to it.

```bash
dotnet tool install --global dotnet-stryker --version 4.16.0
cd backend/tests/Application.UnitTests
dotnet-stryker --project Domain.csproj
dotnet-stryker --project Application.csproj
```

- **Run from `tests/Application.UnitTests/`, not from `backend/`.** Stryker picks up `stryker-config.json` from the working directory, and from `backend/` it finds `Afunto.slnx`, switches to solution mode, and tries to build the whole solution - which drags in `VisualTests` and its ~290 MiB Playwright browser download for a run that never opens a browser.
- **`test-runner: mtp` is mandatory** and is set in `stryker-config.json`. Stryker's default VSTest runner discovers zero tests against TUnit (Microsoft.Testing.Platform only, no VSTest adapter) and reports a vacuous pass. The MTP runner is still marked preview by Stryker and says so on every run.
- **`concurrency: 1` is load-bearing, not a performance setting.** That preview MTP runner miscounts above concurrency 1: 30-73 kills per run that concurrency 1 does not report, never the reverse, and a different subset each time. The score comes out ~5 points high and moves ~1 point between identical runs, which makes it useless as a before/after guardrail. At concurrency 1 two runs at the same commit are bit-for-bit identical. Do not raise it.
- **`Application.UnitTests.csproj` references `Domain.csproj` directly** purely so `--project Domain.csproj` resolves - Stryker only matches the project to mutate against the *direct* ProjectReference list. It is redundant to the compiler; see the comment in the csproj.
- Recorded baseline (`Domain` 66.75 %, `Application` 68.49 %, combined 67.89 % at commit `c4a143b`), wall-clock (~4 min + ~8 min), and how to read the number: `docs/TDRs/2_slow_ci_pipeline.adoc`. CI equivalent: `.github/workflows/mutation-tests.yml` (`workflow_dispatch` only).

### Run all tests
TUnit uses Microsoft.Testing.Platform, not the `dotnet test` new testing
experience - run each project directly, the same way CI does:
```bash
# from backend/
dotnet run --project tests/Application.UnitTests
dotnet run --project tests/ArchitectureTests
dotnet run --project tests/IntegrationTests
dotnet run --project tests/VisualTests
```

## NuGet Packages (key ones)

All versions centrally managed in `Directory.Packages.props`.

| Package | Used in |
|---|---|
| `Asp.Versioning.Http` | Api - URL-segment versioning |
| `Asp.Versioning.OpenApi` | Api - one OpenAPI document per API version |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | Api - JWT validation |
| `NSwag.MSBuild` | IntegrationTests - generates that project's C# API client from the OpenAPI document on build |
| `EFCore.NamingConventions` | Infrastructure - snake_case |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | Infrastructure - Postgres provider |
| `Aspire.Hosting.Testing` | IntegrationTests + VisualTests - boots the real AppHost (Postgres, Keycloak, RustFS, Mailpit, API) once per test session |
| `Respawn` | IntegrationTests - DB reset |
| `NetArchTest.Rules` | ArchitectureTests |
| `NSubstitute` | Application.UnitTests |
| `AwesomeAssertions` | All test projects |
