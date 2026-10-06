using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Notifications.DeleteNotification.v1;
using Domain.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace Api.Notifications.DeleteNotification.v1;

internal sealed class DeleteNotificationEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapDelete("/notifications/{id:guid}", DeleteNotificationAsync)
			.WithName("DeleteNotification")
			.WithTags("Notifications")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoDefaultUserPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> DeleteNotificationAsync(
		[FromRoute] Guid id,
		[FromServices] ISender sender,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		var subClaim = httpContext.User.FindFirst("sub")?.Value;
		if (subClaim is null || !Guid.TryParse(subClaim, out var userId))
			return Results.Problem("Unable to identify the current user.", statusCode: StatusCodes.Status401Unauthorized);

		var command = new DeleteNotificationCommand(NotificationId.Create(id).GetValueOrThrow(), userId);
		var found = await sender.Send(command, cancellationToken);
		return found ? Results.NoContent() : Results.NotFound();
	}
}
