using Application.Common.Authorization;
using Application.Common.Email;
using Application.Common.Exceptions;
using Application.Common.Keycloak;
using Application.Common.Localization;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Domain.Notifications;
using Domain.Primitives;

namespace Application.Organizations.ResendInvitation.v1;

internal sealed class ResendInvitationCommandHandler(
	IApplicationDbContext dbContext,
	IUnitOfWork unitOfWork,
	IKeycloakUserService keycloakUserService,
	IEmailService emailService,
	IEmailTemplateRenderer emailTemplateRenderer,
	IEmailLinkBuilder emailLinkBuilder)
	: ICommandHandler<ResendInvitationCommand, bool>
{
	public async ValueTask<bool> Handle(
		ResendInvitationCommand request,
		CancellationToken cancellationToken = default)
	{
		await OwnershipGuard.EnsureIsOrganizerAsync(
			dbContext,
			request.OrganizationId.Value,
			request.RequestingUserId,
			cancellationToken);

		var invitation = await dbContext.OrganizationInvitations.FindAsync(request.InvitationId, cancellationToken)
			?? throw new ResultFailureException(Error.NotFound("OrganizationInvitation.NotFound", "Invitation not found."));

		if (invitation.OrganizationId != request.OrganizationId)
			throw new ResultFailureException(Error.Validation("OrganizationInvitation.WrongOrganization", "Invitation does not belong to this organization."));

		var organization = await dbContext.Organizations.FindAsync(invitation.OrganizationId, cancellationToken)
			?? throw new ResultFailureException(Error.NotFound("Organization.NotFound", "Organization not found."));

		var invitee = await keycloakUserService.GetUserAsync(invitation.InviteeId.Value, cancellationToken);

		invitation.Resend(DateTimeOffset.UtcNow).ThrowIfFailure();

		var notification = Notification.Create(
			invitation.InviteeId,
			NotificationKind.InvitationReceived,
			invitation.Id.Value);
		await dbContext.Notifications.AddAsync(notification, cancellationToken);

		await unitOfWork.SaveChangesAsync(cancellationToken);

		var inviteeUser = (await dbContext.GetOrCreateUsersAsync([invitation.InviteeId], cancellationToken))[0];
		var inviteeLanguage = SupportedLanguages.Resolve(inviteeUser.PreferredLanguage);

		var draft = InvitationEmail.Draft(
			inviteeLanguage,
			invitee.FirstName ?? invitee.Username,
			organization.Name,
			emailLinkBuilder);

		await emailService.SendAsync(
			new EmailMessage(invitee.Email, emailTemplateRenderer.Render(draft), invitation.Id.Value.ToString()),
			cancellationToken);

		return true;
	}
}
