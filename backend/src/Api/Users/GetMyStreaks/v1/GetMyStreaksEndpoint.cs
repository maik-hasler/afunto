using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Users;
using Application.Users.GetMyStreaks.v1;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;

namespace Api.Users.GetMyStreaks.v1;

internal sealed class GetMyStreaksEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapGet("/me/streaks", GetMyStreaksAsync)
			.WithName("GetMyStreaks")
			.WithTags("Users")
			.Produces<StreakSummary>()
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoDefaultUserPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Read)
			.MapToApiVersion(1);

	private static async Task<IResult> GetMyStreaksAsync(
		[FromServices] ISender sender,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		var subClaim = httpContext.User.FindFirst("sub")?.Value;
		if (subClaim is null || !Guid.TryParse(subClaim, out var userId))
			return Results.Problem("Unable to identify the current user.", statusCode: StatusCodes.Status401Unauthorized);

		var query = new GetMyStreaksQuery(UserId.Create(userId).GetValueOrThrow());
		var result = await sender.Send(query, cancellationToken);
		return Results.Ok(result);
	}
}
