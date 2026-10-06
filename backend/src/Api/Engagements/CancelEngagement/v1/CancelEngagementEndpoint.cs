using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.OutputCaching;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Engagements.CancelEngagement.v1;
using Domain.Engagements;
using Domain.Primitives;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using System.Security.Claims;

namespace Api.Engagements.CancelEngagement.v1;

internal sealed class CancelEngagementEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPost("/engagements/{engagementId:guid}/cancel", CancelEngagementAsync)
			.WithName("CancelEngagement")
			.WithTags("Engagements")
			.Produces<EngagementStatusResponse>()
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> CancelEngagementAsync(
		[FromRoute] Guid engagementId,
		[FromBody] CancelEngagementRequest? body,
		[FromServices] ISender sender,
		[FromServices] IOutputCacheStore outputCacheStore,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid) ? UserId.Create(uid).GetValueOrThrow() : throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		if (body?.Reason is { Length: > 500 })
			return Results.Problem("Reason must not exceed 500 characters.", statusCode: StatusCodes.Status400BadRequest);

		var command = new CancelEngagementCommand(EngagementId.Create(engagementId).GetValueOrThrow(), userId, body?.Reason);
		var engagement = await sender.Send(command, cancellationToken);

		await outputCacheStore.EvictVolunteerOpportunityListingCacheAsync(cancellationToken);

		return Results.Ok(new EngagementStatusResponse(engagement.Id.Value, engagement.Status.ToString(), engagement.CancellationReason));
	}
}
