using Application.Common.Exceptions;
using Application.Common.Persistence;
using Application.Common.Storage;
using Application.Engagements;
using Application.VolunteerOpportunities.DeleteVolunteerOpportunity.v1;
using AwesomeAssertions;
using Domain.Common;
using Domain.Engagements;
using Domain.Notifications;
using Domain.Organizations;
using Domain.Primitives;
using Domain.Reports;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Application.UnitTests.VolunteerOpportunities.DeleteVolunteerOpportunity;

public class DeleteVolunteerOpportunityCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId> _opportunityRepo =
		Substitute.For<IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId>>();
	private readonly IAggregateRepository<Notification, NotificationId> _notifRepo =
		Substitute.For<IAggregateRepository<Notification, NotificationId>>();
	private readonly IEngagementReadRepository _engagementReadRepository =
		Substitute.For<IEngagementReadRepository>();
	private readonly IFileStorageService _fileStorage = Substitute.For<IFileStorageService>();
	private readonly IPinGenerator _pinGenerator = Substitute.For<IPinGenerator>();
	private readonly DeleteVolunteerOpportunityCommandHandler _sut;

	private static readonly Address DefaultAddress = Address.Create("Hauptstraße", "1", "12345", "Berlin").Value;
	private static readonly OrganizationId DefaultOrgId = OrganizationId.New();
	private static readonly UserId DefaultRequestingUserId = UserId.New();

	public DeleteVolunteerOpportunityCommandHandlerTests()
	{
		_dbContext.VolunteerOpportunities.Returns(_opportunityRepo);
		_dbContext.Notifications.Returns(_notifRepo);
		_dbContext
			.GetActiveEngagementsForOpportunityAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<CancellationToken>())
			.Returns(new List<Engagement>());
		_dbContext
			.GetOpenReportsForTargetAsync(Arg.Any<ReportTargetType>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns(new List<Report>());
		_engagementReadRepository
			.GetActiveVolunteerIdsByOpportunityAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<TimeSlotId?>(), Arg.Any<CancellationToken>())
			.Returns([]);
		_dbContext
			.IsOrganizerAsync(Arg.Any<OrganizationId>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(true);
		_sut = new DeleteVolunteerOpportunityCommandHandler(
			_dbContext, _engagementReadRepository, _fileStorage, NullLogger<DeleteVolunteerOpportunityCommandHandler>.Instance);
	}

	private VolunteerOpportunity CreateOpportunity() =>
		VolunteerOpportunity.Create(DefaultOrgId, "Titel", null, "Beschreibung", null, false, DefaultAddress, Occurrence.OneTime, ParticipationType.ScheduledSlots, CheckInMethod.None, _pinGenerator, status: OpportunityStatus.Draft).Value;

	[Test]
	public async Task Handle_ShouldReturnTrue_WhenOpportunityExists(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		// Act
		var result = await _sut.Handle(new DeleteVolunteerOpportunityCommand(opportunityId, DefaultRequestingUserId), cancellationToken);

		// Assert
		result.Should().BeTrue();
	}

	[Test]
	public async Task Handle_ShouldCallDelete_OnRepository(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		// Act
		await _sut.Handle(new DeleteVolunteerOpportunityCommand(opportunityId, DefaultRequestingUserId), cancellationToken);

		// Assert
		_opportunityRepo.Received(1).Delete(opportunity);
	}

	[Test]
	public async Task Handle_ShouldNotifyActiveVolunteers_WhenOpportunityDeleted(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		var pendingVolunteer = Guid.NewGuid();
		var confirmedVolunteer = Guid.NewGuid();

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		_engagementReadRepository
			.GetActiveVolunteerIdsByOpportunityAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), Arg.Any<TimeSlotId?>(), cancellationToken)
			.Returns([pendingVolunteer, confirmedVolunteer]);

		// Act
		await _sut.Handle(new DeleteVolunteerOpportunityCommand(opportunityId, DefaultRequestingUserId), cancellationToken);

		// Assert

		await _notifRepo.Received(2).AddAsync(
			Arg.Is<Notification>(n => n!.Kind == NotificationKind.OpportunityDeleted
				&& n.RelatedEntityId == opportunityId
				&& n.TitleSnapshot == "Titel"),
			cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldCancelActiveEngagements_WhenOpportunityDeleted(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		var timeSlotId = TimeSlotId.New();
		var pendingEngagement = Engagement.CreateSlotSignUp(
			VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), UserId.New(), timeSlotId);
		var confirmedEngagement = Engagement.CreateSlotSignUp(
			VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), UserId.New(), timeSlotId);
		confirmedEngagement.Confirm();

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		_dbContext
			.GetActiveEngagementsForOpportunityAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns([pendingEngagement, confirmedEngagement]);

		// Act
		await _sut.Handle(new DeleteVolunteerOpportunityCommand(opportunityId, DefaultRequestingUserId), cancellationToken);

		// Assert
		pendingEngagement.Status.Should().Be(EngagementStatus.Cancelled);
		pendingEngagement.CancellationReason.Should().Be("Opportunity was deleted.");
		confirmedEngagement.Status.Should().Be(EngagementStatus.Cancelled);
		confirmedEngagement.CancellationReason.Should().Be("Opportunity was deleted.");
	}

	[Test]
	public async Task Handle_ShouldNotifyAndCancelEachVolunteer_WhenActiveEngagementsAutoCancelled(
		CancellationToken cancellationToken)
	{
		// Arrange

		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		var timeSlotId = TimeSlotId.New();
		var pendingEngagement = Engagement.CreateSlotSignUp(
			VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), UserId.New(), timeSlotId);
		var confirmedEngagement = Engagement.CreateSlotSignUp(
			VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), UserId.New(), timeSlotId);
		confirmedEngagement.Confirm();

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);
		_dbContext
			.GetActiveEngagementsForOpportunityAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns([pendingEngagement, confirmedEngagement]);

		// Act
		await _sut.Handle(new DeleteVolunteerOpportunityCommand(opportunityId, DefaultRequestingUserId), cancellationToken);

		// Assert
		await _notifRepo.Received(1).AddAsync(
			Arg.Is<Notification>(n => n!.RecipientId == pendingEngagement.VolunteerId!.Value
				&& n.Kind == NotificationKind.EngagementCancelled
				&& n.RelatedEntityId == pendingEngagement.Id.Value
				&& n.TitleSnapshot == "Titel"),
			cancellationToken);
		await _notifRepo.Received(1).AddAsync(
			Arg.Is<Notification>(n => n!.RecipientId == confirmedEngagement.VolunteerId!.Value
				&& n.Kind == NotificationKind.EngagementCancelled
				&& n.RelatedEntityId == confirmedEngagement.Id.Value
				&& n.TitleSnapshot == "Titel"),
			cancellationToken);
		pendingEngagement.Events.Should().ContainSingle(e => e is EngagementCancelledDomainEvent)
			.Which.Should().BeOfType<EngagementCancelledDomainEvent>()
			.Which.OpportunityTitle.Should().Be("Titel");
		confirmedEngagement.Events.Should().ContainSingle(e => e is EngagementCancelledDomainEvent);
	}

	[Test]
	public async Task Handle_ShouldNotNotify_WhenNoActiveEngagements(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		_engagementReadRepository
			.GetActiveVolunteerIdsByOpportunityAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), Arg.Any<TimeSlotId?>(), cancellationToken)
			.Returns([]);

		// Act
		await _sut.Handle(new DeleteVolunteerOpportunityCommand(opportunityId, DefaultRequestingUserId), cancellationToken);

		// Assert
		await _notifRepo.DidNotReceive().AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldMarkOpenReportsActioned_WhenOpportunityDeleted(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		var report = Report.Create(ReportTargetType.VolunteerOpportunity, opportunityId, UserId.New(), ReportReason.Spam, null).Value;

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		_dbContext
			.GetOpenReportsForTargetAsync(ReportTargetType.VolunteerOpportunity, opportunityId, cancellationToken)
			.Returns([report]);

		// Act
		await _sut.Handle(new DeleteVolunteerOpportunityCommand(opportunityId, DefaultRequestingUserId), cancellationToken);

		// Assert
		report.Status.Should().Be(ReportStatus.Actioned);
		report.ResolvedByUserId.Should().Be(DefaultRequestingUserId);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenOpportunityNotFound(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns((VolunteerOpportunity?)null);

		// Act
		Func<Task> act = async () => await _sut.Handle(new DeleteVolunteerOpportunityCommand(opportunityId, DefaultRequestingUserId), cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>()
			.WithMessage($"*{opportunityId}*");
	}

	[Test]
	public async Task Handle_ShouldNotCallDelete_WhenOpportunityNotFound(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns((VolunteerOpportunity?)null);

		// Act
		try { await _sut.Handle(new DeleteVolunteerOpportunityCommand(opportunityId, DefaultRequestingUserId), cancellationToken); }
		catch (ResultFailureException) { }

		// Assert
		_opportunityRepo.DidNotReceive().Delete(Arg.Any<VolunteerOpportunity>());
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenRequestingUserIsNotOrganizer(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);
		_dbContext
			.IsOrganizerAsync(Arg.Any<OrganizationId>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(false);

		// Act
		Func<Task> act = async () => await _sut.Handle(new DeleteVolunteerOpportunityCommand(opportunityId, DefaultRequestingUserId), cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.Forbidden);
		_opportunityRepo.DidNotReceive().Delete(Arg.Any<VolunteerOpportunity>());
	}

	[Test]
	public async Task Handle_ShouldDeleteTheBannerObject_WhenOpportunityHasABanner(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		opportunity.SetBannerImageUrl("https://example.com/opportunity-banners/banner.png");
		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/opportunity-banners/banner.png")
			.Returns($"opportunity-banners/{opportunityId}.png");

		// Act
		await _sut.Handle(new DeleteVolunteerOpportunityCommand(opportunityId, DefaultRequestingUserId), cancellationToken);

		// Assert
		await _fileStorage.Received(1).DeleteAsync($"opportunity-banners/{opportunityId}.png", cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldNotAttemptDeletion_WhenOpportunityHasNoBanner(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		// Act
		await _sut.Handle(new DeleteVolunteerOpportunityCommand(opportunityId, DefaultRequestingUserId), cancellationToken);

		// Assert
		await _fileStorage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldNotThrow_WhenDeletingTheBannerObjectFails(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		opportunity.SetBannerImageUrl("https://example.com/opportunity-banners/banner.png");
		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/opportunity-banners/banner.png")
			.Returns($"opportunity-banners/{opportunityId}.png");
		_fileStorage
			.DeleteAsync($"opportunity-banners/{opportunityId}.png", Arg.Any<CancellationToken>())
			.ThrowsAsync(new InvalidOperationException("Storage unavailable"));

		// Act
		Func<Task> act = async () => await _sut.Handle(new DeleteVolunteerOpportunityCommand(opportunityId, DefaultRequestingUserId), cancellationToken);

		// Assert
		await act.Should().NotThrowAsync();
	}
}
