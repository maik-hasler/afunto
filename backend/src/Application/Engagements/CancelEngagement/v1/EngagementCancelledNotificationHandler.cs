using Application.Common.Email;
using Application.Common.Keycloak;
using Application.Common.Localization;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Domain.Engagements;
using Microsoft.Extensions.Logging;

namespace Application.Engagements.CancelEngagement.v1;

// Always sent - a volunteer cannot opt out of cancellations, or they might turn up to a
// shift that no longer exists. Bundled like the confirmation email: a cancelled series or
// opportunity costs each volunteer one email, not one per date (#2402).
internal sealed class EngagementCancelledNotificationHandler(
	IApplicationDbContext dbContext,
	IKeycloakUserService keycloakUserService,
	IEmailService emailService,
	IEmailTemplateRenderer emailTemplateRenderer,
	IEmailLinkBuilder emailLinkBuilder,
	TimeProvider timeProvider,
	ILogger<EngagementCancelledNotificationHandler> logger)
	: INotificationHandler<EngagementCancelledDomainEvent>
{
	public async Task Handle(
		EngagementCancelledDomainEvent notification,
		CancellationToken cancellationToken)
	{
		// Null once the opportunity was deleted in the same transaction that cancelled the
		// engagement - the event's title snapshot covers that case.
		var opportunity = await dbContext.VolunteerOpportunities.FindAsync(notification.OpportunityId, cancellationToken);
		if (opportunity is null && notification.OpportunityTitle is null)
		{
			logger.LogWarning(
				"Skipping cancellation email for engagement {EngagementId}: opportunity title unavailable for {OpportunityId}",
				notification.EngagementId.Value,
				notification.OpportunityId.Value);
			return;
		}

		var claimed = await dbContext.ClaimStatusNotificationsAsync(
			notification.VolunteerId, notification.OpportunityId, EngagementStatus.Cancelled, timeProvider.GetUtcNow(), cancellationToken);
		if (claimed.Count == 0)
			return;

		try
		{
			var volunteerUser = (await dbContext.GetOrCreateUsersAsync([notification.VolunteerId], cancellationToken))[0];
			var volunteer = await keycloakUserService.GetUserAsync(notification.VolunteerId.Value, cancellationToken);
			var language = SupportedLanguages.Resolve(volunteerUser.PreferredLanguage);
			var schedule = EmailFacts.Schedule(claimed, opportunity);
			var facts = new List<EmailFact>();
			if (schedule is not null)
				facts.Add(schedule);
			facts.AddRange(claimed
				.Select(engagement => engagement.CancellationReason)
				.OfType<string>()
				.Where(reason => !string.IsNullOrWhiteSpace(reason))
				.Distinct()
				.Select(reason => new EmailFact.Reason(reason)));

			var draft = new EmailDraft(
				schedule is null ? EmailTemplateKind.InterestCancelled : EmailTemplateKind.EngagementCancelled,
				language,
				volunteer.FirstName ?? volunteer.Username,
				emailLinkBuilder.Opportunities())
			{
				Placeholders = new Dictionary<string, string>
				{
					["OpportunityTitle"] = opportunity is null
						? notification.OpportunityTitle!
						: EmailFacts.OpportunityTitle(opportunity, language),
				},
				Count = claimed.Count,
				Facts = facts,
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
	}
}
