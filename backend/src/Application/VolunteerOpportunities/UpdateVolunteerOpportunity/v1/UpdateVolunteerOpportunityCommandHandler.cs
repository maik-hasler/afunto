using Application.Common.Authorization;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Application.Engagements;
using Application.Notifications;
using Domain.Common;
using Domain.Notifications;
using Domain.Primitives;
using Domain.VolunteerOpportunities;

namespace Application.VolunteerOpportunities.UpdateVolunteerOpportunity.v1;

internal sealed class UpdateVolunteerOpportunityCommandHandler(
	IApplicationDbContext dbContext,
	IEngagementReadRepository engagementReadRepository,
	IPinGenerator pinGenerator)
	: ICommandHandler<UpdateVolunteerOpportunityCommand, bool>
{
	public async ValueTask<bool> Handle(
		UpdateVolunteerOpportunityCommand request,
		CancellationToken cancellationToken = default)
	{
		var opportunityId = VolunteerOpportunityId.Create(request.OpportunityId).GetValueOrThrow();

		var opportunity = await dbContext.VolunteerOpportunities.FindAsync(
			opportunityId, cancellationToken)
			?? throw new ResultFailureException(Error.NotFound("VolunteerOpportunity.NotFound", $"Volunteer opportunity '{request.OpportunityId}' not found."));

		await OwnershipGuard.EnsureIsOrganizerAsync(
			dbContext,
			opportunity.OrganizationId.Value,
			request.RequestingUserId,
			cancellationToken);

		if (request.ParticipationType != opportunity.ParticipationType)
		{
			var engagements = await engagementReadRepository.GetByOpportunityAsync(
				opportunityId, cancellationToken);

			if (engagements.Count > 0)
				throw new ResultFailureException(Error.Conflict(
					"VolunteerOpportunity.ParticipationTypeLocked",
					"ParticipationType cannot be changed while any engagement exists for this opportunity."));
		}

		var prevIsRemote = opportunity.IsRemote;
		var prevAddress = opportunity.Address;
		var prevOccurrence = opportunity.Occurrence;

		opportunity.Rename(request.TitleDe, request.TitleEn).ThrowIfFailure();
		opportunity.ChangeDescription(request.DescriptionDe, request.DescriptionEn).ThrowIfFailure();

		opportunity.Relocate(request.IsRemote, request.Address).ThrowIfFailure();
		opportunity.Reschedule(request.Occurrence);
		opportunity.Recategorize(request.Category, request.Tags).ThrowIfFailure();
		opportunity.ChangeCheckInMethod(request.CheckInMethod, pinGenerator, DateTimeOffset.UtcNow, request.CheckInPin).ThrowIfFailure();
		opportunity.SwitchParticipationType(request.ParticipationType);
		opportunity.SetValidUntil(request.ValidUntil, DateTimeOffset.UtcNow).ThrowIfFailure();

		var relocated =
			prevIsRemote != request.IsRemote ||
			AddressTextChanged(prevAddress, request.Address);
		var materialChanged = relocated || prevOccurrence != request.Occurrence;

		if (materialChanged)
		{
			await OpportunityNotificationHelper.NotifyActiveVolunteersAsync(
				dbContext,
				engagementReadRepository,
				opportunityId,
				NotificationKind.OpportunityUpdated,
				cancellationToken,
				opportunityTitle: opportunity.TitleDe);
		}

		// Email only for a new place: an occurrence switch leaves every booked slot exactly
		// where and when it was, so the bell notification above is enough (#2402).
		if (relocated)
			opportunity.NotifyVolunteersOfRelocation();

		return true;
	}

	private static bool AddressTextChanged(Address? prev, Address? next) =>
		prev?.Street != next?.Street ||
		prev?.HouseNumber != next?.HouseNumber ||
		prev?.ZipCode != next?.ZipCode ||
		prev?.City != next?.City;
}
