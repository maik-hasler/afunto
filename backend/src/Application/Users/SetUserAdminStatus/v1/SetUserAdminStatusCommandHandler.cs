using Application.Common.Exceptions;
using Application.Common.Keycloak;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Domain.AuditLogs;
using Domain.Primitives;
using Domain.Users;

namespace Application.Users.SetUserAdminStatus.v1;

internal sealed class SetUserAdminStatusCommandHandler(
	IKeycloakUserService keycloakUserService,
	IApplicationDbContext dbContext,
	TimeProvider timeProvider)
	: ICommandHandler<SetUserAdminStatusCommand, bool>
{
	public async ValueTask<bool> Handle(
		SetUserAdminStatusCommand request,
		CancellationToken cancellationToken = default)
	{
		if (await keycloakUserService.IsServiceAccountAsync(request.TargetUserId, cancellationToken))
			throw new ResultFailureException(Error.Forbidden(
				"Users.CannotModifyServiceAccount",
				"The backend's own service account cannot be promoted or demoted."));

		if (!request.IsAdmin && request.TargetUserId == request.ActingUserId)
			throw new ResultFailureException(Error.Conflict(
				"Users.CannotDemoteSelf",
				"You cannot remove your own admin access."));

		if (request.IsAdmin)
			await keycloakUserService.AssignAdminRoleAsync(request.TargetUserId, cancellationToken);
		else
			await keycloakUserService.RemoveAdminRoleAsync(request.TargetUserId, cancellationToken);

		var targetUser = await dbContext.GetOrCreateUserAsync(
			UserId.Create(request.TargetUserId).GetValueOrThrow(), cancellationToken);
		targetUser.RecordPermissionsChanged(timeProvider.GetUtcNow());

		var actingUserId = UserId.Create(request.ActingUserId).GetValueOrThrow();
		var auditLog = AuditLog.Create(
			actingUserId,
			request.IsAdmin ? AuditActionType.UserPromotedToAdmin : AuditActionType.UserDemotedFromAdmin,
			AuditSubjectType.User,
			request.TargetUserId);
		await dbContext.AuditLogs.AddAsync(auditLog, cancellationToken);

		return true;
	}
}
