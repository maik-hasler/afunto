using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Users.UpdateNotificationPreferences.v1;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;

namespace Api.Users.UpdateNotificationPreferences.v1;

internal sealed class UpdateNotificationPreferencesEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPut("/users/me/notification-preferences", UpdateNotificationPreferencesAsync)
			.WithName("UpdateNotificationPreferences")
			.WithTags("Users")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoDefaultUserPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> UpdateNotificationPreferencesAsync(
		[FromBody] UpdateNotificationPreferencesRequest request,
		[FromServices] ISender sender,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		var subClaim = httpContext.User.FindFirst("sub")?.Value;
		if (subClaim is null || !Guid.TryParse(subClaim, out var userId))
		{
			return Results.Problem("Unable to identify the current user.", statusCode: StatusCodes.Status401Unauthorized);
		}

		var command = new UpdateNotificationPreferencesCommand(
			UserId.Create(userId).GetValueOrThrow(),
			request.NotifyOnNewSignUp,
			request.NotifyOnWithdrawal,
			request.NotifyOnEngagementConfirmed,
			request.NotifyOnEngagementCancelled,
			request.NotifyOnEngagementReminder);

		await sender.Send(command, cancellationToken);

		return Results.NoContent();
	}
}
