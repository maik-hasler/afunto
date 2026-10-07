using System.Globalization;
using Application.Common.Email;
using Domain.Organizations;

namespace Application.Organizations;

// Shared by CreateInvitation and ResendInvitation so both send the identical email.
internal static class InvitationEmail
{
	public static EmailDraft Draft(
		string language,
		string inviteeName,
		string organizationName,
		IEmailLinkBuilder emailLinkBuilder) =>
		new(EmailTemplateKind.InvitationReceived, language, inviteeName, emailLinkBuilder.MySignUps())
		{
			Placeholders = new Dictionary<string, string>
			{
				["OrganizationName"] = organizationName,
				["ExpiryDays"] = OrganizationInvitation.ExpiryWindowDays.ToString(CultureInfo.InvariantCulture),
			},
		};
}
