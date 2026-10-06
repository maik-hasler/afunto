using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Pagination;
using Application.Engagements;
using Application.Organizations.GetOrganizationEngagements.v1;
using Domain.Engagements;
using Domain.Primitives;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Organizations.GetOrganizationEngagements.v1;

internal sealed class GetOrganizationEngagementsEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapGet("/organizations/{organizationId:guid}/engagements", GetOrganizationEngagementsAsync)
			.WithName("GetOrganizationEngagements")
			.WithTags("Organizations")
			.Produces<PagedList<EngagementSummary>>()
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Read)
			.MapToApiVersion(1);

	private static async Task<IResult> GetOrganizationEngagementsAsync(
		[FromRoute] Guid organizationId,
		[FromQuery] int pageNumber,
		[FromQuery] int pageSize,
		[FromQuery] string? status,
		[FromQuery] string? search,
		[FromServices] ISender sender,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		if (pageNumber < 1)
			return Results.Problem("PageNumber must be at least 1.", statusCode: StatusCodes.Status400BadRequest);

		if (pageSize < 1 || pageSize > 100)
			return Results.Problem("PageSize must be between 1 and 100.", statusCode: StatusCodes.Status400BadRequest);

		EngagementStatus? parsedStatus = null;
		if (!string.IsNullOrWhiteSpace(status))
		{
			if (!Enum.TryParse<EngagementStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
				return Results.Problem("Status must be one of Pending, Confirmed, Cancelled, Withdrawn.", statusCode: StatusCodes.Status400BadRequest);
			parsedStatus = parsed;
		}

		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid) ? UserId.Create(uid).GetValueOrThrow() : throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));
		var query = new GetOrganizationEngagementsQuery(
			organizationId,
			userId,
			pageNumber,
			pageSize,
			parsedStatus,
			search);
		var result = await sender.Send(query, cancellationToken);
		return Results.Ok(result);
	}
}
