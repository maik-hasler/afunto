using Application.Common.Authorization;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Application.Engagements.CreateEngagement.v1;
using Application.Engagements.Common;
using Domain.Engagements;
using Domain.Primitives;

namespace Application.Engagements.AssignMemberToOpportunity.v1;

// An organizer hands an opportunity to one of the organization's own members: the member is
// signed up and the sign-up is confirmed in one step, so it shows up under their sign-ups and
// they are notified like for any other confirmation. Sign-up rules (capacity, deadline, one
// sign-up per slot) stay with CreateEngagementCommandHandler (afunto#2406).
internal sealed class AssignMemberToOpportunityCommandHandler(
	IApplicationDbContext dbContext,
	ISender sender,
	TimeProvider timeProvider)
	: ICommandHandler<AssignMemberToOpportunityCommand, Engagement>
{
	// IndividualContact sign-ups require a message; an assignment has no applicant to write one.
	private const string DefaultAssignmentMessage = "Assigned by an organizer.";

	public async ValueTask<Engagement> Handle(
		AssignMemberToOpportunityCommand request,
		CancellationToken cancellationToken = default)
	{
		var opportunity = await dbContext.VolunteerOpportunities.FindAsync(request.OpportunityId, cancellationToken)
			?? throw new ResultFailureException(Error.NotFound("VolunteerOpportunity.NotFound", $"Volunteer opportunity '{request.OpportunityId.Value}' not found."));

		await OwnershipGuard.EnsureIsOrganizerAsync(
			dbContext,
			opportunity.OrganizationId.Value,
			request.RequestingUserId,
			cancellationToken);

		if (!await dbContext.IsMemberAsync(opportunity.OrganizationId, request.MemberId, cancellationToken))
			throw new ResultFailureException(Error.Validation(
				"Engagement.AssigneeNotMember",
				"Only members of the organization can be assigned to its opportunities."));

		var engagement = await sender.Send(
			new CreateEngagementCommand(
				request.OpportunityId,
				request.MemberId,
				request.TimeSlotId,
				string.IsNullOrWhiteSpace(request.Message) ? DefaultAssignmentMessage : request.Message),
			cancellationToken);

		var confirmed = await EngagementConfirmationHelper.ConfirmAsync(
			dbContext, sender, engagement, timeProvider, cancellationToken);

		return confirmed.GetValueOrThrow();
	}
}
