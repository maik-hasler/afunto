using System.Diagnostics;
using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.ExceptionHandlers;
using Api.Common.Health;
using Api.Common.Logging;
using Api.Common.Middleware;
using Api.Common.Network;
using Api.Common.OutputCaching;
using Api.Common.RateLimiting;
using Application;
using Application.Common.Startup;
using Asp.Versioning;
using Infrastructure;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var isDesignTimeToolInvocation = args.Any(a => a.StartsWith("--applicationName=", StringComparison.Ordinal));

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddMetricsEndpoint();

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// .NET 10 minimal-API opt-in (#1146, #1173): without this, every
// [MaxLength]/[Required]/etc. DataAnnotation on a request DTO only affects the
// generated OpenAPI/NSwag client - ASP.NET Core never evaluates DataAnnotations
// on minimal-API parameters unless this is registered, so the server itself
// accepted unbounded/missing values on every endpoint.
builder.Services.AddValidation();

var apiVersioning = builder.Services.AddApiVersioning(options =>
	{
		options.DefaultApiVersion = new ApiVersion(1);
		options.ReportApiVersions = true;
		options.AssumeDefaultVersionWhenUnspecified = true;
		options.ApiVersionReader = new UrlSegmentApiVersionReader();
	})
	.AddApiExplorer(options =>
	{
		options.GroupNameFormat = "'v'V";
		options.SubstituteApiVersionInUrl = true;
	});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
	.AddJwtBearer(options =>
	{
		options.MapInboundClaims = false;
		options.Authority = builder.Configuration["Authentication:Authority"];
		// Real production Keycloak is always HTTPS, so this stays strict there by
		// default. The override exists only for ProductionEnvironmentFixture, which
		// boots the Production branch of this file against the Aspire test network's
		// Keycloak - always plain HTTP there, with no TLS termination in front of it -
		// to exercise RequiredConfigurationValidator/migrate-on-startup/HSTS without
		// needing a genuinely HTTPS-secured IdP available in that environment.
		options.RequireHttpsMetadata = builder.Configuration.GetValue<bool?>(
			"Authentication:RequireHttpsMetadata") ?? !builder.Environment.IsDevelopment();
		options.TokenValidationParameters = new TokenValidationParameters
		{
			ValidateAudience = true,
			ValidAudience = builder.Configuration["Authentication:ValidAudience"],
			RoleClaimType = "roles",
			ValidIssuers = builder.Configuration
				.GetSection("Authentication:ValidIssuers").Get<string[]>(),
		};
	});

builder.Services.AddAuthorizationBuilder()
	.AddPolicy(AuthorizationPolicies.AfuntoAdminPolicy, policy =>
		policy.RequireClaim(AuthorizationPolicies.RealmClaim, AuthorizationPolicies.AfuntoRealm)
			.RequireRole(AuthorizationPolicies.AdminRole))
	.AddDefaultPolicy(AuthorizationPolicies.AfuntoDefaultUserPolicy, policy =>
		policy.RequireClaim(AuthorizationPolicies.RealmClaim, AuthorizationPolicies.AfuntoRealm)
			.RequireRole(AuthorizationPolicies.DefaultUser))
	.AddPolicy(AuthorizationPolicies.AfuntoOrganisatorPolicy, policy =>
		policy.RequireClaim(AuthorizationPolicies.RealmClaim, AuthorizationPolicies.AfuntoRealm)
			.RequireRole(AuthorizationPolicies.OrganisatorRole));

builder.Services.AddCors(options =>
	options.AddDefaultPolicy(policy =>
		policy.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4321"])
			.AllowAnyHeader()
			.WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")));

builder.Services.AddHttpClient(KeycloakHealthCheck.HttpClientName, client =>
	client.Timeout = TimeSpan.FromSeconds(5));

builder.Services.AddHealthChecks()
	.AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
	.AddCheck<KeycloakHealthCheck>("keycloak", tags: ["ready"])
	.AddCheck<StorageHealthCheck>("storage", tags: ["ready"]);

builder.Services.AddEndpoints();
builder.Services.AddRateLimitingPolicies(builder.Configuration);
builder.Services.AddOutputCachingPolicies(builder.Configuration);

builder.Services.AddResponseCompression(options =>
{
	options.EnableForHttps = true;
	options.Providers.Add<BrotliCompressionProvider>();
	options.Providers.Add<GzipCompressionProvider>();
	options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Append("application/json");
});

var trustedNetworks = builder.Configuration.GetSection("TrustedNetworks").Get<TrustedNetworksOptions>()
	?? new TrustedNetworksOptions();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
	options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
	foreach (var cidr in trustedNetworks.Cidrs)
		options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
});

builder.Services.AddHttpLogging(logging =>
{
	logging.LoggingFields = HttpLoggingFields.RequestMethod
		| HttpLoggingFields.RequestPath
		| HttpLoggingFields.ResponseStatusCode
		| HttpLoggingFields.Duration;
});
builder.Services.AddHttpLoggingInterceptor<TraceIdHttpLoggingInterceptor>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ResultFailureExceptionHandler>();
builder.Services.AddExceptionHandler<ConcurrencyExceptionHandler>();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();

