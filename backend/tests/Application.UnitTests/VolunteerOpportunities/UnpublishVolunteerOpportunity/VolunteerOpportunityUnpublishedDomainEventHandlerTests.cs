using Application.Common.Persistence;
using Application.Engagements;
using Application.VolunteerOpportunities.UnpublishVolunteerOpportunity.v1;
using AwesomeAssertions;
using Domain.Common;
using Domain.Engagements;
using Domain.Notifications;
using Domain.Organizations;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Application.UnitTests.VolunteerOpportunities.UnpublishVolunteerOpportunity;

public class VolunteerOpportunityUnpublishedDomainEventHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
	private readonly IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId> _opportunityRepo =
		Substitute.For<IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId>>();
	private readonly IAggregateRepository<Notification, NotificationId> _notifRepo =
		Substitute.For<IAggregateRepository<Notification, NotificationId>>();
	private readonly IEngagementReadRepository _engagementReadRepository =
		Substitute.For<IEngagementReadRepository>();
	private readonly VolunteerOpportunityUnpublishedDomainEventHandler _sut;

	private static readonly Address DefaultAddress = Address.Create("Hauptstraße", "1", "12345", "Berlin").Value;
	private static readonly OrganizationId DefaultOrgId = OrganizationId.New();

	public VolunteerOpportunityUnpublishedDomainEventHandlerTests()
	{
		_dbContext.VolunteerOpportunities.Returns(_opportunityRepo);
		_dbContext.Notifications.Returns(_notifRepo);
		_dbContext
			.GetActiveEngagementsForOpportunityAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<CancellationToken>())
			.Returns(new List<Engagement>());
		_engagementReadRepository
			.GetActiveVolunteerIdsByOpportunityAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<TimeSlotId?>(), Arg.Any<CancellationToken>())
			.Returns([]);
		_sut = new VolunteerOpportunityUnpublishedDomainEventHandler(
			_dbContext, _unitOfWork, _engagementReadRepository,
			NullLogger<VolunteerOpportunityUnpublishedDomainEventHandler>.Instance);
	}

	private static VolunteerOpportunity CreatePublishedOpportunity() =>
		VolunteerOpportunity.Create(
			DefaultOrgId, "Titel", null, "Beschreibung", null, false, DefaultAddress, Occurrence.OneTime, ParticipationType.IndividualContact,
			CheckInMethod.None, Substitute.For<IPinGenerator>(), status: OpportunityStatus.Published,
			validUntil: DateTimeOffset.UtcNow.AddDays(30)).Value;

	[Test]
	public async Task Handle_ShouldNotifyActiveVolunteers_WithOpportunityUnpublishedKind(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreatePublishedOpportunity();
		var volunteerId = Guid.NewGuid();
		_opportunityRepo.FindAsync(opportunity.Id, cancellationToken).Returns(opportunity);
		_engagementReadRepository
			.GetActiveVolunteerIdsByOpportunityAsync(opportunity.Id, Arg.Any<TimeSlotId?>(), cancellationToken)
			.Returns([volunteerId]);

		var domainEvent = new VolunteerOpportunityUnpublishedDomainEvent(opportunity.Id, DefaultOrgId);

		// Act
		await _sut.Handle(domainEvent, cancellationToken);

		// Assert

		await _notifRepo.Received(1).AddAsync(
			Arg.Is<Notification>(n => n!.Kind == NotificationKind.OpportunityUnpublished
				&& n.RelatedEntityId == opportunity.Id.Value
				&& n.TitleSnapshot == "Titel"),
			cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldCancelActiveEngagements_WithoutACannedReason(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunity = CreatePublishedOpportunity();
		var timeSlotId = TimeSlotId.New();
		var pendingEngagement = Engagement.CreateSlotSignUp(opportunity.Id, UserId.New(), timeSlotId);
		_opportunityRepo.FindAsync(opportunity.Id, cancellationToken).Returns(opportunity);
		_dbContext
			.GetActiveEngagementsForOpportunityAsync(opportunity.Id, cancellationToken)
			.Returns([pendingEngagement]);

		var domainEvent = new VolunteerOpportunityUnpublishedDomainEvent(opportunity.Id, DefaultOrgId);

		// Act
		await _sut.Handle(domainEvent, cancellationToken);

		// Assert
		pendingEngagement.Status.Should().Be(EngagementStatus.Cancelled);
		pendingEngagement.CancellationReason.Should().BeNull();
	}

	[Test]
	public async Task Handle_ShouldNotThrow_WhenOpportunityNoLongerExists(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		_opportunityRepo.FindAsync(opportunityId, cancellationToken).Returns((VolunteerOpportunity?)null);
		var domainEvent = new VolunteerOpportunityUnpublishedDomainEvent(opportunityId, DefaultOrgId);

		// Act
		Func<Task> act = async () => await _sut.Handle(domainEvent, cancellationToken);

		// Assert
		await act.Should().NotThrowAsync();
		await _notifRepo.DidNotReceive().AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldSaveChanges_AfterCascade(
		CancellationToken cancellationToken)
	{
		// Arrange

		var opportunity = CreatePublishedOpportunity();
		_opportunityRepo.FindAsync(opportunity.Id, cancellationToken).Returns(opportunity);
		var domainEvent = new VolunteerOpportunityUnpublishedDomainEvent(opportunity.Id, DefaultOrgId);

		// Act
		await _sut.Handle(domainEvent, cancellationToken);

		// Assert
		await _unitOfWork.Received(1).SaveChangesAsync(cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldNotSaveChanges_WhenOpportunityNoLongerExists(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = VolunteerOpportunityId.New();
		_opportunityRepo.FindAsync(opportunityId, cancellationToken).Returns((VolunteerOpportunity?)null);
		var domainEvent = new VolunteerOpportunityUnpublishedDomainEvent(opportunityId, DefaultOrgId);

		// Act
		await _sut.Handle(domainEvent, cancellationToken);

		// Assert
		await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
	}
}
