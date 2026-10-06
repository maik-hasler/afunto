using Application.Achievements;
using Application.Achievements.BadgeCatalog;
using Application.AuditLogs;
using Application.Common;
using Application.Common.Email;
using Application.Common.Geocoding;
using Application.Common.Keycloak;
using Application.Common.Maps;
using Application.Common.Persistence;
using Application.Common.RateLimiting;
using Application.Common.Storage;
using Application.Engagements;
using Application.Meta;
using Application.Notifications;
using Application.Organizations;
using Application.Reports;
using Application.VolunteerOpportunities;
using Infrastructure.Achievements;
using Infrastructure.BackgroundJobs;
using Infrastructure.Common;
using Infrastructure.Email;
using Infrastructure.Geocoding;
using Infrastructure.Geocoding.GermanPlaces;
using Infrastructure.Keycloak;
using Infrastructure.Maps;
using Infrastructure.Meta;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Interceptors;
using Infrastructure.Persistence.Options;
using Infrastructure.Persistence.Repositories;
using Infrastructure.RateLimiting;
using Infrastructure.Storage;
using Infrastructure.VolunteerOpportunities;
using Domain.VolunteerOpportunities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Infrastructure;

public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddInfrastructureServices(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		services.ConfigureOptions<ConnectionStringOptionsSetup>();

		services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
		services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
		services.AddScoped<ISaveChangesInterceptor, ConvertDomainEventsToOutboxMessagesInterceptor>();

		services.AddSingleton<IPinGenerator, RandomPinGenerator>();

		services.AddSingleton<ICheckInAttemptLimiter, CheckInAttemptLimiter>();

		services.AddSingleton<IVersionProvider, VersionProvider>();

		services.AddDbContext<ApplicationDbContext>((sp, options) =>
		{
			options.AddInterceptors(
				sp.GetServices<ISaveChangesInterceptor>());

			options.UseNpgsql(
				sp.GetRequiredService<IOptions<ConnectionStringOptions>>().Value.Afunto,
				npg =>
				{
					npg.MigrationsAssembly("Infrastructure");

					// No EnableRetryOnFailure: ExecuteInTransactionAsync (ApplicationDbContext)
					// wraps a manual BeginTransactionAsync around the whole command handler,
					// including any non-idempotent I/O it performs (Keycloak calls, SMTP sends).
					// A retrying execution strategy would replay all of that on a transient
					// error instead of just the DB write.
				});

			options.UseSnakeCaseNamingConvention();
		});

		services.AddScoped<IApplicationDbContextInitializer, ApplicationDbContextInitializer>();

		services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ApplicationDbContext>());

		services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

		services.AddScoped<IVolunteerOpportunityReadRepository, VolunteerOpportunityReadRepository>();

		services.AddScoped<IOrganizationReadRepository, OrganizationReadRepository>();

		services.AddScoped<IEngagementReadRepository, EngagementReadRepository>();

		services.AddScoped<INotificationReadRepository, NotificationReadRepository>();

		services.AddScoped<IOrganizationDashboardReadRepository, OrganizationDashboardReadRepository>();

		services.AddScoped<IAdminOrganizationReadRepository, AdminOrganizationReadRepository>();

		services.AddScoped<IAdminReportReadRepository, AdminReportReadRepository>();

		services.AddScoped<IAdminAuditLogReadRepository, AdminAuditLogReadRepository>();

		services.AddScoped<IAchievementReadRepository, AchievementReadRepository>();

		services.ConfigureOptions<BadgeCatalogOptionsSetup>();
		services.AddSingleton<IBadgeCatalogService, BadgeCatalogService>();

		services.ConfigureOptions<SmtpOptionsSetup>();
		services.AddSingleton<EmailMetrics>();
		services.AddSingleton<EmailRateLimiter>();
		services.AddScoped<IEmailService, SmtpEmailService>();
		services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();
		services.ConfigureOptions<ApiOptionsSetup>();
		services.AddSingleton<IUnsubscribeLinkBuilder, UnsubscribeLinkBuilder>();
		services.ConfigureOptions<EngagementReminderOptionsSetup>();
		services.AddHostedService<EngagementReminderJob>();
		services.ConfigureOptions<OrganizerNotificationDigestOptionsSetup>();
		services.AddHostedService<OrganizerNotificationDigestJob>();
		services.ConfigureOptions<AutomaticCheckInOptionsSetup>();
		services.AddHostedService<AutomaticCheckInJob>();
		services.ConfigureOptions<OutboxOptionsSetup>();
		services.AddSingleton<OutboxMetrics>();
		services.AddHostedService<OutboxProcessorJob>();
		services.AddHostedService<OutboxRetentionJob>();
		services.AddHostedService<GeocodingRetryJob>();
		services.AddHostedService<OrganizationMembershipBackfillJob>();
		services.ConfigureOptions<InvitationExpiryOptionsSetup>();
		services.AddHostedService<InvitationExpiryJob>();
		services.ConfigureOptions<CheckInAttemptPruneOptionsSetup>();
		services.AddHostedService<CheckInAttemptPruneJob>();
		services.ConfigureOptions<NotificationRetentionOptionsSetup>();
		services.AddHostedService<NotificationRetentionJob>();
		services.ConfigureOptions<AbuseReportRetentionOptionsSetup>();
		services.AddHostedService<AbuseReportRetentionJob>();

		services.AddSingleton<IGermanPlaceDirectory, GermanPlaceDirectory>();

		if (configuration.GetValue<bool>("Geocoding:UseFakeService"))
		{
			services.AddSingleton<IGeocodingService, FakeGeocodingService>();
		}
		else
		{
			services.ConfigureOptions<GeocodingOptionsSetup>();
			services.AddHttpClient<IGeocodingService, NominatimGeocodingService>(
				(sp, client) =>
				{
					var geocodingOptions = sp.GetRequiredService<IOptions<GeocodingOptions>>().Value;
					client.BaseAddress = new Uri(geocodingOptions.BaseUrl.TrimEnd('/') + "/");
					client.Timeout = TimeSpan.FromSeconds(geocodingOptions.TimeoutSeconds);
					client.DefaultRequestHeaders.UserAgent.ParseAdd(geocodingOptions.UserAgent);
				})

				.AddStandardResilienceHandler(options => options.Retry.MaxRetryAttempts = 1);
		}

		// SizeLimit makes the shared cache evict under pressure instead of growing without
		// bound (#2215) - every cache.Set call site across the codebase must supply a Size
		// once this is set, or MemoryCache.Set throws at runtime.
		var memoryCacheSizeLimitBytes = configuration.GetValue("MemoryCache:SizeLimitBytes", 100L * 1024 * 1024);
		services.AddMemoryCache(o => o.SizeLimit = memoryCacheSizeLimitBytes);
		services.ConfigureOptions<MapTileOptionsSetup>();
		services.AddHttpClient<IMapTileService, OpenStreetMapTileService>(
			(sp, client) =>
			{
				var mapTileOptions = sp.GetRequiredService<IOptions<MapTileOptions>>().Value;
				client.BaseAddress = new Uri(mapTileOptions.BaseUrl.TrimEnd('/') + "/");
				client.Timeout = TimeSpan.FromSeconds(mapTileOptions.TimeoutSeconds);
				client.DefaultRequestHeaders.UserAgent.ParseAdd(mapTileOptions.UserAgent);
			});

		services.ConfigureOptions<StorageSettingsSetup>();
		services.AddSingleton<IFileStorageService, S3FileStorageService>();

		services.ConfigureOptions<KeycloakOptionsSetup>();

		services.AddSingleton<KeycloakAdminTokenProvider>();
		services.AddHttpClient(KeycloakAdminTokenProvider.HttpClientName, (sp, client) =>
		{
			var keycloakOptions = sp.GetRequiredService<IOptions<KeycloakOptions>>().Value;
			client.BaseAddress = new Uri(keycloakOptions.BaseUrl);
			client.Timeout = TimeSpan.FromSeconds(keycloakOptions.TimeoutSeconds);
		});

		services.AddHttpClient<IKeycloakOrganizationService, KeycloakOrganizationService>(
			(sp, client) =>
			{
				var keycloakOptions = sp.GetRequiredService<IOptions<KeycloakOptions>>().Value;
				client.BaseAddress = new Uri(keycloakOptions.BaseUrl);
				client.Timeout = TimeSpan.FromSeconds(keycloakOptions.TimeoutSeconds);
			});

		services.AddHttpClient<IKeycloakUserService, KeycloakUserService>(
			(sp, client) =>
			{
				var keycloakOptions = sp.GetRequiredService<IOptions<KeycloakOptions>>().Value;
				client.BaseAddress = new Uri(keycloakOptions.BaseUrl);
				client.Timeout = TimeSpan.FromSeconds(keycloakOptions.TimeoutSeconds);
			});

		return services;
	}
}
