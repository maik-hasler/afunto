using Application.Common.Email;
using Application.Common.Keycloak;
using Application.Common.Localization;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Domain.Engagements;
using Domain.Users;
using Microsoft.Extensions.Logging;

namespace Application.Engagements.EngagementReminder.v1;

internal sealed class EngagementReminderDueHandler(
	IApplicationDbContext dbContext,
	IKeycloakUserService keycloakUserService,
	IEmailService emailService,
	IEmailTemplateRenderer emailTemplateRenderer,
	IEmailLinkBuilder emailLinkBuilder,
	ILogger<EngagementReminderDueHandler> logger)
	: INotificationHandler<EngagementReminderDueDomainEvent>
{
	public async Task Handle(
		EngagementReminderDueDomainEvent notification,
		CancellationToken cancellationToken)
	{
		var opportunity = await dbContext.VolunteerOpportunities.FindAsync(notification.OpportunityId, cancellationToken);
		var timeSlot = opportunity?.TimeSlots.FirstOrDefault(ts => ts.Id == notification.TimeSlotId);

		if (opportunity is null || timeSlot is null)
		{
			logger.LogWarning(
				"Skipping reminder for engagement {EngagementId}: opportunity {OpportunityId} or time slot {TimeSlotId} no longer exists",
				notification.EngagementId.Value,
				notification.OpportunityId.Value,
				notification.TimeSlotId.Value);
			return;
		}

		var volunteerUser = (await dbContext.GetOrCreateUsersAsync([notification.VolunteerId], cancellationToken))[0];

		if (!volunteerUser.IsSubscribedTo(EmailNotificationType.EngagementReminder))
		{
			logger.LogInformation(
				"Skipping reminder for engagement {EngagementId}: volunteer opted out of reminder emails",
				notification.EngagementId.Value);
			return;
		}

		var volunteer = await keycloakUserService.GetUserAsync(notification.VolunteerId.Value, cancellationToken);
		var language = SupportedLanguages.Resolve(volunteerUser.PreferredLanguage);

		var draft = new EmailDraft(
			EmailTemplateKind.EngagementReminder,
			language,
			volunteer.FirstName ?? volunteer.Username,
			emailLinkBuilder.MySignUps())
		{
			Placeholders = new Dictionary<string, string>
			{
				["OpportunityTitle"] = EmailFacts.OpportunityTitle(opportunity, language),
			},
			Facts = [EmailFacts.Schedule(timeSlot), EmailFacts.Location(opportunity)],
			UnsubscribeUrl = emailLinkBuilder.Unsubscribe(
				notification.VolunteerId, volunteerUser.UnsubscribeToken, EmailNotificationType.EngagementReminder),
		};

		var results = await emailService.SendBatchAsync(
			[new EmailMessage(volunteer.Email, emailTemplateRenderer.Render(draft), notification.EngagementId.Value.ToString())],
			cancellationToken);
		if (!results[0])
			throw new InvalidOperationException(
				$"Failed to send reminder email for engagement {notification.EngagementId.Value}");

		logger.LogInformation(
			"Sent reminder for engagement {EngagementId}",
			notification.EngagementId.Value);
	}
}
