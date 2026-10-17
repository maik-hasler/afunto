using Api.Common.Endpoints;
using Api.Common.OutputCaching;
using Api.Common.RateLimiting;
using Api.VolunteerOpportunities.Common;
using Application.Common.Messaging;
using Application.Common.Pagination;
using Application.VolunteerOpportunities.GetVolunteerOpportunities.v1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using System.Security.Claims;

namespace Api.VolunteerOpportunities.GetVolunteerOpportunities.v1;

internal sealed class GetVolunteerOpportunitiesEndpoint
	: IEndpoint
{
	public void MapEndpoint(
		IEndpointRouteBuilder app)
	{
		app.MapGet("/volunteer-opportunities", GetVolunteerOpportunitiesAsync)
			.WithName("GetVolunteerOpportunities")
			.Produces<PagedList<VolunteerOpportunitySummary>>()
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.AllowAnonymous()
			.RequireRateLimiting(RateLimitingPolicies.Read)
			.CacheOutput(OutputCachingPolicies.VolunteerOpportunityListing)
			.MapToApiVersion(1);
	}

	private static async Task<IResult> GetVolunteerOpportunitiesAsync(
		[AsParameters] GetVolunteerOpportunitiesRequest request,
		[FromServices] ISender sender,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		if (request.PageNumber < 1)
			return Results.Problem("PageNumber must be at least 1.", statusCode: StatusCodes.Status400BadRequest);

		if (request.PageSize < 1 || request.PageSize > 100)
			return Results.Problem("PageSize must be between 1 and 100.", statusCode: StatusCodes.Status400BadRequest);

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

		var query = new GetVolunteerOpportunitiesQuery(
			request.PageNumber,
			request.PageSize,
			request.Occurrence,
			request.ParticipationType,
			request.IsRemote,
			request.DateFrom,
			request.DateTo,
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
