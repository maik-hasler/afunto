using Application.Common.Authorization;
using Application.Common.Exceptions;
using Application.Common.Keycloak;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Domain.Organizations;
using Domain.Primitives;
using Domain.Users;

namespace Application.Organizations.ChangeMemberRole.v1;

internal sealed class ChangeMemberRoleCommandHandler(
	IApplicationDbContext dbContext,
	IKeycloakOrganizationService keycloakOrganizationService,
	TimeProvider timeProvider)
	: ICommandHandler<ChangeMemberRoleCommand, bool>
{
	public async ValueTask<bool> Handle(
		ChangeMemberRoleCommand request,
		CancellationToken cancellationToken = default)
	{
		await OwnershipGuard.EnsureIsOrganizerAsync(
			dbContext,
			request.OrganizationId.Value,
			request.RequestingUserId,
			cancellationToken);

		var membership = await dbContext.GetMembershipAsync(
			request.OrganizationId, request.TargetUserId, cancellationToken)
			?? throw new ResultFailureException(Error.NotFound(
				"OrganizationMembership.NotFound", "Membership not found."));

		var wasOrganizer = membership.Role == OrganizationMemberRole.Organizer;
		var demoting = wasOrganizer && request.Role != OrganizationMemberRole.Organizer;

		if (demoting)
		{
			var organizerCount = await dbContext.CountOrganizersAsync(request.OrganizationId, cancellationToken);

			if (organizerCount <= 1)
				throw new ResultFailureException(Error.Conflict(
					"Organization.SoleOrganizerDemote",
					"Conflict: you cannot demote the only organizer of this organization. Promote another member first, or delete the organization."));
		}

		membership.ChangeRole(request.Role).ThrowIfFailure();

		if (request.Role == OrganizationMemberRole.Organizer && !wasOrganizer)
		{
			await keycloakOrganizationService.AssignOrganizerRoleAsync(
				request.TargetUserId.Value, cancellationToken);
			await RecordPermissionsChangedAsync(request.TargetUserId, cancellationToken);
		}
		else if (demoting)
		{
			var remainingOrganizerOrgs = await dbContext.GetOrganizerOrganizationsAsync(
				request.TargetUserId, cancellationToken);
			var stillOrganizerElsewhere = remainingOrganizerOrgs.Any(o => o.Id != request.OrganizationId);

			// Still an organizer elsewhere: the realm role and so the token stay
			// the same, and the membership guard already enforces the demotion.
			if (!stillOrganizerElsewhere)
			{
				await keycloakOrganizationService.RevokeOrganizerRoleAsync(
					request.TargetUserId.Value, cancellationToken);
				await RecordPermissionsChangedAsync(request.TargetUserId, cancellationToken);
			}
		}

		return true;
	}

	private async Task RecordPermissionsChangedAsync(UserId targetUserId, CancellationToken cancellationToken)
	{
		var targetUser = await dbContext.GetOrCreateUserAsync(targetUserId, cancellationToken);
		targetUser.RecordPermissionsChanged(timeProvider.GetUtcNow());
	}
}
