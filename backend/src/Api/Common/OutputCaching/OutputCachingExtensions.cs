using Microsoft.AspNetCore.OutputCaching;

namespace Api.Common.OutputCaching;

internal static class OutputCachingExtensions
{
	public static IServiceCollection AddOutputCachingPolicies(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		var options = configuration
			.GetSection("OutputCaching")
			.Get<OutputCachingOptions>() ?? new OutputCachingOptions();

		return services.AddOutputCache(cache =>
		{
			cache.AddPolicy(OutputCachingPolicies.LongPublicRead, policy =>
				policy.Expire(TimeSpan.FromSeconds(options.LongPublicReadSeconds)));

			cache.AddPolicy(OutputCachingPolicies.LongPublicReadByLanguage, policy =>
				policy.Expire(TimeSpan.FromSeconds(options.LongPublicReadSeconds))
					.SetVaryByHeader("X-Language"));

			cache.AddPolicy(OutputCachingPolicies.ShortPublicRead, policy =>
				policy.Expire(TimeSpan.FromSeconds(options.ShortPublicReadSeconds)));

			cache.AddPolicy(OutputCachingPolicies.HealthCheck, policy =>
				policy.Expire(TimeSpan.FromSeconds(options.HealthCheckSeconds)));

			cache.AddPolicy(OutputCachingPolicies.VolunteerOpportunityListing, policy =>
				policy.Expire(TimeSpan.FromSeconds(options.ShortPublicReadSeconds))
					.With(IsAnonymousRequest)
					.Tag(OutputCachingPolicies.VolunteerOpportunityListingTag));

			// Its own policy rather than reusing VolunteerOpportunityListing above - the
			// result here depends on the caller's X-Timezone, which the plain listing
			// doesn't vary by and shouldn't start fragmenting its cache over (#2203).
			cache.AddPolicy(OutputCachingPolicies.VolunteerOpportunityDateAvailability, policy =>
				policy.Expire(TimeSpan.FromSeconds(options.ShortPublicReadSeconds))
					.SetVaryByHeader("X-Timezone")
					.With(IsAnonymousRequest)
					.Tag(OutputCachingPolicies.VolunteerOpportunityListingTag));
		});
	}

	// The listing and date availability hide members-only opportunities from everyone but the
	// owning organization's members, so a signed-in response is per viewer and must neither be
	// served from nor stored in the shared cache (afunto#2406). Read off the header rather than
	// HttpContext.User so the answer does not depend on middleware order.
	private static bool IsAnonymousRequest(OutputCacheContext context) =>
		!context.HttpContext.Request.Headers.ContainsKey("Authorization");

	public static ValueTask EvictVolunteerOpportunityListingCacheAsync(
		this IOutputCacheStore store,
		CancellationToken cancellationToken) =>
		store.EvictByTagAsync(OutputCachingPolicies.VolunteerOpportunityListingTag, cancellationToken);
}
