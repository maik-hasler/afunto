using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Messaging;
using Application.Users.SetUserAdminStatus.v1;
using Microsoft.AspNetCore.Mvc;

namespace Api.Users.SetUserAdminStatus.v1;

internal sealed class SetUserAdminStatusEndpoint : IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPut("/admin/users/{userId:guid}/admin-status", SetUserAdminStatusAsync)
			.WithName("SetUserAdminStatus")
			.WithTags("Admin")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status409Conflict)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoAdminPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> SetUserAdminStatusAsync(
		[FromRoute] Guid userId,
		[FromBody] SetUserAdminStatusRequest request,
		[FromServices] ISender sender,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		var subClaim = httpContext.User.FindFirst("sub")?.Value;
		if (subClaim is null || !Guid.TryParse(subClaim, out var actingUserId))
		{
			return Results.Problem(
				"Unable to identify the current user.",
				statusCode: StatusCodes.Status401Unauthorized);
		}

		var command = new SetUserAdminStatusCommand(userId, actingUserId, request.IsAdmin);

		await sender.Send(command, cancellationToken);

		return Results.NoContent();
	}
}
