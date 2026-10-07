using Domain.Organizations;
using Domain.Users;
using Domain.VolunteerOpportunities;

namespace Application.Common.Email;

// Deep links into the frontend for email buttons and footers. Every email links back into
// the app instead of telling the reader to "check the app" (#2402).
public interface IEmailLinkBuilder
{
	string MySignUps();

	string VolunteerOpportunity(VolunteerOpportunityId opportunityId);

	string OrganizationEngagements(OrganizationId organizationId);

	string Opportunities();

	string Home();

	string NotificationSettings();

	string Unsubscribe(UserId userId, Guid unsubscribeToken, EmailNotificationType type);
}
