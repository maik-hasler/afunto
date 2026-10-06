using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.OutputCaching;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.VolunteerOpportunities.DeleteOpportunityBanner.v1;
using Domain.Primitives;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using System.Security.Claims;

namespace Api.VolunteerOpportunities.DeleteOpportunityBanner.v1;

internal sealed class DeleteOpportunityBannerEndpoint : IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapDelete("/volunteer-opportunities/{opportunityId:guid}/banner", DeleteOpportunityBannerAsync)
			.WithName("DeleteOpportunityBanner")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> DeleteOpportunityBannerAsync(
		[FromRoute] Guid opportunityId,
		[FromServices] ISender sender,
		[FromServices] IOutputCacheStore outputCacheStore,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid)
			? UserId.Create(uid).GetValueOrThrow()
			: throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));

		await sender.Send(
			new DeleteOpportunityBannerCommand(opportunityId, userId),
			cancellationToken);

		await outputCacheStore.EvictVolunteerOpportunityListingCacheAsync(cancellationToken);

		return Results.NoContent();
	}
}
