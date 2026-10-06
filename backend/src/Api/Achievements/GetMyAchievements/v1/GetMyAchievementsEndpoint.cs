using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Achievements;
using Application.Achievements.GetMyAchievements.v1;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;

namespace Api.Achievements.GetMyAchievements.v1;

internal sealed class GetMyAchievementsEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapGet("/me/achievements", GetMyAchievementsAsync)
			.WithName("GetMyAchievements")
			.WithTags("Achievements")
			.Produces<List<AchievementSummary>>()
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoDefaultUserPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Read)
			.MapToApiVersion(1);

	private static async Task<IResult> GetMyAchievementsAsync(
		[FromServices] ISender sender,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		var subClaim = httpContext.User.FindFirst("sub")?.Value;
		if (subClaim is null || !Guid.TryParse(subClaim, out var userId))
			return Results.Problem("Unable to identify the current user.", statusCode: StatusCodes.Status401Unauthorized);

		var query = new GetMyAchievementsQuery(UserId.Create(userId).GetValueOrThrow());
		var result = await sender.Send(query, cancellationToken);
		return Results.Ok(result);
	}
}
