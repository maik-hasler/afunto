using Application.Common.Exceptions;
using Application.Common.Keycloak;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Domain.Engagements;
using Domain.Notifications;
using Domain.Primitives;
using Domain.Users;
using Domain.VolunteerOpportunities;

namespace Application.Engagements.CreateEngagement.v1;

internal sealed class CreateEngagementCommandHandler(
	IApplicationDbContext dbContext,
	IKeycloakOrganizationService keycloakOrganizationService)
	: ICommandHandler<CreateEngagementCommand, Engagement>
{
	public async ValueTask<Engagement> Handle(
		CreateEngagementCommand request,
		CancellationToken cancellationToken = default)
	{
		var opportunity = await dbContext.VolunteerOpportunities.FindAsync(
			request.OpportunityId, cancellationToken);

		if (opportunity is null)
			throw new ResultFailureException(Error.NotFound("VolunteerOpportunity.NotFound", $"Volunteer opportunity with id '{request.OpportunityId.Value}' was not found."));

		if (opportunity.Status != OpportunityStatus.Published)
			throw new ResultFailureException(Error.Conflict("Engagement.OpportunityNotPublished", "Conflict: this opportunity is not open for sign-ups."));

		if (opportunity.ParticipationType == ParticipationType.IndividualContact
			&& opportunity.ValidUntil is { } validUntil
			&& validUntil <= DateTimeOffset.UtcNow)
			throw new ResultFailureException(Error.Conflict("Engagement.ApplicationDeadlinePassed", "Conflict: the application deadline for this opportunity has passed."));

		if (opportunity.ParticipationType == ParticipationType.ScheduledSlots && request.TimeSlotId is null)
			throw new ResultFailureException(Error.Validation("Engagement.TimeSlotRequired", "A time slot is required to sign up for this opportunity."));

		if (opportunity.ParticipationType == ParticipationType.IndividualContact && request.TimeSlotId is not null)
			throw new ResultFailureException(Error.Validation("Engagement.TimeSlotNotAllowed", "This opportunity does not use time slots."));

		var alreadySignedUp = await dbContext.HasEngagementAsync(
			request.VolunteerId, request.OpportunityId, request.TimeSlotId, cancellationToken);

		// HasEngagementAsync matches on the time slot too, so a slot sign-up only
		// clashes with the same slot - a second slot of the same opportunity is
		// fine. Saying "this opportunity" there was simply wrong (afunto#2323).
		if (alreadySignedUp)
			throw new ResultFailureException(request.TimeSlotId is not null
				? Error.Conflict("Engagement.AlreadySignedUpForTimeSlot", "Conflict: you are already signed up for this time slot.")
				: Error.Conflict("Engagement.AlreadySignedUp", "Conflict: you are already signed up for this opportunity."));

		TimeSlot? timeSlot = null;
		if (request.TimeSlotId is not null)
		{
			timeSlot = opportunity.TimeSlots.FirstOrDefault(ts => ts.Id == request.TimeSlotId);
			if (timeSlot is null)
				throw new ResultFailureException(Error.Validation("Engagement.TimeSlotNotInOpportunity", "The selected time slot does not belong to this opportunity."));

			if (timeSlot.EndDateTime <= DateTimeOffset.UtcNow)
				throw new ResultFailureException(Error.Conflict("Engagement.TimeSlotEnded", "Conflict: this time slot has already ended."));

			await dbContext.LockTimeSlotForUpdateAsync(request.TimeSlotId.Value, cancellationToken);

			var activeCount = await dbContext.CountActiveEngagementsForTimeSlotAsync(
				request.TimeSlotId.Value, cancellationToken);
			if (timeSlot.MaxParticipants is int max && activeCount >= max)
				throw new ResultFailureException(Error.Conflict("Engagement.TimeSlotFull", "Conflict: this time slot has reached its capacity and cannot accept more sign-ups."));
		}

		var existingTerminal = await dbContext.GetTerminalEngagementAsync(
			request.VolunteerId, request.OpportunityId, request.TimeSlotId, cancellationToken);

		Engagement engagement;
		if (existingTerminal is not null)
		{
			existingTerminal.Reactivate(request.TimeSlotId, request.Message, timeSlot?.StartDateTime, timeSlot?.EndDateTime).ThrowIfFailure();
			engagement = existingTerminal;
		}
		else
		{
			engagement = request.TimeSlotId is not null
				? Engagement.CreateSlotSignUp(request.OpportunityId, request.VolunteerId, request.TimeSlotId.Value, timeSlot?.StartDateTime, timeSlot?.EndDateTime)
				: Engagement.CreateIndividualContact(request.OpportunityId, request.VolunteerId, request.Message
					?? throw new ResultFailureException(Error.Validation("Engagement.MessageRequired", "Message is required for individual contact."))).GetValueOrThrow();

			await dbContext.Engagements.AddAsync(engagement, cancellationToken);
		}

		var members = await keycloakOrganizationService
			.GetMembersAsync(opportunity.OrganizationId.Value, cancellationToken);

		foreach (var organizer in members.Where(m => m.IsOrganisator))
		{
			var notification = Notification.Create(
				UserId.Create(organizer.UserId).GetValueOrThrow(),
				NotificationKind.EngagementCreated,
				engagement.Id.Value);

			await dbContext.Notifications.AddAsync(notification, cancellationToken);
		}

		return engagement;
	}
}
