using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Users.GetNotificationPreferences.v1;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;

namespace Api.Users.GetNotificationPreferences.v1;

internal sealed class GetNotificationPreferencesEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapGet("/users/me/notification-preferences", GetNotificationPreferencesAsync)
			.WithName("GetNotificationPreferences")
			.WithTags("Users")
			.Produces<NotificationPreferencesResponse>()
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoDefaultUserPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Read)
			.MapToApiVersion(1);

	private static async Task<IResult> GetNotificationPreferencesAsync(
		[FromServices] ISender sender,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		var subClaim = httpContext.User.FindFirst("sub")?.Value;
		if (subClaim is null || !Guid.TryParse(subClaim, out var userId))
		{
			return Results.Problem("Unable to identify the current user.", statusCode: StatusCodes.Status401Unauthorized);
		}

		var result = await sender.Send(
			new GetNotificationPreferencesQuery(UserId.Create(userId).GetValueOrThrow()),
			cancellationToken);

		return Results.Ok(result);
	}
}