apiVersioning.AddOpenApi(versioned =>
{
	var options = versioned.Document;

	options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
	options.AddDocumentTransformer((document, _, _) =>
	{
		document.Info = new OpenApiInfo
		{
			Title = "Afunto API",
			Version = "v1",
			Description = "API for the Afunto application"
		};

		foreach (var pathItem in document.Paths.Values)
		{
			if (pathItem.Operations is null)
				continue;

			foreach (var operation in pathItem.Operations.Values)
			{
				if (operation.RequestBody?.Content is not { } content ||
					!content.TryGetValue("multipart/form-data", out var mediaType) ||
					mediaType.Schema?.Properties is not { } properties)
					continue;

				foreach (var propertyName in properties.Keys.ToList())
				{
					if (properties[propertyName] is OpenApiSchemaReference { Reference.Id: "IFormFile" })
						properties[propertyName] = new OpenApiSchema
						{
							Type = JsonSchemaType.String,
							Format = "binary"
						};
				}
			}
		}

		document.Components?.Schemas?.Remove("IFormFile");

		return Task.CompletedTask;
	});
});

var app = builder.Build();

if (!isDesignTimeToolInvocation)
{
	var missingConfiguration = RequiredConfigurationValidator.FindMissing(
		app.Environment.IsDevelopment(),
		app.Configuration.GetConnectionString("afunto"),
		app.Configuration["Keycloak:ClientSecret"],
		app.Configuration["Authentication:Authority"],
		app.Configuration.GetSection("Cors:Origins").Get<string[]>(),
		app.Configuration["Smtp:Host"],
		app.Configuration["Smtp:Port"]);

	if (missingConfiguration.Count > 0)
		throw new InvalidOperationException(
			$"Missing required configuration outside Development: {string.Join(", ", missingConfiguration)}.");
}

if (app.Environment.IsDevelopment())
{
	var scope = app.Services.CreateScope();

	var initializer = scope.ServiceProvider.GetRequiredService<IApplicationDbContextInitializer>();

	await initializer.MigrateAsync();

	try
	{
		await initializer.SeedAsync();
	}
	catch (Exception ex)
	{
		app.Logger.LogError(ex, "An exception occurred while seeding the database");
	}

	app.MapOpenApi().WithDocumentPerVersion();
}
else if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
	var scope = app.Services.CreateScope();

	var initializer = scope.ServiceProvider.GetRequiredService<IApplicationDbContextInitializer>();

	await initializer.MigrateAsync();

	if (app.Configuration.GetValue<bool>("Database:SeedOnStartup"))
	{
		try
		{
			await initializer.SeedAsync();
		}
		catch (Exception ex)
		{
			app.Logger.LogError(ex, "An exception occurred while seeding the database");
		}
	}
}

app.MapDefaultEndpoints(
	health => health
		.RequireRateLimiting(RateLimitingPolicies.Read)
		.CacheOutput(OutputCachingPolicies.HealthCheck),
	alive => alive.RequireRateLimiting(RateLimitingPolicies.Read));
app.MapMetricsEndpoint();

app.UseForwardedHeaders();

// Bridges RequestSizeLimitAttribute metadata (set per-endpoint via .WithMetadata()) to
// Kestrel's IHttpMaxRequestBodySizeFeature - see RequestSizeLimitMiddleware for why minimal
// API endpoints need this explicitly. Placed before anything that could read the request
// body; endpoint metadata is already resolved this early since UseRouting() is never called
// explicitly, so WebApplication runs routing at the very start of the pipeline (#1177).
app.UseMiddleware<RequestSizeLimitMiddleware>();

app.UseHttpLogging();
app.UseResponseCompression();
app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();

app.Use(async (context, next) =>
{
	context.Response.OnStarting(() =>
	{
		context.Response.Headers["X-Content-Type-Options"] = "nosniff";
		context.Response.Headers["X-Frame-Options"] = "DENY";
		context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

		// Bearer tokens must never travel over a downgraded HTTP connection - skipped in
		// Development since local dev runs over plain HTTP (#1370).
		if (!app.Environment.IsDevelopment())
			context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

		// PII-bearing authenticated responses (GET /v1/users/me, member search, the
		// check-in PIN, etc.) must never be retained by a shared proxy or a browser's
		// back/forward cache. AllowAnonymous endpoints are unaffected and opt into
		// caching explicitly via OutputCachingPolicies instead (#1180).
		if (context.User.Identity?.IsAuthenticated == true)
			context.Response.Headers["Cache-Control"] = "no-store";

		context.Response.Headers["X-Trace-Id"] =
			Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

		return Task.CompletedTask;
	});

	await next();
});

app.UseRateLimiter();
app.UseAuthorization();

app.Use(async (ctx, next) =>
{
	using (app.Logger.BeginScope(new Dictionary<string, object?>
	{
		["TraceId"] = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier,
		["UserId"] = ctx.User.FindFirst("sub")?.Value ?? "anonymous",
	}))
	{
		await next(ctx);
	}
});

app.UseMiddleware<LoginStreakMiddleware>();

// Placed after LoginStreakMiddleware, not before: a cache hit short-circuits the
// pipeline entirely (next() is never called), which would otherwise silently skip
// that day's login-streak update for every authenticated request that happens to
// land on a cached response (#1391).
app.UseOutputCache();

app.MapEndpoints();

await app.RunAsync();
