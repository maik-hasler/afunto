using Application.Common.Exceptions;
using Application.Common.Persistence;
using Domain.Engagements;
using Domain.Notifications;
using Domain.Organizations;
using Domain.Users;

namespace Application.Engagements.Common;

/// <summary>
/// Tells the other side of a meeting what just happened. The notification points at
/// the engagement, not the meeting: a meeting is replaced or cancelled long before the
/// notification is read, the engagement stays.
/// </summary>
internal static class MeetingNotifier
{
	public static async Task NotifyVolunteerAsync(
		IApplicationDbContext dbContext,
		Engagement engagement,
		NotificationKind kind,
		CancellationToken cancellationToken)
	{
		if (engagement.VolunteerId is not UserId volunteerId)
			return;

		await dbContext.Notifications.AddAsync(
			Notification.Create(volunteerId, kind, engagement.Id.Value),
			cancellationToken);
	}

	public static async Task NotifyOrganizersAsync(
		IApplicationDbContext dbContext,
		OrganizationId organizationId,
		Engagement engagement,
		NotificationKind kind,
		CancellationToken cancellationToken)
	{
		var organizerIds = await dbContext.GetOrganizerUserIdsAsync(organizationId, cancellationToken);

		foreach (var organizerId in organizerIds)
		{
			await dbContext.Notifications.AddAsync(
				Notification.Create(UserId.Create(organizerId).GetValueOrThrow(), kind, engagement.Id.Value),
				cancellationToken);
		}
	}
}
