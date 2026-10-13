using Application.Common.Authorization;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Domain.Notifications;
using Domain.Primitives;

namespace Application.Organizations.ResendInvitation.v1;

internal sealed class ResendInvitationCommandHandler(
	IApplicationDbContext dbContext,
	IUnitOfWork unitOfWork)
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

		invitation.Resend(DateTimeOffset.UtcNow).ThrowIfFailure();

		var notification = Notification.Create(
			invitation.InviteeId,
			NotificationKind.InvitationReceived,
			invitation.Id.Value);
		await dbContext.Notifications.AddAsync(notification, cancellationToken);

		await unitOfWork.SaveChangesAsync(cancellationToken);

		return true;
	}
}
