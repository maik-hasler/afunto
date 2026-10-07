using Application.Common.Email;
using Application.Common.Keycloak;
using Application.Common.Localization;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Domain.Engagements;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.Extensions.Logging;

namespace Application.Engagements.ConfirmEngagement.v1;

// One email per volunteer and opportunity, not per engagement: the first event of a bulk
// confirmation claims every confirmed-but-unnotified engagement of the same volunteer on
// the same opportunity, and the sibling events find nothing left to claim (#2402).
internal sealed class EngagementConfirmedNotificationHandler(
	IApplicationDbContext dbContext,
	IKeycloakUserService keycloakUserService,
	IEmailService emailService,
	IEmailTemplateRenderer emailTemplateRenderer,
	IEmailLinkBuilder emailLinkBuilder,
	TimeProvider timeProvider,
	ILogger<EngagementConfirmedNotificationHandler> logger)
	: INotificationHandler<EngagementConfirmedDomainEvent>
{
	public async Task Handle(
		EngagementConfirmedDomainEvent notification,
		CancellationToken cancellationToken)
	{
		var opportunity = await dbContext.VolunteerOpportunities.FindAsync(notification.OpportunityId, cancellationToken);
		if (opportunity is null)
		{
			logger.LogWarning(
				"Skipping confirmation email for engagement {EngagementId}: opportunity {OpportunityId} no longer exists",
				notification.EngagementId.Value,
				notification.OpportunityId.Value);
			return;
		}

		var now = timeProvider.GetUtcNow();
		var claimed = await dbContext.ClaimStatusNotificationsAsync(
			notification.VolunteerId, notification.OpportunityId, EngagementStatus.Confirmed, now, cancellationToken);
		if (claimed.Count == 0)
			return;

		try
		{
			var volunteerUser = (await dbContext.GetOrCreateUsersAsync([notification.VolunteerId], cancellationToken))[0];
			if (!volunteerUser.IsSubscribedTo(EmailNotificationType.EngagementConfirmed))
				return;

			var volunteer = await keycloakUserService.GetUserAsync(notification.VolunteerId.Value, cancellationToken);
			var language = SupportedLanguages.Resolve(volunteerUser.PreferredLanguage);
			var schedule = EmailFacts.Schedule(claimed, opportunity);

			var draft = new EmailDraft(
				schedule is null ? EmailTemplateKind.InterestConfirmed : EmailTemplateKind.EngagementConfirmed,
				language,
				volunteer.FirstName ?? volunteer.Username,
				emailLinkBuilder.MySignUps())
			{
				Placeholders = new Dictionary<string, string>
				{
					["OpportunityTitle"] = EmailFacts.OpportunityTitle(opportunity, language),
				},
				Count = claimed.Count,
				Facts = schedule is null ? [] : [schedule, EmailFacts.Location(opportunity)],
				UnsubscribeUrl = emailLinkBuilder.Unsubscribe(
					notification.VolunteerId, volunteerUser.UnsubscribeToken, EmailNotificationType.EngagementConfirmed),
			};

			await emailService.SendAsync(
				new EmailMessage(volunteer.Email, emailTemplateRenderer.Render(draft), notification.EngagementId.Value.ToString()),
				cancellationToken);
		}
		catch
		{
			await dbContext.ReleaseStatusNotificationsAsync([.. claimed.Select(e => e.Id)], CancellationToken.None);
			throw;
		}

		await SuppressRedundantRemindersAsync(claimed, opportunity.TimeSlots, now, cancellationToken);
	}

	// A confirmation that lands inside the reminder window already names the date and
	// place - the reminder job would only repeat it within the hour.
	private async Task SuppressRedundantRemindersAsync(
		IReadOnlyCollection<Engagement> confirmed,
		IReadOnlyCollection<TimeSlot> timeSlots,
		DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		var reminderDue = now + Engagement.ReminderLeadTime;
		var startsSoon = confirmed
			.Where(engagement => timeSlots.Any(ts => ts.Id == engagement.TimeSlotId && ts.StartDateTime <= reminderDue))
			.Select(engagement => engagement.Id)
			.ToList();

		if (startsSoon.Count > 0)
			await dbContext.MarkRemindersSentAsync(startsSoon, now, cancellationToken);
	}
}
