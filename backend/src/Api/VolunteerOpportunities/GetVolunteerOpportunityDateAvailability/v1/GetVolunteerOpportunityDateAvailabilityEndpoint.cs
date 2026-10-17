using Api.Common.Endpoints;
using Api.Common.OutputCaching;
using Api.Common.RateLimiting;
using Api.VolunteerOpportunities.Common;
using Application.Common.Messaging;
using Application.VolunteerOpportunities.GetVolunteerOpportunityDateAvailability.v1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using System.Security.Claims;

namespace Api.VolunteerOpportunities.GetVolunteerOpportunityDateAvailability.v1;

internal sealed class GetVolunteerOpportunityDateAvailabilityEndpoint
	: IEndpoint
{
	private const int MaxWindowDays = 62;

	public void MapEndpoint(
		IEndpointRouteBuilder app)
	{
		app.MapGet("/volunteer-opportunities/date-availability", GetVolunteerOpportunityDateAvailabilityAsync)
			.WithName("GetVolunteerOpportunityDateAvailability")
			.Produces<IReadOnlyList<VolunteerOpportunityAvailableDate>>()
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.AllowAnonymous()
			.RequireRateLimiting(RateLimitingPolicies.Read)

			.CacheOutput(OutputCachingPolicies.VolunteerOpportunityDateAvailability)
			.MapToApiVersion(1);
	}

	private static async Task<IResult> GetVolunteerOpportunityDateAvailabilityAsync(
		[AsParameters] GetVolunteerOpportunityDateAvailabilityRequest request,
		HttpRequest httpRequest,
		[FromServices] ISender sender,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		if (request.To < request.From)
			return Results.Problem("'To' must not be before 'From'.", statusCode: StatusCodes.Status400BadRequest);

		if (request.To - request.From > TimeSpan.FromDays(MaxWindowDays))
			return Results.Problem($"The requested window must not exceed {MaxWindowDays} days.", statusCode: StatusCodes.Status400BadRequest);

		var filterProblem = VolunteerOpportunityFilterValidation.Validate(
			request.CenterLatitude,
			request.CenterLongitude,
			request.RadiusKm,
			request.Occurrence,
			request.ParticipationType,
			request.Categories,
			request.Keyword,
			Guid.TryParse(user.FindFirstValue("sub"), out var uid) ? uid : null);

		if (filterProblem is not null)
			return filterProblem;

		var timezone = httpRequest.Headers["X-Timezone"].FirstOrDefault();

		var query = new GetVolunteerOpportunityDateAvailabilityQuery(
			request.From,
			request.To,
			timezone,
			request.Occurrence,
			request.ParticipationType,
			request.IsRemote,
			request.CenterLatitude,
			request.CenterLongitude,
			request.RadiusKm,
			request.Categories,
			request.Tag,
			request.Keyword);

		var result = await sender.Send(query, cancellationToken);

		return Results.Ok(result);
	}
}
