using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.OutputCaching;
using Api.Common.RateLimiting;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.VolunteerOpportunities.DeleteVolunteerOpportunity.v1;
using Domain.Primitives;
using Domain.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using System.Security.Claims;

namespace Api.VolunteerOpportunities.DeleteVolunteerOpportunity.v1;

internal sealed class DeleteVolunteerOpportunityEndpoint
	: IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapDelete("/volunteer-opportunities/{opportunityId:guid}", DeleteVolunteerOpportunityAsync)
			.WithName("DeleteVolunteerOpportunity")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoOrganisatorPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Write)
			.MapToApiVersion(1);

	private static async Task<IResult> DeleteVolunteerOpportunityAsync(
		[FromRoute] Guid opportunityId,
		[FromServices] ISender sender,
		[FromServices] IOutputCacheStore outputCacheStore,
		ClaimsPrincipal user,
		CancellationToken cancellationToken)
	{
		try
		{
			var userId = Guid.TryParse(user.FindFirstValue("sub"), out var uid) ? UserId.Create(uid).GetValueOrThrow() : throw new ResultFailureException(Error.Validation("User.InvalidId", "Invalid user."));
			var command = new DeleteVolunteerOpportunityCommand(opportunityId, userId);
			await sender.Send(command, cancellationToken);
			await outputCacheStore.EvictVolunteerOpportunityListingCacheAsync(cancellationToken);
			return Results.NoContent();
		}
		catch (ResultFailureException ex) when (ex.Error.Type == ErrorType.NotFound)
		{
			return Results.NotFound();
		}
	}
}
