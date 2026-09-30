using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Organizations.CreateInvitation.v1;
using Domain.Organizations;
using Domain.Primitives;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;

namespace Api.Organizations.CreateInvitation.v1;

internal sealed class CreateInvitationEndpoint : IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app)
	{
		app.MapPost("/organizations/{organizationId:guid}/invitations", CreateInvitationAsync)
			.WithName("CreateInvitation")
			.WithTags("Organizations")
			.Produces<CreateInvitationResponse>(StatusCodes.Status201Created)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status409Conflict)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);
	}

	private static async Task<IResult> CreateInvitationAsync(
		[FromRoute] Guid organizationId,
		[FromBody] CreateInvitationRequest request,
		[FromServices] ISender sender,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		var subClaim = httpContext.User.FindFirst("sub")?.Value;
		if (subClaim is null || !Guid.TryParse(subClaim, out var invitedById))
			return Results.Problem("Unable to identify the current user.", statusCode: StatusCodes.Status401Unauthorized);

		if (!Enum.TryParse<OrganizationMemberRole>(request.Role, ignoreCase: true, out var role) || !Enum.IsDefined(role))
			throw new ResultFailureException(Error.Validation("OrganizationInvitation.InvalidRole", "Invalid role."));

		var command = new CreateInvitationCommand(
			OrganizationId.Create(organizationId).GetValueOrThrow(),
			UserId.Create(request.InviteeId).GetValueOrThrow(),
			role,
			UserId.Create(invitedById).GetValueOrThrow());

		var invitation = await sender.Send(command, cancellationToken);

		return Results.Created(
			$"/v1/organizations/{organizationId}/invitations/{invitation.Id.Value}",
			new CreateInvitationResponse(invitation.Id.Value, invitation.ExpiresOn));
	}
}
