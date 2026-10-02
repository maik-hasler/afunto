using System.Text.Json.Nodes;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
	.WithPgAdmin();

var isTestEnv = builder.Environment.EnvironmentName == "Testing";

if (!isTestEnv)
	postgres.WithDataVolume();

// Fixed host ports below are for local dev convenience (predictable URLs like
// localhost:8080/9000/1025) - nothing in IntegrationTests/VisualTests depends
// on a specific port, they all resolve endpoints dynamically via
// _app.GetEndpoint(...)/_app.CreateHttpClient(...). CI runs many of these test
// jobs on the same runner in sequence, and a fixed host port left bound by a
// container DCP couldn't clean up in time (or another job's leftover
// container) fails every subsequent bind attempt the same way, which turned
// one transient hiccup into an unrecoverable "port is already allocated"
// retry storm (#2204). Passing port: null here lets Docker pick a free
// ephemeral port instead, so isTestEnv runs can never collide on a fixed one.
var mailpit = builder.AddContainer("mailpit", "ghcr.io/axllent/mailpit", "v1.31.2")
	.WithHttpEndpoint(port: isTestEnv ? null : 1080, targetPort: 8025, name: "webui", isProxied: false)
	.WithEndpoint(port: isTestEnv ? null : 1025, targetPort: 1025, name: "smtp", scheme: "tcp", isProxied: false);

// S3-compatible object storage, see ADR-13 for why RustFS replaced MinIO. The
// resource is named for its role, not the product, so a future swap touches
// only this block.
var storage = builder.AddContainer("storage", "rustfs/rustfs", "1.0.0")
	.WithEnvironment("RUSTFS_ACCESS_KEY", "storage")
	.WithEnvironment("RUSTFS_SECRET_KEY", "storage123")
	.WithHttpEndpoint(port: isTestEnv ? null : 9000, targetPort: 9000, name: "api", isProxied: false)
	.WithHttpEndpoint(port: isTestEnv ? null : 9001, targetPort: 9001, name: "console", isProxied: false)
	.WithHttpHealthCheck("/health", endpointName: "api");

var storageApiEndpoint = storage.GetEndpoint("api");

var database = postgres.AddDatabase("afunto");

var keycloakRealmPath = Path.GetFullPath(
	Path.Combine(builder.AppHostDirectory, "..", "..", "..", "..", "keycloak", "realms"));

var keycloakThemePath = Path.GetFullPath(
	Path.Combine(builder.AppHostDirectory, "..", "..", "..", "..", "keycloak", "themes", "afunto"));

var localRealm = JsonNode.Parse(
	File.ReadAllText(Path.Combine(keycloakRealmPath, "afunto-realm.json")))!;
if (localRealm["clients"] is JsonArray realmClients)
{
	foreach (var client in realmClients)
	{
		if (client is not JsonObject clientObject)
			continue;

		var clientId = clientObject["clientId"]?.GetValue<string>();

		if (clientId == "frontend")
		{
			clientObject["webOrigins"] = new JsonArray("*");
			clientObject["redirectUris"] = new JsonArray("http://localhost:*");

			if (clientObject["attributes"] is JsonObject frontendAttributes)
				frontendAttributes["post.logout.redirect.uris"] = "http://localhost:*";
		}

		if (clientId == "frontend-test")
			clientObject["enabled"] = true;

		if (clientId == "backend")
			clientObject["secret"] = "backend-secret";
	}
}

if (localRealm["users"] is JsonArray realmUsers)
{
	foreach (var user in realmUsers)
	{
		if (user is not JsonObject userObject)
			continue;

		var username = userObject["username"]?.GetValue<string>();

		if (username is "vera" or "olaf" or "admin")
			userObject["enabled"] = true;
	}
}

localRealm["bruteForceProtected"] = false;

localRealm["accessTokenLifespan"] = 3600;

localRealm["smtpServer"] = new JsonObject
{
	["host"] = "mailpit",
	["port"] = "1025",
	["from"] = "noreply@afunto.local",
	["fromDisplayName"] = "Afunto",
	["ssl"] = "false",
	["starttls"] = "false",
	["auth"] = "false",
};

var keycloakRealmImportPath = Path.Combine(
	Path.GetTempPath(), "afunto-aspire-realm-import");
Directory.CreateDirectory(keycloakRealmImportPath);
File.WriteAllText(
	Path.Combine(keycloakRealmImportPath, "afunto-realm.json"),
	localRealm.ToJsonString());

var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.7.4")
	.WithEnvironment("KC_DB", "dev-file")
	.WithBindMount(keycloakRealmImportPath, "/opt/keycloak/data/import", isReadOnly: true)
	.WithBindMount(keycloakThemePath, "/opt/keycloak/themes/afunto", isReadOnly: true)
	.WithArgs("start-dev", "--import-realm")
	.WithHttpEndpoint(port: isTestEnv ? null : 8080, targetPort: 8080, isProxied: false)

	.WithHttpHealthCheck("/realms/afunto/.well-known/openid-configuration");

var keycloakEndpoint = keycloak.GetEndpoint("http");

var mailpitSmtpEndpoint = mailpit.GetEndpoint("smtp");

var anonymousReadPermitLimit = builder.Configuration["RateLimiting:Read:AnonymousPermitLimit"] ?? "10000";

