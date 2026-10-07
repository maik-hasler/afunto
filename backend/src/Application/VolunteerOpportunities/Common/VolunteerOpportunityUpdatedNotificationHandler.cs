using Application.Common.Email;
using Application.Common.Exceptions;
using Application.Common.Keycloak;
using Application.Common.Localization;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Application.Engagements;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.Extensions.Logging;

namespace Application.VolunteerOpportunities.Common;

// Raised only for a new time or a new place (see VolunteerOpportunity.NotifyVolunteersOf*),
// so - like a cancellation - there is no opt-out: it decides whether someone turns up at
// the right place at the right time. The email names the new value instead of sending the
// reader to the app to find out what changed (#2402).
internal sealed class VolunteerOpportunityUpdatedNotificationHandler(
	IApplicationDbContext dbContext,
	IEngagementReadRepository engagementReadRepository,
	IKeycloakUserService keycloakUserService,
	IEmailService emailService,
	IEmailTemplateRenderer emailTemplateRenderer,
	IEmailLinkBuilder emailLinkBuilder,
	ILogger<VolunteerOpportunityUpdatedNotificationHandler> logger)
	: INotificationHandler<VolunteerOpportunityUpdatedDomainEvent>
{
	public async Task Handle(
		VolunteerOpportunityUpdatedDomainEvent notification,
		CancellationToken cancellationToken)
	{
		var opportunity = await dbContext.VolunteerOpportunities.FindAsync(
			notification.OpportunityId, cancellationToken);
		if (opportunity is null)
		{
			logger.LogWarning(
				"Skipping opportunity-updated email for opportunity {OpportunityId}: it no longer exists",
				notification.OpportunityId.Value);
			return;
		}

		TimeSlot? rescheduledSlot = null;
		if (notification.TimeSlotId is { } timeSlotId)
		{
			rescheduledSlot = opportunity.TimeSlots.FirstOrDefault(ts => ts.Id == timeSlotId);
			if (rescheduledSlot is null)
			{
				logger.LogWarning(
					"Skipping rescheduled email for opportunity {OpportunityId}: time slot {TimeSlotId} no longer exists",
					notification.OpportunityId.Value,
					timeSlotId.Value);
				return;
			}
		}

		var volunteerIds = await engagementReadRepository.GetActiveVolunteerIdsByOpportunityAsync(
			notification.OpportunityId, notification.TimeSlotId, cancellationToken);

		if (volunteerIds.Count == 0)
			return;

		var volunteerUserIds = volunteerIds.Select(id => UserId.Create(id).GetValueOrThrow()).ToList();
		var volunteerUsersById = (await dbContext.GetOrCreateUsersAsync(volunteerUserIds, cancellationToken))
			.ToDictionary(u => u.Id);

		var profileMap = await keycloakUserService.GetUserProfilesAsync(volunteerIds, cancellationToken);

		var (kind, actionUrl, changedFact) = rescheduledSlot is null
			? (EmailTemplateKind.OpportunityLocationChanged, emailLinkBuilder.VolunteerOpportunity(opportunity.Id), (EmailFact)EmailFacts.Location(opportunity))
			: (EmailTemplateKind.TimeSlotRescheduled, emailLinkBuilder.MySignUps(), EmailFacts.Schedule(rescheduledSlot));

		var messages = new List<EmailMessage>(volunteerIds.Count);
		foreach (var volunteerId in volunteerIds)
		{
			if (!profileMap.TryGetValue(volunteerId, out var volunteer))
				continue;

			var volunteerUser = volunteerUsersById[UserId.Create(volunteerId).GetValueOrThrow()];
			var language = SupportedLanguages.Resolve(volunteerUser.PreferredLanguage);

			var draft = new EmailDraft(kind, language, volunteer.FirstName ?? volunteer.Username, actionUrl)
			{
				Placeholders = new Dictionary<string, string>
				{
					["OpportunityTitle"] = EmailFacts.OpportunityTitle(opportunity, language),
				},
				Facts = [changedFact],
			};

			messages.Add(new EmailMessage(volunteer.Email, emailTemplateRenderer.Render(draft), volunteerId.ToString()));
		}

		if (messages.Count > 0)
		{
			var results = await emailService.SendBatchAsync(messages, cancellationToken);
			var failedCount = results.Count(succeeded => !succeeded);
			if (failedCount > 0)
				throw new InvalidOperationException(
					$"Failed to send {failedCount} of {results.Count} opportunity notification email(s) for opportunity {notification.OpportunityId.Value}");
		}
	}
}
