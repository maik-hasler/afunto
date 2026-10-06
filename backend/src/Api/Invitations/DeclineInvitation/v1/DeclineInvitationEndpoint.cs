using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Invitations.DeclineInvitation.v1;
using Domain.Organizations;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;

namespace Api.Invitations.DeclineInvitation.v1;

internal sealed class DeclineInvitationEndpoint : IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app)
	{
		app.MapPost("/invitations/{invitationId:guid}/decline", DeclineInvitationAsync)
			.WithName("DeclineInvitation")
			.WithTags("Invitations")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoDefaultUserPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);
	}

	private static async Task<IResult> DeclineInvitationAsync(
		[FromRoute] Guid invitationId,
		[FromServices] ISender sender,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		var subClaim = httpContext.User.FindFirst("sub")?.Value;
		if (subClaim is null || !Guid.TryParse(subClaim, out var userId))
			return Results.Problem("Unable to identify the current user.", statusCode: StatusCodes.Status401Unauthorized);

		var command = new DeclineInvitationCommand(
			OrganizationInvitationId.Create(invitationId).GetValueOrThrow(),
			UserId.Create(userId).GetValueOrThrow());

		await sender.Send(command, cancellationToken);
		return Results.NoContent();
	}
}
