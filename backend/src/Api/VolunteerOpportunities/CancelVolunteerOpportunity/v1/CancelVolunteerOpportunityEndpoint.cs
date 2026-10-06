using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.OutputCaching;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.VolunteerOpportunities.CancelVolunteerOpportunity.v1;
using Domain.Primitives;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using System.Security.Claims;

namespace Api.VolunteerOpportunities.CancelVolunteerOpportunity.v1;

internal sealed class CancelVolunteerOpportunityEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPost("/volunteer-opportunities/{opportunityId:guid}/cancel", CancelVolunteerOpportunityAsync)
			.WithName("CancelVolunteerOpportunity")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> CancelVolunteerOpportunityAsync(
		[FromRoute] Guid opportunityId,
		[FromBody] CancelVolunteerOpportunityRequest? body,
		[FromServices] ISender sender,
		[FromServices] IOutputCacheStore outputCacheStore,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid)
			? UserId.Create(uid).GetValueOrThrow()
			: throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		if (body?.Reason is { Length: > 500 })
			return Results.Problem("Reason must not exceed 500 characters.", statusCode: StatusCodes.Status400BadRequest);

		await sender.Send(
			new CancelVolunteerOpportunityCommand(opportunityId, userId, body?.Reason),
			cancellationToken);

		await outputCacheStore.EvictVolunteerOpportunityListingCacheAsync(cancellationToken);

		return Results.NoContent();
	}
}
