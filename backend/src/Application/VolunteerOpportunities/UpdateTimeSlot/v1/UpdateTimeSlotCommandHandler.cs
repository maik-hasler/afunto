using Application.Common.Authorization;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Application.Engagements;
using Application.Notifications;
using Domain.Notifications;
using Domain.Primitives;
using Domain.VolunteerOpportunities;

namespace Application.VolunteerOpportunities.UpdateTimeSlot.v1;

internal sealed class UpdateTimeSlotCommandHandler(
	IApplicationDbContext dbContext,
	IEngagementReadRepository engagementReadRepository)
	: ICommandHandler<UpdateTimeSlotCommand, UpdateTimeSlotResult>
{
	public async ValueTask<UpdateTimeSlotResult> Handle(
		UpdateTimeSlotCommand request,
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

		var timeSlotId = TimeSlotId.Create(request.TimeSlotId).GetValueOrThrow();
		var targetSlot = opportunity.TimeSlots.FirstOrDefault(ts => ts.Id == timeSlotId)
			?? throw new ResultFailureException(Error.NotFound("VolunteerOpportunity.TimeSlotNotFound", $"Time slot with id '{request.TimeSlotId}' not found."));

		if (request.Scope == SeriesEditScope.Only)
			return await UpdateOnlyAsync(opportunity, opportunityId, timeSlotId, request, cancellationToken);

		return await UpdateSeriesCapacityAsync(opportunity, targetSlot, request, cancellationToken);
	}

	private async ValueTask<UpdateTimeSlotResult> UpdateOnlyAsync(
		VolunteerOpportunity opportunity,
		VolunteerOpportunityId opportunityId,
		TimeSlotId timeSlotId,
		UpdateTimeSlotCommand request,
		CancellationToken cancellationToken)
	{
		if (request.StartDateTime is null || request.EndDateTime is null)
			throw new ResultFailureException(Error.Validation(
				"VolunteerOpportunity.TimeSlotDatesRequired",
				"StartDateTime and EndDateTime are required when Scope is Only."));

		var activeCount = await dbContext.CountActiveEngagementsForTimeSlotAsync(timeSlotId, cancellationToken);
		if (request.MaxParticipants is int max && max < activeCount)
			throw new ResultFailureException(Error.Validation(
				"VolunteerOpportunity.TimeSlotCapacityBelowActive",
				$"Cannot reduce capacity below the current number of active sign-ups ({activeCount})."));

		var timeSlot = opportunity.TimeSlots.First(ts => ts.Id == timeSlotId);
		var rescheduled =
			timeSlot.StartDateTime != request.StartDateTime.Value ||
			timeSlot.EndDateTime != request.EndDateTime.Value;

		opportunity.UpdateTimeSlot(
			timeSlotId,
			request.StartDateTime.Value,
			request.EndDateTime.Value,
			request.MaxParticipants,
			DateTimeOffset.UtcNow).ThrowIfFailure();

		// A capacity-only edit changes nothing for anyone already signed up, so it notifies
		// nobody - neither by bell nor by email (#2402).
		if (rescheduled)
		{
			await OpportunityNotificationHelper.NotifyActiveVolunteersAsync(
				dbContext,
				engagementReadRepository,
				opportunityId,
				NotificationKind.OpportunityUpdated,
				cancellationToken,
				timeSlotId,
				opportunity.TitleDe);

			opportunity.NotifyVolunteersOfReschedule(timeSlotId);
		}

		return new UpdateTimeSlotResult(1, []);
	}

	private async ValueTask<UpdateTimeSlotResult> UpdateSeriesCapacityAsync(
		VolunteerOpportunity opportunity,
		TimeSlot targetSlot,
		UpdateTimeSlotCommand request,
		CancellationToken cancellationToken)
	{
		if (targetSlot.SeriesId is null)
			throw new ResultFailureException(Error.Validation(
				"VolunteerOpportunity.TimeSlotNotPartOfSeries",
				"This time slot is not part of a recurring series."));

		var now = DateTimeOffset.UtcNow;
		var affectedSlots = opportunity.TimeSlots
			.Where(ts => ts.SeriesId == targetSlot.SeriesId && ts.StartDateTime > now)
			.Where(ts => request.Scope == SeriesEditScope.EntireSeries || ts.StartDateTime >= targetSlot.StartDateTime)
			.OrderBy(ts => ts.StartDateTime)
			.ToList();

		var activeCountsBySlot = await dbContext.CountActiveEngagementsForTimeSlotsAsync(
			affectedSlots.Select(ts => ts.Id).ToList(), cancellationToken);

		var skipped = new List<Guid>();
		var updatedCount = 0;

		foreach (var slot in affectedSlots)
		{
			var activeCount = activeCountsBySlot.GetValueOrDefault(slot.Id);
			if (request.MaxParticipants < activeCount)
			{
				skipped.Add(slot.Id.Value);
				continue;
			}

			// Capacity only - nothing changes for volunteers already signed up, so no
			// notification (#2402).
			opportunity.UpdateTimeSlotCapacity(slot.Id, request.MaxParticipants).ThrowIfFailure();
			updatedCount++;
		}

		return new UpdateTimeSlotResult(updatedCount, skipped);
	}
}
