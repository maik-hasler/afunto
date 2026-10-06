using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Notifications.DeleteReadNotifications.v1;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;

namespace Api.Notifications.DeleteReadNotifications.v1;

internal sealed class DeleteReadNotificationsEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapDelete("/notifications/read", DeleteReadNotificationsAsync)
			.WithName("DeleteReadNotifications")
			.WithTags("Notifications")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoDefaultUserPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> DeleteReadNotificationsAsync(
		[FromServices] ISender sender,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		var subClaim = httpContext.User.FindFirst("sub")?.Value;
		if (subClaim is null || !Guid.TryParse(subClaim, out var userId))
			return Results.Problem("Unable to identify the current user.", statusCode: StatusCodes.Status401Unauthorized);

		var command = new DeleteReadNotificationsCommand(UserId.Create(userId).GetValueOrThrow());
		await sender.Send(command, cancellationToken);
		return Results.NoContent();
	}
}
