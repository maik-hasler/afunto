using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class ServiceDefaultsExtensions
{
	private const string EmailMeterName = "Afunto.Email";

	private const string OutboxMeterName = "Afunto.Outbox";

	public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
	{
		builder.ConfigureOpenTelemetry();
		builder.AddDefaultHealthChecks();

		builder.Services.AddMetrics();
		builder.Services.AddServiceDiscovery();
		builder.Services.ConfigureHttpClientDefaults(http =>
		{
			http.AddStandardResilienceHandler();
			http.AddServiceDiscovery();
		});

		return builder;
	}

	private static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
	{
		builder.Logging.AddOpenTelemetry(logging =>
		{
			logging.IncludeFormattedMessage = true;
			logging.IncludeScopes = true;
		});

		builder.Services.AddOpenTelemetry()
			.WithMetrics(metrics =>
			{
				metrics
					.AddAspNetCoreInstrumentation()
					.AddHttpClientInstrumentation()
					.AddRuntimeInstrumentation()
					.AddMeter(EmailMeterName)
					.AddMeter(OutboxMeterName)
					.AddPrometheusExporter();
			})
			.WithTracing(tracing =>
			{
				tracing
					.AddAspNetCoreInstrumentation()
					.AddHttpClientInstrumentation();
			});

		builder.AddOpenTelemetryExporters();

		return builder;
	}

	private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
	{
		if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
		{
			builder.Services.AddOpenTelemetry().UseOtlpExporter();
		}

		return builder;
	}

	private static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
	{
		builder.Services.AddHealthChecks()
			.AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

		return builder;
	}

	// An explicit Kestrel Listen call replaces the base image's ASPNETCORE_HTTP_PORTS-driven
	// binding entirely rather than adding to it, so the primary HTTP port has to be
	// re-declared here alongside the metrics port or the API stops listening on it.
	public static WebApplicationBuilder AddMetricsEndpoint(this WebApplicationBuilder builder)
	{
		var metricsPort = builder.Configuration.GetValue<int?>("Metrics:Port");
		if (metricsPort is not int port)
			return builder;

		var httpPort = builder.Configuration.GetValue<int?>("ASPNETCORE_HTTP_PORTS") ?? 8080;

		builder.WebHost.ConfigureKestrel(kestrel =>
		{
			kestrel.ListenAnyIP(httpPort);
			kestrel.ListenAnyIP(port);
		});

		return builder;
	}

	// Bound to the metrics port only, so /metrics never becomes reachable through the
	// public API port (and therefore never through the public Traefik route either).
	public static WebApplication MapMetricsEndpoint(this WebApplication app)
	{
		var metricsPort = app.Configuration.GetValue<int?>("Metrics:Port");
		if (metricsPort is int port)
			app.MapPrometheusScrapingEndpoint().RequireHost($"*:{port}");

		return app;
	}

	public static WebApplication MapDefaultEndpoints(
		this WebApplication app,
		Action<IEndpointConventionBuilder>? configureHealthEndpoint = null,
		Action<IEndpointConventionBuilder>? configureAliveEndpoint = null)
	{
		var healthEndpoint = app.MapHealthChecks("/health", new HealthCheckOptions
		{
			Predicate = r => r.Tags.Contains("ready")
		});
		var aliveEndpoint = app.MapHealthChecks("/alive", new HealthCheckOptions
		{
			Predicate = r => r.Tags.Contains("live")
		});

		configureHealthEndpoint?.Invoke(healthEndpoint);
		configureAliveEndpoint?.Invoke(aliveEndpoint);

		return app;
	}
}
