using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Messaging;
using Application.Users.SetUserEnabled.v1;
using Microsoft.AspNetCore.Mvc;

namespace Api.Users.SetUserEnabled.v1;

internal sealed class SetUserEnabledEndpoint : IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPut("/admin/users/{userId:guid}/enabled", SetUserEnabledAsync)
			.WithName("SetUserEnabled")
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

	private static async Task<IResult> SetUserEnabledAsync(
		[FromRoute] Guid userId,
		[FromBody] SetUserEnabledRequest request,
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

		var command = new SetUserEnabledCommand(userId, actingUserId, request.Enabled);

		await sender.Send(command, cancellationToken);

		return Results.NoContent();
	}
}
