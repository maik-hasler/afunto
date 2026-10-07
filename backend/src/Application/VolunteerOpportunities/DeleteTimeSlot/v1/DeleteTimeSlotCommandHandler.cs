using Application.Common.Authorization;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Application.Engagements.Common;
using Domain.Primitives;
using Domain.VolunteerOpportunities;
using Microsoft.Extensions.Logging;

namespace Application.VolunteerOpportunities.DeleteTimeSlot.v1;

internal sealed class DeleteTimeSlotCommandHandler(
	IApplicationDbContext dbContext,
	ILogger<DeleteTimeSlotCommandHandler> logger)
	: ICommandHandler<DeleteTimeSlotCommand, DeleteTimeSlotResult>
{
	public async ValueTask<DeleteTimeSlotResult> Handle(
		DeleteTimeSlotCommand request,
		CancellationToken cancellationToken = default)
	{
		var opportunity = await dbContext.VolunteerOpportunities.FindAsync(
			VolunteerOpportunityId.Create(request.OpportunityId).GetValueOrThrow(), cancellationToken)
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
		{
			var activeCount = await dbContext.CountActiveEngagementsForTimeSlotAsync(timeSlotId, cancellationToken);
			if (activeCount > 0)
				throw new ResultFailureException(Error.Conflict(
					"VolunteerOpportunity.TimeSlotHasActiveEngagements",
					$"Cannot delete a time slot that has {activeCount} active sign-up(s). Cancel the affected engagements first."));

			opportunity.RemoveTimeSlot(timeSlotId).ThrowIfFailure();
			return new DeleteTimeSlotResult([request.TimeSlotId]);
		}

		return await DeleteSeriesAsync(opportunity, targetSlot, request.Scope, cancellationToken);
	}

	private async ValueTask<DeleteTimeSlotResult> DeleteSeriesAsync(
		VolunteerOpportunity opportunity,
		TimeSlot targetSlot,
		SeriesEditScope scope,
		CancellationToken cancellationToken)
	{
		if (targetSlot.SeriesId is null)
			throw new ResultFailureException(Error.Validation(
				"VolunteerOpportunity.TimeSlotNotPartOfSeries",
				"This time slot is not part of a recurring series."));

		var now = DateTimeOffset.UtcNow;
		var affectedSlots = opportunity.TimeSlots
			.Where(ts => ts.SeriesId == targetSlot.SeriesId && ts.StartDateTime > now)
			.Where(ts => scope == SeriesEditScope.EntireSeries || ts.StartDateTime >= targetSlot.StartDateTime)
			.OrderBy(ts => ts.StartDateTime)
			.ToList();

		var affectedIds = affectedSlots.Select(ts => ts.Id).ToList();

		var engagementsToCancel = await dbContext.GetActiveEngagementsForTimeSlotsAsync(affectedIds, cancellationToken);
		foreach (var engagement in engagementsToCancel)
		{
			await EngagementCancellationHelper.CancelAsync(
				dbContext,
				engagement,
				reason: null,
				opportunity.TitleDe,

				notifyVolunteer: true,
				logger,
				cancellationToken);
		}

		foreach (var slot in affectedSlots)
		{
			opportunity.RemoveTimeSlot(slot.Id).ThrowIfFailure();
		}

		return new DeleteTimeSlotResult(affectedIds.Select(id => id.Value).ToList());
	}
}
