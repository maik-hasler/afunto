using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Organizations.GetOrganizationCalendarEvents.v1;
using Domain.Primitives;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Organizations.GetOrganizationCalendarEvents.v1;

internal sealed class GetOrganizationCalendarEventsEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app)
	{
		app.MapGet("/organizations/{organizationId:guid}/calendar-events", GetOrganizationCalendarEventsAsync)
			.WithName("GetOrganizationCalendarEvents")
			.Produces<IReadOnlyList<OrganizationCalendarEventDto>>()
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoDefaultUserPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Read)
			.MapToApiVersion(1);
	}

	private static async Task<IResult> GetOrganizationCalendarEventsAsync(
		[FromRoute] Guid organizationId,
		[FromQuery] DateTimeOffset from,
		[FromQuery] DateTimeOffset to,
		[FromServices] ISender sender,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		if (to < from)
			return Results.Problem("'to' must not be before 'from'.", statusCode: StatusCodes.Status400BadRequest);

		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid)
			? UserId.Create(uid).GetValueOrThrow()
			: throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		var query = new GetOrganizationCalendarEventsQuery(organizationId, userId, from, to);
		var result = await sender.Send(query, cancellationToken);
		return Results.Ok(result);
	}
}
