using Domain.Primitives;
using Domain.Users;
using Domain.VolunteerOpportunities;

namespace Domain.Engagements;

public sealed record EngagementReactivatedDomainEvent(
	EngagementId EngagementId,
	UserId VolunteerId,
	VolunteerOpportunityId OpportunityId)
	: DomainEvent;
