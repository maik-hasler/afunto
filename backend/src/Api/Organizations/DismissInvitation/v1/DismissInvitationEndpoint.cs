using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Organizations.DismissInvitation.v1;
using Domain.Organizations;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;

namespace Api.Organizations.DismissInvitation.v1;

internal sealed class DismissInvitationEndpoint : IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app)
	{
		app.MapDelete("/organizations/{organizationId:guid}/invitations/{invitationId:guid}", DismissInvitationAsync)
			.WithName("DismissInvitation")
			.WithTags("Organizations")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);
	}

	private static async Task<IResult> DismissInvitationAsync(
		[FromRoute] Guid organizationId,
		[FromRoute] Guid invitationId,
		[FromServices] ISender sender,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		var subClaim = httpContext.User.FindFirst("sub")?.Value;
		if (subClaim is null || !Guid.TryParse(subClaim, out var requestingUserId))
			return Results.Problem("Unable to identify the current user.", statusCode: StatusCodes.Status401Unauthorized);

		var command = new DismissInvitationCommand(
			OrganizationId.Create(organizationId).GetValueOrThrow(),
			OrganizationInvitationId.Create(invitationId).GetValueOrThrow(),
			UserId.Create(requestingUserId).GetValueOrThrow());

		await sender.Send(command, cancellationToken);
		return Results.NoContent();
	}
}
