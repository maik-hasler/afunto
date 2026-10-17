using Application.Common.Authorization;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Application.Engagements.Common;
using Domain.Engagements;
using Domain.Notifications;
using Domain.Primitives;
using Domain.VolunteerOpportunities;

namespace Application.Engagements.ProposeMeeting.v1;

internal sealed class ProposeMeetingCommandHandler(
	IApplicationDbContext dbContext,
	TimeProvider timeProvider)
	: ICommandHandler<ProposeMeetingCommand, MeetingId>
{
	public const int MaxParticipants = 50;

	public async ValueTask<MeetingId> Handle(
		ProposeMeetingCommand request,
		CancellationToken cancellationToken = default)
	{
		var opportunity = await dbContext.VolunteerOpportunities.FindAsync(request.OpportunityId, cancellationToken)
			?? throw new ResultFailureException(Error.NotFound("VolunteerOpportunity.NotFound", $"Volunteer opportunity '{request.OpportunityId.Value}' not found."));

		await OwnershipGuard.EnsureIsOrganizerAsync(
			dbContext,
			opportunity.OrganizationId.Value,
			request.RequestingUserId,
			cancellationToken);

		if (opportunity.ParticipationType != ParticipationType.IndividualContact)
			throw new ResultFailureException(Error.Validation("Meeting.RequiresIndividualContact", "Meetings can only be arranged for an expression of interest."));

		var requestedIds = request.EngagementIds.ToList();
		if (request.ReplacesMeetingId is MeetingId replacedId)
		{
			var carryingReplaced = (await dbContext.GetActiveEngagementsForOpportunityAsync(request.OpportunityId, cancellationToken))
				.Where(e => e.Meeting?.Id == replacedId)
				.Select(e => e.Id)
				.ToList();

			if (carryingReplaced.Count == 0)
				throw new ResultFailureException(Error.Conflict("Meeting.Superseded", "This meeting proposal is no longer current."));

			requestedIds.AddRange(carryingReplaced);
		}

		requestedIds = requestedIds.Distinct().ToList();
		if (requestedIds.Count == 0)
			throw new ResultFailureException(Error.Validation("Meeting.NoParticipants", "Choose at least one person to meet."));

		if (requestedIds.Count > MaxParticipants)
			throw new ResultFailureException(Error.Validation("Meeting.TooManyParticipants", $"A meeting can include at most {MaxParticipants} people."));

		var engagementsById = (await dbContext.GetEngagementsByIdsAsync(requestedIds, cancellationToken))
			.ToDictionary(e => e.Id);

		// All or nothing: a group meeting that silently left out one of the people the
		// organizer picked would be worse than no meeting, so the first failure aborts
		// the whole proposal (the transaction behavior rolls back what came before).
		var meetingId = MeetingId.New();
		var now = timeProvider.GetUtcNow();
		foreach (var engagementId in requestedIds)
		{
			if (!engagementsById.TryGetValue(engagementId, out var engagement)
				|| engagement.OpportunityId != request.OpportunityId)
				throw new ResultFailureException(Error.NotFound("Engagement.NotFound", $"Engagement '{engagementId.Value}' not found."));

			engagement.ProposeMeeting(
				meetingId,
				MeetingParty.Organization,
				request.StartsAt,
				request.EndsAt,
				request.Format,
				request.Location,
				request.Note,
				now).ThrowIfFailure();

			await MeetingNotifier.NotifyVolunteerAsync(dbContext, engagement, NotificationKind.MeetingProposed, cancellationToken);
		}

		return meetingId;
	}
}
