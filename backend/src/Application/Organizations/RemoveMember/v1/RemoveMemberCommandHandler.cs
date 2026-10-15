using Application.Common.Authorization;
using Application.Common.Exceptions;
using Application.Common.Keycloak;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Domain.Organizations;
using Domain.Primitives;
using Domain.Users;

namespace Application.Organizations.RemoveMember.v1;

internal sealed class RemoveMemberCommandHandler(
	IApplicationDbContext dbContext,
	IKeycloakOrganizationService keycloakOrganizationService,
	TimeProvider timeProvider)
	: ICommandHandler<RemoveMemberCommand, bool>
{
	public async ValueTask<bool> Handle(
		RemoveMemberCommand request,
		CancellationToken cancellationToken = default)
	{
		var organizationId = OrganizationId.Create(request.OrganizationId).GetValueOrThrow();
		var userId = UserId.Create(request.UserId).GetValueOrThrow();

		if (userId == request.RequestingUserId)
		{
			await OwnershipGuard.EnsureIsMemberAsync(
				dbContext,
				request.OrganizationId,
				request.RequestingUserId,
				cancellationToken);
		}
		else
		{
			await OwnershipGuard.EnsureIsOrganizerAsync(
				dbContext,
				request.OrganizationId,
				request.RequestingUserId,
				cancellationToken);
		}

		var isOrganizer = await dbContext.IsOrganizerAsync(organizationId, userId, cancellationToken);

		if (isOrganizer)
		{
			var organizerCount = await dbContext.CountOrganizersAsync(organizationId, cancellationToken);

			if (organizerCount <= 1)
				throw new ResultFailureException(Error.Conflict(
					"Organization.SoleOrganizer",
					"Conflict: you are the only organizer of this organization. Delete the organization instead of leaving it."));
		}

		await keycloakOrganizationService.RemoveMemberAsync(
			request.OrganizationId, request.UserId, cancellationToken);

		await dbContext.RemoveMembershipAsync(
			organizationId,
			userId,
			cancellationToken);

		if (isOrganizer)
		{
			var remainingOrganizerOrgs = await dbContext.GetOrganizerOrganizationsAsync(userId, cancellationToken);

			if (remainingOrganizerOrgs.Count == 0)
			{
				await keycloakOrganizationService.RevokeOrganizerRoleAsync(userId.Value, cancellationToken);

				var removedUser = await dbContext.GetOrCreateUserAsync(userId, cancellationToken);
				removedUser.RecordPermissionsChanged(timeProvider.GetUtcNow());
			}
		}

		return true;
	}
}
