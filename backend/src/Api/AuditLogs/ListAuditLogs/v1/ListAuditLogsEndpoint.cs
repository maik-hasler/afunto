using Api.Common.Authentication;
using Api.Common.Endpoints;
using Api.Common.RateLimiting;
using Application.AuditLogs.ListAuditLogs.v1;
using Application.Common.Messaging;
using Application.Common.Pagination;
using Domain.AuditLogs;
using Microsoft.AspNetCore.Mvc;

namespace Api.AuditLogs.ListAuditLogs.v1;

internal sealed class ListAuditLogsEndpoint : IEndpoint
{
	public void MapEndpoint(IEndpointRouteBuilder app) =>
		app.MapGet("/admin/audit-logs", ListAuditLogsAsync)
			.WithName("ListAuditLogs")
			.WithTags("Admin")
			.Produces<PagedList<AuditLogEntry>>()
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status401Unauthorized)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status500InternalServerError)
			.RequireAuthorization(AuthorizationPolicies.AfuntoAdminPolicy)
			.RequireRateLimiting(RateLimitingPolicies.Read)
			.MapToApiVersion(1);

	private static async Task<IResult> ListAuditLogsAsync(
		[FromQuery] int pageNumber,
		[FromQuery] int pageSize,
		[FromQuery] string? actionType,
		[FromQuery] string? subjectType,
		[FromQuery] Guid? actorUserId,
		[FromQuery] DateTimeOffset? from,
		[FromQuery] DateTimeOffset? to,
		[FromQuery] bool? oldestFirst,
		[FromServices] ISender sender,
		CancellationToken cancellationToken)
	{
		AuditActionType? parsedActionType = null;
		if (!string.IsNullOrWhiteSpace(actionType))
		{
			if (!Enum.TryParse<AuditActionType>(actionType, ignoreCase: true, out var value))
			{
				return Results.Problem(
					$"Invalid action type. Allowed values: {string.Join(", ", Enum.GetNames<AuditActionType>())}.",
					statusCode: StatusCodes.Status400BadRequest);
			}

			parsedActionType = value;
		}

		AuditSubjectType? parsedSubjectType = null;
		if (!string.IsNullOrWhiteSpace(subjectType))
		{
			if (!Enum.TryParse<AuditSubjectType>(subjectType, ignoreCase: true, out var value))
			{
				return Results.Problem(
					$"Invalid subject type. Allowed values: {string.Join(", ", Enum.GetNames<AuditSubjectType>())}.",
					statusCode: StatusCodes.Status400BadRequest);
			}

			parsedSubjectType = value;
		}

		if (from is { } fromValue && to is { } toValue && fromValue > toValue)
		{
			return Results.Problem(
				"The 'from' bound must not be later than the 'to' bound.",
				statusCode: StatusCodes.Status400BadRequest);
		}

		var query = new ListAuditLogsQuery(
			pageNumber,
			pageSize,
			parsedActionType,
			parsedSubjectType,
			actorUserId,
			from,
			to,
			oldestFirst ?? false);

		var result = await sender.Send(query, cancellationToken);

		return Results.Ok(result);
	}
}
