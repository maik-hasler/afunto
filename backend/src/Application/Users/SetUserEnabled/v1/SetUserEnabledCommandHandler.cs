using Application.Common.Exceptions;
using Application.Common.Keycloak;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Domain.AuditLogs;
using Domain.Primitives;
using Domain.Users;

namespace Application.Users.SetUserEnabled.v1;

internal sealed class SetUserEnabledCommandHandler(
	IKeycloakUserService keycloakUserService,
	IApplicationDbContext dbContext,
	TimeProvider timeProvider)
	: ICommandHandler<SetUserEnabledCommand, bool>
{
	public async ValueTask<bool> Handle(
		SetUserEnabledCommand request,
		CancellationToken cancellationToken = default)
	{
		if (await keycloakUserService.IsServiceAccountAsync(request.TargetUserId, cancellationToken))
			throw new ResultFailureException(Error.Forbidden(
				"Users.CannotModifyServiceAccount",
				"The backend's own service account cannot be blocked or unblocked."));

		if (!request.Enabled && request.TargetUserId == request.ActingUserId)
			throw new ResultFailureException(Error.Conflict(
				"Users.CannotDisableSelf",
				"You cannot block your own account."));

		await keycloakUserService.SetUserEnabledAsync(request.TargetUserId, request.Enabled, cancellationToken);

		var targetUser = await dbContext.GetOrCreateUserAsync(
			UserId.Create(request.TargetUserId).GetValueOrThrow(), cancellationToken);
		targetUser.RecordPermissionsChanged(timeProvider.GetUtcNow());

		var actingUserId = UserId.Create(request.ActingUserId).GetValueOrThrow();
		var auditLog = AuditLog.Create(
			actingUserId,
			request.Enabled ? AuditActionType.UserEnabled : AuditActionType.UserDisabled,
			AuditSubjectType.User,
			request.TargetUserId);
		await dbContext.AuditLogs.AddAsync(auditLog, cancellationToken);

		return true;
	}
}
