using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Users.DeleteMyAccount.v1;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;

namespace Api.Users.DeleteMyAccount.v1;

internal sealed class DeleteMyAccountEndpoint : IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapDelete("/users/me", DeleteMyAccountAsync)
			.WithName("DeleteMyAccount")
			.WithTags("Users")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoDefaultUserPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> DeleteMyAccountAsync(
		[FromServices] ISender sender,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		var subClaim = httpContext.User.FindFirst("sub")?.Value;
		if (subClaim is null || !Guid.TryParse(subClaim, out var userId))
		{
			return Results.Problem(
				"Unable to identify the current user.",
				statusCode: StatusCodes.Status401Unauthorized);
		}

		await sender.Send(new DeleteMyAccountCommand(UserId.Create(userId).GetValueOrThrow()), cancellationToken);

		return Results.NoContent();
	}
}
