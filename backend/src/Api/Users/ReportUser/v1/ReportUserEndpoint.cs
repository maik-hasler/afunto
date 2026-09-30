using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Users.ReportUser.v1;
using Domain.Primitives;
using Domain.Reports;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Users.ReportUser.v1;

internal sealed class ReportUserEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapPost("/users/{userId:guid}/reports", ReportUserAsync)
			.WithName("ReportUser")
			.WithTags("Users")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoDefaultUserPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> ReportUserAsync(
		[FromRoute] Guid userId,
		[FromBody] ReportUserRequest request,
		[FromServices] ISender sender,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		var reporterId = Guid.TryParse(user.FindFirstValue("sub"), out var uid)
			? UserId.Create(uid).GetValueOrThrow()
			: throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		if (!Enum.TryParse<ReportReason>(request.Reason, ignoreCase: true, out var reason))
		{
			return Results.Problem(
				"Invalid reason. Allowed values: Spam, IllegalContent, Fraud, Harassment, Other.",
				statusCode: StatusCodes.Status400BadRequest);
		}

		var command = new ReportUserCommand(userId, reporterId, reason, request.Details);

		await sender.Send(command, cancellationToken);

		return Results.NoContent();
	}
}
