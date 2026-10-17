using Application.Common.Messaging;
using Domain.Engagements;
using Domain.Users;
using Domain.VolunteerOpportunities;

namespace Application.Engagements.AssignMemberToOpportunity.v1;

public sealed record AssignMemberToOpportunityCommand(
	VolunteerOpportunityId OpportunityId,
	UserId MemberId,
	TimeSlotId? TimeSlotId,
	string? Message,
	UserId RequestingUserId)
	: ICommand<Engagement>;
