using AwesomeAssertions;
using Domain.Engagements;
using Domain.Organizations;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Infrastructure.Persistence.Outbox;

using CoreDomainEvent = Domain.Primitives.DomainEvent;
using CoreOrganizationId = Domain.Organizations.OrganizationId;

namespace IntegrationTests;

public class OutboxMessageSerializationTests
{
	[Test]
	[MethodDataSource(nameof(AllDomainEvents))]
	public void FromDomainEvent_ThenToDomainEvent_ShouldRoundTripEveryValueObjectId(
		CoreDomainEvent domainEvent)
	{
		var message = OutboxMessage.FromDomainEvent(domainEvent, DateTime.UtcNow);

		var roundTripped = message.ToDomainEvent();

		roundTripped.Should().Be(domainEvent);
	}

	[Test]
	public void AllDomainEvents_ShouldCoverEveryDomainEventSubclass()
	{
		var allSubclassNames = typeof(CoreDomainEvent).Assembly
			.GetTypes()
			.Where(t => t is { IsClass: true, IsAbstract: false } && typeof(CoreDomainEvent).IsAssignableFrom(t))
			.Select(t => t.FullName)
			.ToList();

		var coveredNames = AllDomainEvents().Select(e => e.GetType().FullName).ToList();

		coveredNames.Should().BeEquivalentTo(allSubclassNames,
			"every DomainEvent subclass must have a round-trip case in AllDomainEvents()");
	}

	public static IEnumerable<CoreDomainEvent> AllDomainEvents()
	{
		yield return new EngagementCancelledDomainEvent(
			EngagementId.New(), UserId.New(), VolunteerOpportunityId.New(), "Reason");
		yield return new EngagementCreatedDomainEvent(
			EngagementId.New(), UserId.New(), VolunteerOpportunityId.New());
		yield return new EngagementCheckInUndoneDomainEvent(
			EngagementId.New(), UserId.New(), VolunteerOpportunityId.New());
		yield return new EngagementCheckedInDomainEvent(
			EngagementId.New(), UserId.New(), VolunteerOpportunityId.New());
		yield return new EngagementConfirmedDomainEvent(
			EngagementId.New(), UserId.New(), VolunteerOpportunityId.New());
		yield return new EngagementFeedbackDeletedDomainEvent(
			EngagementId.New(), UserId.New(), VolunteerOpportunityId.New());
		yield return new EngagementFeedbackSubmittedDomainEvent(
			EngagementId.New(), UserId.New(), VolunteerOpportunityId.New(), 5);
		yield return new EngagementFeedbackUpdatedDomainEvent(
			EngagementId.New(), UserId.New(), VolunteerOpportunityId.New(), 5);
		yield return new EngagementReactivatedDomainEvent(
			EngagementId.New(), UserId.New(), VolunteerOpportunityId.New());
		yield return new EngagementReminderDueDomainEvent(
			EngagementId.New(), UserId.New(), VolunteerOpportunityId.New(), TimeSlotId.New());
		yield return new EngagementWithdrawnDomainEvent(
			EngagementId.New(), UserId.New(), VolunteerOpportunityId.New());
		yield return new OrganizationDeletedDomainEvent(CoreOrganizationId.New());
		yield return new OrganizationInvitationAcceptedDomainEvent(
			OrganizationInvitationId.New(), CoreOrganizationId.New(), UserId.New());
		yield return new OrganizationInvitationDeclinedDomainEvent(
			OrganizationInvitationId.New(), CoreOrganizationId.New(), UserId.New());
		yield return new UserAccountDeletedDomainEvent(UserId.New());
		yield return new VolunteerOpportunityCancelledDomainEvent(
			VolunteerOpportunityId.New(), CoreOrganizationId.New(), "Reason");
		yield return new VolunteerOpportunityGeocodingRequestedDomainEvent(
			VolunteerOpportunityId.New());
		yield return new VolunteerOpportunityPublishedDomainEvent(
			VolunteerOpportunityId.New(), CoreOrganizationId.New());
		yield return new VolunteerOpportunityUnpublishedDomainEvent(
			VolunteerOpportunityId.New(), CoreOrganizationId.New());
		yield return new VolunteerOpportunityUpdatedDomainEvent(
			VolunteerOpportunityId.New(), TimeSlotId: null);
	}
}
