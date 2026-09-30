using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Organizations.SearchMemberCandidates.v1;
using Domain.Primitives;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Organizations.SearchMemberCandidates.v1;

internal sealed class SearchMemberCandidatesEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app)
	{
		app.MapGet("/organizations/{organizationId:guid}/members/search", SearchAsync)
			.WithName("SearchMemberCandidates")
			.Produces<IReadOnlyList<MemberCandidateDto>>()
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Read)
			.MapToApiVersion(1);
	}

	private static async Task<IResult> SearchAsync(
		[FromRoute] Guid organizationId,
		[FromQuery] string q,
		[FromServices] ISender sender,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(q) || q.Length < 4)
			return Results.Ok(Array.Empty<MemberCandidateDto>());

		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid)
			? UserId.Create(uid).GetValueOrThrow()
			: throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		var result = await sender.Send(
			new SearchMemberCandidatesQuery(organizationId, q, userId),
			cancellationToken);

		return Results.Ok(result);
	}
}
