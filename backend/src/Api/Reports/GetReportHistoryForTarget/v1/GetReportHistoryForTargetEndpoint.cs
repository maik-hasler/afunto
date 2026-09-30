using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.Common.Messaging;
using Application.Reports.GetReportHistoryForTarget.v1;
using Domain.Reports;
using Microsoft.AspNetCore.Mvc;

namespace Api.Reports.GetReportHistoryForTarget.v1;

internal sealed class GetReportHistoryForTargetEndpoint : IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapGet("/admin/reports/targets/{targetType}/{targetId:guid}/history", GetReportHistoryForTargetAsync)
			.WithName("GetReportHistoryForTarget")
			.WithTags("Admin")
			.Produces<List<ReportHistoryEntry>>()
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoAdminPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Read)
			.MapToApiVersion(1);

	private static async Task<IResult> GetReportHistoryForTargetAsync(
		[FromRoute] string targetType,
		[FromRoute] Guid targetId,
		[FromServices] ISender sender,
		CancellationToken cancellationToken)
	{
		if (!Enum.TryParse<ReportTargetType>(targetType, ignoreCase: true, out var parsedTargetType))
		{
			return Results.Problem(
				"Invalid target type. Allowed values: VolunteerOpportunity, Organization, User.",
				statusCode: StatusCodes.Status400BadRequest);
		}

		var result = await sender.Send(new GetReportHistoryForTargetQuery(parsedTargetType, targetId), cancellationToken);

		return Results.Ok(result);
	}
}
