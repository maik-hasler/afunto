using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Organizations.ChangeMemberRole.v1;
using Domain.Organizations;
using Domain.Primitives;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Organizations.ChangeMemberRole.v1;

internal sealed class ChangeMemberRoleEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app)
	{
		app.MapPut("/organizations/{organizationId:guid}/members/{userId:guid}/role", ChangeMemberRoleAsync)
			.WithName("ChangeMemberRole")
			.WithTags("Organizations")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);
	}

	private static async Task<IResult> ChangeMemberRoleAsync(
		[FromRoute] Guid organizationId,
		[FromRoute] Guid userId,
		[FromBody] ChangeMemberRoleRequest request,
		[FromServices] ISender sender,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		var requestingUserId = Guid.TryParse(user.FindFirstValue("sub"), out var uid) ? UserId.Create(uid).GetValueOrThrow() : throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		if (!Enum.TryParse<OrganizationMemberRole>(request.Role, ignoreCase: true, out var role) || !Enum.IsDefined(role))
			throw new ResultFailureException(Error.Validation("OrganizationMembership.InvalidRole", "Invalid role."));

		var command = new ChangeMemberRoleCommand(
			OrganizationId.Create(organizationId).GetValueOrThrow(),
			UserId.Create(userId).GetValueOrThrow(),
			role,
			requestingUserId);

		await sender.Send(command, cancellationToken);

		return Results.NoContent();
	}
}
