using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Organizations.GetOrgInvitations.v1;
using Domain.Organizations;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;

namespace Api.Organizations.GetOrgInvitations.v1;

internal sealed class GetOrgInvitationsEndpoint : IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app)
	{
		app.MapGet("/organizations/{organizationId:guid}/invitations", GetOrgInvitationsAsync)
			.WithName("GetOrgInvitations")
			.WithTags("Organizations")
			.Produces<List<OrgInvitationDto>>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Read)
			.MapToApiVersion(1);
	}

	private static async Task<IResult> GetOrgInvitationsAsync(
		[FromRoute] Guid organizationId,
		[FromServices] ISender sender,
		HttpContext httpContext,
		CancellationToken cancellationToken)
	{
		var subClaim = httpContext.User.FindFirst("sub")?.Value;
		if (subClaim is null || !Guid.TryParse(subClaim, out var requestingUserId))
			return Results.Problem("Unable to identify the current user.", statusCode: StatusCodes.Status401Unauthorized);

		var query = new GetOrgInvitationsQuery(OrganizationId.Create(organizationId).GetValueOrThrow(), UserId.Create(requestingUserId).GetValueOrThrow());
		var result = await sender.Send(query, cancellationToken);
		return Results.Ok(result);
	}
}
