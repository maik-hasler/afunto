using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Notifications.MarkAllNotificationsRead.v1;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;

namespace Api.Notifications.MarkAllNotificationsRead.v1;

internal sealed class MarkAllNotificationsReadEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPost("/notifications/read-all", MarkAllNotificationsReadAsync)
			.WithName("MarkAllNotificationsRead")
			.WithTags("Notifications")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoDefaultUserPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> MarkAllNotificationsReadAsync(
		[FromServices] ISender sender,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		var subClaim = httpContext.User.FindFirst("sub")?.Value;
		if (subClaim is null || !Guid.TryParse(subClaim, out var userId))
			return Results.Problem("Unable to identify the current user.", statusCode: StatusCodes.Status401Unauthorized);

		var command = new MarkAllNotificationsReadCommand(UserId.Create(userId).GetValueOrThrow());
		await sender.Send(command, cancellationToken);
		return Results.NoContent();
	}
}
