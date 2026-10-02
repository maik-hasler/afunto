using Application.Common.Exceptions;
using Application.Common.Persistence;
using Application.Common.Storage;
using Application.Engagements;
using Application.VolunteerOpportunities.AdminShadowDeleteVolunteerOpportunity.v1;
using AwesomeAssertions;
using Domain.AuditLogs;
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

namespace Application.UnitTests.VolunteerOpportunities.AdminShadowDeleteVolunteerOpportunity;

public class AdminShadowDeleteVolunteerOpportunityCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId> _opportunityRepo =
		Substitute.For<IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId>>();
	private readonly IAggregateRepository<Notification, NotificationId> _notifRepo =
		Substitute.For<IAggregateRepository<Notification, NotificationId>>();
	private readonly IAggregateRepository<Report, ReportId> _reportRepo =
		Substitute.For<IAggregateRepository<Report, ReportId>>();
	private readonly IAggregateRepository<AuditLog, AuditLogId> _auditLogRepo =
		Substitute.For<IAggregateRepository<AuditLog, AuditLogId>>();
	private readonly IEngagementReadRepository _engagementReadRepository =
		Substitute.For<IEngagementReadRepository>();
	private readonly IFileStorageService _fileStorage = Substitute.For<IFileStorageService>();
	private readonly IPinGenerator _pinGenerator = Substitute.For<IPinGenerator>();
	private readonly AdminShadowDeleteVolunteerOpportunityCommandHandler _sut;

	private static readonly Address DefaultAddress = Address.Create("Hauptstraße", "1", "12345", "Berlin").Value;
	private static readonly OrganizationId DefaultOrgId = OrganizationId.New();
	private static readonly UserId DefaultAdminUserId = UserId.New();

	public AdminShadowDeleteVolunteerOpportunityCommandHandlerTests()
	{
		_dbContext.VolunteerOpportunities.Returns(_opportunityRepo);
		_dbContext.Notifications.Returns(_notifRepo);
		_dbContext.Reports.Returns(_reportRepo);
		_dbContext.AuditLogs.Returns(_auditLogRepo);
		_dbContext
			.GetActiveEngagementsForOpportunityAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<CancellationToken>())
			.Returns(new List<Engagement>());
		_dbContext
			.GetOpenReportsForTargetAsync(Arg.Any<ReportTargetType>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns(new List<Report>());
		_engagementReadRepository
			.GetActiveVolunteerIdsByOpportunityAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<TimeSlotId?>(), Arg.Any<CancellationToken>())
			.Returns([]);
		_sut = new AdminShadowDeleteVolunteerOpportunityCommandHandler(
			_dbContext, _engagementReadRepository, _fileStorage, NullLogger<AdminShadowDeleteVolunteerOpportunityCommandHandler>.Instance);
	}

	private VolunteerOpportunity CreateOpportunity() =>
		VolunteerOpportunity.Create(DefaultOrgId, "Titel", null, "Beschreibung", null, false, DefaultAddress, Occurrence.OneTime, ParticipationType.ScheduledSlots, CheckInMethod.None, _pinGenerator, status: OpportunityStatus.Draft).Value;

	[Test]
	public async Task Handle_ShouldShadowDeleteOpportunity_WithoutCheckingOwnership(
		CancellationToken cancellationToken)
	{
		// Arrange

		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		// Act
		var result = await _sut.Handle(new AdminShadowDeleteVolunteerOpportunityCommand(opportunityId, DefaultAdminUserId), cancellationToken);

		// Assert
		result.Should().BeTrue();
		opportunity.IsDeleted.Should().BeTrue();
		_opportunityRepo.DidNotReceive().Delete(Arg.Any<VolunteerOpportunity>());
		await _auditLogRepo.Received(1).AddAsync(
			Arg.Is<AuditLog>(a => a!.ActorUserId == DefaultAdminUserId
				&& a.ActionType == AuditActionType.VolunteerOpportunityShadowDeleted
				&& a.SubjectType == AuditSubjectType.VolunteerOpportunity
				&& a.SubjectId == opportunityId),
			cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldMarkOpenReportsActioned_WhenOpportunityShadowDeleted(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);
		var report = Report.Create(ReportTargetType.VolunteerOpportunity, opportunityId, UserId.New(), ReportReason.IllegalContent, null).Value;
		_dbContext
			.GetOpenReportsForTargetAsync(ReportTargetType.VolunteerOpportunity, opportunityId, cancellationToken)
			.Returns([report]);

		// Act
		await _sut.Handle(new AdminShadowDeleteVolunteerOpportunityCommand(opportunityId, DefaultAdminUserId), cancellationToken);

		// Assert
		report.Status.Should().Be(ReportStatus.Actioned);
		report.ResolvedByUserId.Should().Be(DefaultAdminUserId);
	}

	[Test]
	public async Task Handle_ShouldNotifyAndCancelEachVolunteer_WhenActiveEngagementsAutoCancelled(
		CancellationToken cancellationToken)
	{
		// Arrange

		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		var timeSlotId = TimeSlotId.New();
		var engagement = Engagement.CreateSlotSignUp(
			VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), UserId.New(), timeSlotId);
		engagement.Confirm();

		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);
		_dbContext
			.GetActiveEngagementsForOpportunityAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns([engagement]);

		// Act
		await _sut.Handle(new AdminShadowDeleteVolunteerOpportunityCommand(opportunityId, DefaultAdminUserId), cancellationToken);

		// Assert

		await _notifRepo.Received(1).AddAsync(
			Arg.Is<Notification>(n => n!.RecipientId == engagement.VolunteerId!.Value
				&& n.Kind == NotificationKind.EngagementCancelled
				&& n.RelatedEntityId == engagement.Id.Value
				&& n.TitleSnapshot == "Titel"),
			cancellationToken);
		engagement.Status.Should().Be(EngagementStatus.Cancelled);
		engagement.Events.Should().ContainSingle(e => e is EngagementCancelledDomainEvent)
			.Which.Should().BeOfType<EngagementCancelledDomainEvent>()
			.Which.OpportunityTitle.Should().Be("Titel");
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
		Func<Task> act = async () => await _sut.Handle(new AdminShadowDeleteVolunteerOpportunityCommand(opportunityId, DefaultAdminUserId), cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.NotFound);
	}

	[Test]
	public async Task Handle_ShouldQuarantineTheBannerObject_WhenOpportunityHasABanner(
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
		await _sut.Handle(new AdminShadowDeleteVolunteerOpportunityCommand(opportunityId, DefaultAdminUserId), cancellationToken);

		// Assert
		await _fileStorage.Received(1).QuarantineAsync($"opportunity-banners/{opportunityId}.png", cancellationToken);
		await _fileStorage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldNotAttemptQuarantine_WhenOpportunityHasNoBanner(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		// Act
		await _sut.Handle(new AdminShadowDeleteVolunteerOpportunityCommand(opportunityId, DefaultAdminUserId), cancellationToken);

		// Assert
		await _fileStorage.DidNotReceive().QuarantineAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldNotThrow_WhenQuarantiningTheBannerObjectFails(
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
			.QuarantineAsync($"opportunity-banners/{opportunityId}.png", Arg.Any<CancellationToken>())
			.ThrowsAsync(new InvalidOperationException("Storage unavailable"));

		// Act
		Func<Task> act = async () => await _sut.Handle(new AdminShadowDeleteVolunteerOpportunityCommand(opportunityId, DefaultAdminUserId), cancellationToken);

		// Assert
		await act.Should().NotThrowAsync();
	}
}
