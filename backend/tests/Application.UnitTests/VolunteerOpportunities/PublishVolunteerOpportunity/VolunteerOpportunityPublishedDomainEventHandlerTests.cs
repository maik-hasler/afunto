using Application.Common.Keycloak;
using Application.Common.Persistence;
using Application.VolunteerOpportunities.PublishVolunteerOpportunity.v1;
using AwesomeAssertions;
using Domain.Common;
using Domain.Notifications;
using Domain.Organizations;
using Domain.VolunteerOpportunities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Application.UnitTests.VolunteerOpportunities.PublishVolunteerOpportunity;

public class VolunteerOpportunityPublishedDomainEventHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
	private readonly IKeycloakOrganizationService _keycloakService =
		Substitute.For<IKeycloakOrganizationService>();
	private readonly IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId> _opportunityRepo =
		Substitute.For<IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId>>();
	private readonly IAggregateRepository<Notification, NotificationId> _notifRepo =
		Substitute.For<IAggregateRepository<Notification, NotificationId>>();
	private readonly VolunteerOpportunityPublishedDomainEventHandler _sut;

	private static readonly Address DefaultAddress = Address.Create("Hauptstraße", "1", "12345", "Berlin").Value;

	public VolunteerOpportunityPublishedDomainEventHandlerTests()
	{
		_dbContext.VolunteerOpportunities.Returns(_opportunityRepo);
		_dbContext.Notifications.Returns(_notifRepo);
		_sut = new VolunteerOpportunityPublishedDomainEventHandler(
			_dbContext, _unitOfWork, _keycloakService,
			NullLogger<VolunteerOpportunityPublishedDomainEventHandler>.Instance);
	}

	private static VolunteerOpportunity CreatePublishedOpportunity(bool membersOnly) =>
		VolunteerOpportunity.Create(
			OrganizationId.New(), "Titel", null, "Beschreibung", null, false, DefaultAddress, Occurrence.OneTime, ParticipationType.IndividualContact,
			CheckInMethod.None, Substitute.For<IPinGenerator>(), status: OpportunityStatus.Published,
			validUntil: DateTimeOffset.UtcNow.AddDays(30), membersOnly: membersOnly).Value;

	private static KeycloakOrganizationMember Member(Guid id, bool isOrganisator) =>
		new(id, "user", null, null, "user@example.com", isOrganisator);

	[Test]
	public async Task Handle_ShouldNotifyOnlyNonOrganizerMembers_WhenMembersOnly(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreatePublishedOpportunity(membersOnly: true);
		var memberId = Guid.NewGuid();
		_opportunityRepo.FindAsync(opportunity.Id, cancellationToken).Returns(opportunity);
		_keycloakService.GetMembersAsync(opportunity.OrganizationId.Value, cancellationToken)
			.Returns([Member(memberId, isOrganisator: false), Member(Guid.NewGuid(), isOrganisator: true)]);

		// Act
		await _sut.Handle(
			new VolunteerOpportunityPublishedDomainEvent(opportunity.Id, opportunity.OrganizationId),
			cancellationToken);

		// Assert
		await _notifRepo.Received(1).AddAsync(
			Arg.Is<Notification>(n =>
				n.Kind == NotificationKind.MembersOnlyOpportunityPublished &&
				n.RecipientId.Value == memberId &&
				n.RelatedEntityId == opportunity.Id.Value),
			cancellationToken);
		await _unitOfWork.Received(1).SaveChangesAsync(cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldNotNotify_WhenOpportunityIsPublic(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreatePublishedOpportunity(membersOnly: false);
		_opportunityRepo.FindAsync(opportunity.Id, cancellationToken).Returns(opportunity);

		// Act
		await _sut.Handle(
			new VolunteerOpportunityPublishedDomainEvent(opportunity.Id, opportunity.OrganizationId),
			cancellationToken);

		// Assert
		await _notifRepo.DidNotReceive().AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
	}
}
