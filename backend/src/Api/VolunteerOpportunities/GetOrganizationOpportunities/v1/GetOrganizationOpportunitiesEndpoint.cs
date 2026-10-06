using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Pagination;
using Application.VolunteerOpportunities.GetOrganizationOpportunities.v1;
using Application.VolunteerOpportunities.GetVolunteerOpportunities.v1;
using Domain.Primitives;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.VolunteerOpportunities.GetOrganizationOpportunities.v1;

internal sealed class GetOrganizationOpportunitiesEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapGet("/organizations/{organizationId:guid}/opportunities", GetOrganizationOpportunitiesAsync)
			.WithName("GetOrganizationOpportunities")
			.Produces<PagedList<VolunteerOpportunitySummary>>()
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoDefaultUserPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Read)
			.MapToApiVersion(1);

	private static async Task<IResult> GetOrganizationOpportunitiesAsync(
		[FromRoute] Guid organizationId,
		[FromQuery] string status,
		[FromQuery] int pageNumber,
		[FromQuery] int pageSize,
		[FromServices] ISender sender,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		if (!Enum.TryParse<OpportunityStatus>(status, ignoreCase: true, out var parsedStatus))
			return Results.Problem(
				$"Status must be one of: {string.Join(", ", Enum.GetNames<OpportunityStatus>())}.",
				statusCode: StatusCodes.Status400BadRequest);

		if (pageNumber < 1)
			return Results.Problem("PageNumber must be at least 1.", statusCode: StatusCodes.Status400BadRequest);

		if (pageSize < 1 || pageSize > 100)
			return Results.Problem("PageSize must be between 1 and 100.", statusCode: StatusCodes.Status400BadRequest);

		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid)
			? UserId.Create(uid).GetValueOrThrow()
			: throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		var opportunities = await sender.Send(
			new GetOrganizationOpportunitiesQuery(organizationId, userId, parsedStatus, pageNumber, pageSize),
			cancellationToken);

		return Results.Ok(opportunities);
	}
}
