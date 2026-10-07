using Application.Common.Email;
using Domain.Organizations;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Infrastructure.Common;
using Microsoft.Extensions.Options;

namespace Infrastructure.Email;

internal sealed class EmailLinkBuilder(
	IOptions<ApiOptions> options)
	: IEmailLinkBuilder
{
	private string FrontendBaseUrl => options.Value.FrontendBaseUrl.TrimEnd('/');

	public string MySignUps() =>
		$"{FrontendBaseUrl}/my-signups";

	public string VolunteerOpportunity(VolunteerOpportunityId opportunityId) =>
		$"{FrontendBaseUrl}/volunteer-opportunities/{opportunityId.Value}";

	public string OrganizationEngagements(OrganizationId organizationId) =>
		$"{FrontendBaseUrl}/app/{organizationId.Value}/dashboard/engagements";

	public string Opportunities() =>
		$"{FrontendBaseUrl}/opportunities";

	public string Home() =>
		$"{FrontendBaseUrl}/";

	// The anchor is the id of NotificationPreferencesSection on the profile page.
	public string NotificationSettings() =>
		$"{FrontendBaseUrl}/profile#email-notifications";

	public string Unsubscribe(UserId userId, Guid unsubscribeToken, EmailNotificationType type) =>
		$"{FrontendBaseUrl}/unsubscribe?userId={userId.Value}&type={type}&token={unsubscribeToken}";
}