var backend = builder.AddProject<Projects.Api>("backend")
	.WithReference(database)
	.WaitFor(database)
	.WaitFor(keycloak)
	.WaitFor(storage)
	.WithEnvironment("Authentication__Authority",
		ReferenceExpression.Create($"{keycloakEndpoint}/realms/afunto"))
	.WithEnvironment("Authentication__ValidIssuers__0",
		ReferenceExpression.Create($"{keycloakEndpoint}/realms/afunto"))
	.WithEnvironment("Keycloak__BaseUrl",
		ReferenceExpression.Create($"{keycloakEndpoint}"))
	.WithEnvironment("Keycloak__ClientSecret", "backend-secret")
	.WithEnvironment("Smtp__Host", mailpitSmtpEndpoint.Property(EndpointProperty.Host))
	.WithEnvironment("Smtp__Port", mailpitSmtpEndpoint.Property(EndpointProperty.Port))
	.WithEnvironment("Storage__Endpoint", ReferenceExpression.Create($"{storageApiEndpoint}"))
	.WithEnvironment("Storage__AccessKey", "storage")
	.WithEnvironment("Storage__SecretKey", "storage123")
	.WithEnvironment("Storage__BucketName", "afunto")
	.WithEnvironment("RateLimiting__Write__PermitLimit", "10000")
	.WithEnvironment("RateLimiting__Read__AuthenticatedPermitLimit", "10000")

	.WithEnvironment("RateLimiting__Read__AnonymousPermitLimit", anonymousReadPermitLimit);

if (isTestEnv)
{
	// Every existing integration/visual test runs the backend as ASPNETCORE_ENVIRONMENT=
	// Development (the default below), which skips RequiredConfigurationValidator entirely
	// and always runs migrate+seed - the non-Development branch of Program.cs has never
	// been exercised by a test (#2204). ProductionEnvironmentFixture opts a single, separate
	// test class into "Production" here without touching that default for the other ~70.
	backend.WithEnvironment("ASPNETCORE_ENVIRONMENT",
		builder.Configuration["Testing:BackendAspNetCoreEnvironment"] ?? "Development");

	backend.WithEnvironment("Geocoding__UseFakeService", "true");
}

// Outside Development, Program.cs only migrates when this is true - see the isTestEnv
// block above. Unconditional (not isTestEnv-gated) for the same reason
// RateLimiting:Read:AnonymousPermitLimit above is: a config passthrough, not a test hook,
// and a no-op in real Development usage since Program.cs never reads it there.
if (builder.Configuration["Database:MigrateOnStartup"] is { } migrateOnStartup)
	backend.WithEnvironment("Database__MigrateOnStartup", migrateOnStartup);

// Same passthrough shape as Database:MigrateOnStartup above, for the same reason -
// Program.cs only seeds outside Development when both this and MigrateOnStartup are
// true, and ProductionEnvironmentFixture is the only caller that ever sets it. Without
// this block the fixture's own --Database:SeedOnStartup=true only reaches the AppHost's
// *own* configuration, never the backend project it launches - Aspire only forwards what
// is explicitly passed via WithEnvironment.
if (builder.Configuration["Database:SeedOnStartup"] is { } seedOnStartup)
	backend.WithEnvironment("Database__SeedOnStartup", seedOnStartup);

// Same passthrough shape as Database:MigrateOnStartup above, for the same reason - see
// Program.cs's comment on RequireHttpsMetadata for why ProductionEnvironmentFixture is
// the only caller that ever sets this.
if (builder.Configuration["Authentication:RequireHttpsMetadata"] is { } requireHttpsMetadata)
	backend.WithEnvironment("Authentication__RequireHttpsMetadata", requireHttpsMetadata);

// Same passthrough shape as Database:MigrateOnStartup above, for the same reason. Unlike
// the Read/Write limits, the map tile budget is not raised to 10000 here, so it keeps
// appsettings.json's production value - which sits *below* the anonymous Read limit
// IntegrationTestFixture pins. MapTileRateLimitingTests proves tiles are on a more
// generous budget than the content bucket by out-requesting that Read limit, and it
// cannot do that while the tile budget is the smaller of the two. Only that fixture ever
// sets this; without the passthrough its argument reaches the AppHost's own configuration
// and never the backend project it launches.
if (builder.Configuration["RateLimiting:MapTiles:PermitLimit"] is { } mapTilesPermitLimit)
	backend.WithEnvironment("RateLimiting__MapTiles__PermitLimit", mapTilesPermitLimit);

var frontend = builder.AddViteApp("frontend", "../../../../frontend")
	.WithPnpm()
	.WithReference(backend)
	.WaitFor(backend)
	.WithEnvironment("VITE_API_URL", backend.GetEndpoint("http"))
	.WithEnvironment("VITE_KEYCLOAK_AUTHORITY_URL",
		ReferenceExpression.Create($"{keycloakEndpoint}/realms/afunto"))
	.WithEnvironment("STORAGE_PUBLIC_URL", ReferenceExpression.Create($"{storageApiEndpoint}"))

	.WithEnvironment("VITE_TOAST_LIFETIME_MS", isTestEnv ? "0" : "5000");

backend.WithEnvironment("Cors__Origins__0", frontend.GetEndpoint("http"));

builder.Build().Run();
