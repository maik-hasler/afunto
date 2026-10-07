using Application.Common.Exceptions;
using Application.Common.Persistence;
using Application.Common.Storage;
using Application.Engagements;
using Domain.Notifications;
using Domain.Reports;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.Extensions.Logging;

namespace Application.VolunteerOpportunities.Common;

internal static class VolunteerOpportunityDeletionHelper
{
	public static async Task DeleteAsync(
		IApplicationDbContext dbContext,
		IEngagementReadRepository engagementReadRepository,
		IFileStorageService fileStorage,
		VolunteerOpportunity opportunity,
		VolunteerOpportunityId opportunityId,
		UserId actingUserId,
		DateTimeOffset now,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		await ResolveEngagementsAndReportsAsync(
			dbContext, engagementReadRepository, opportunity, opportunityId, actingUserId, now, logger, cancellationToken);

		dbContext.VolunteerOpportunities.Delete(opportunity);

		await DeleteBannerAsync(fileStorage, opportunity, cancellationToken);
	}

	public static async Task ShadowDeleteAsync(
		IApplicationDbContext dbContext,
		IEngagementReadRepository engagementReadRepository,
		IFileStorageService fileStorage,
		VolunteerOpportunity opportunity,
		VolunteerOpportunityId opportunityId,
		UserId actingUserId,
		DateTimeOffset now,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		await ResolveEngagementsAndReportsAsync(
			dbContext, engagementReadRepository, opportunity, opportunityId, actingUserId, now, logger, cancellationToken);

		opportunity.MarkDeleted(now).ThrowIfFailure();

		await QuarantineBannerAsync(fileStorage, opportunity, cancellationToken);
	}

	private static async Task DeleteBannerAsync(
		IFileStorageService fileStorage, VolunteerOpportunity opportunity, CancellationToken cancellationToken)
	{
		var objectKey = opportunity.BannerImageUrl is not null
			? fileStorage.GetObjectKeyFromPublicUrl(opportunity.BannerImageUrl)
			: null;
		if (objectKey is null)
			return;

		try
		{
			await fileStorage.DeleteAsync(objectKey, cancellationToken);
		}
		catch
		{
			// Object may already be gone or storage may be transiently unavailable; continue.
		}
	}

	private static async Task QuarantineBannerAsync(
		IFileStorageService fileStorage, VolunteerOpportunity opportunity, CancellationToken cancellationToken)
	{
		var objectKey = opportunity.BannerImageUrl is not null
			? fileStorage.GetObjectKeyFromPublicUrl(opportunity.BannerImageUrl)
			: null;
		if (objectKey is null)
			return;

		try
		{
			await fileStorage.QuarantineAsync(objectKey, cancellationToken);
		}
		catch
		{
			// Object may already be gone, already quarantined, or storage may be
			// transiently unavailable; continue - the DB-level shadow delete is
			// what actually hides the opportunity from all read paths.
		}
	}

	private static async Task ResolveEngagementsAndReportsAsync(
		IApplicationDbContext dbContext,
		IEngagementReadRepository engagementReadRepository,
		VolunteerOpportunity opportunity,
		VolunteerOpportunityId opportunityId,
		UserId actingUserId,
		DateTimeOffset now,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		await VolunteerOpportunityEngagementCascadeHelper.NotifyAndCancelActiveEngagementsAsync(
			dbContext,
			engagementReadRepository,
			opportunityId,
			opportunity.TitleDe,
			NotificationKind.OpportunityDeleted,
			engagementCancellationReason: null,

			notifyPerEngagement: true,
			logger,
			cancellationToken);

		var openReports = await dbContext.GetOpenReportsForTargetAsync(
			ReportTargetType.VolunteerOpportunity, opportunityId.Value, cancellationToken);
		foreach (var report in openReports)
		{
			report.MarkActioned(actingUserId, now).ThrowIfFailure();
		}
	}
}
