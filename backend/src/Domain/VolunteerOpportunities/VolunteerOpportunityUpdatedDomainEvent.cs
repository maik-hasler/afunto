using Domain.Primitives;

namespace Domain.VolunteerOpportunities;

// TimeSlotId set: that slot was rescheduled. TimeSlotId null: the opportunity moved to a
// different place (or online). See VolunteerOpportunity.NotifyVolunteersOf*.
public sealed record VolunteerOpportunityUpdatedDomainEvent(
	VolunteerOpportunityId OpportunityId,
	TimeSlotId? TimeSlotId)
	: DomainEvent;
