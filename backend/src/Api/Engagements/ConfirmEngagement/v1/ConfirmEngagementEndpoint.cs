using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Engagements.ConfirmEngagement.v1;
using Domain.Engagements;
using Domain.Primitives;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Engagements.ConfirmEngagement.v1;

internal sealed class ConfirmEngagementEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPost("/engagements/{engagementId:guid}/confirm", ConfirmEngagementAsync)
			.WithName("ConfirmEngagement")
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

	private static async Task<IResult> ConfirmEngagementAsync(
		[FromRoute] Guid engagementId,
		[FromServices] ISender sender,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid) ? UserId.Create(uid).GetValueOrThrow() : throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));
		var command = new ConfirmEngagementCommand(EngagementId.Create(engagementId).GetValueOrThrow(), userId);
		var engagement = await sender.Send(command, cancellationToken);
		return Results.Ok(new EngagementStatusResponse(engagement.Id.Value, engagement.Status.ToString()));
	}
}
