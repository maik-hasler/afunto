using Application.Common.Exceptions;
using Application.Common.Persistence;
using Application.Common.Storage;
using Application.Engagements;
using Application.Organizations.AdminShadowDeleteOrganization.v1;
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

namespace Application.UnitTests.Organizations.AdminShadowDeleteOrganization;

public class AdminShadowDeleteOrganizationCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<Organization, OrganizationId> _organizationRepo =
		Substitute.For<IAggregateRepository<Organization, OrganizationId>>();
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
	private readonly AdminShadowDeleteOrganizationCommandHandler _sut;

	private static readonly Address DefaultAddress = Address.Create("Hauptstraße", "1", "12345", "Berlin").Value;
	private static readonly UserId DefaultAdminUserId = UserId.New();

	public AdminShadowDeleteOrganizationCommandHandlerTests()
	{
		_dbContext.Organizations.Returns(_organizationRepo);
		_dbContext.VolunteerOpportunities.Returns(_opportunityRepo);
		_dbContext.Notifications.Returns(_notifRepo);
		_dbContext.Reports.Returns(_reportRepo);
		_dbContext.AuditLogs.Returns(_auditLogRepo);
		_dbContext
			.GetOpportunitiesForOrganizationAsync(Arg.Any<OrganizationId>(), Arg.Any<CancellationToken>())
			.Returns(new List<VolunteerOpportunity>());
		_dbContext
			.GetActiveEngagementsForOpportunityAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<CancellationToken>())
			.Returns(new List<Engagement>());
		_dbContext
			.GetOpenReportsForTargetAsync(Arg.Any<ReportTargetType>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns(new List<Report>());
		_engagementReadRepository
			.GetActiveVolunteerIdsByOpportunityAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<TimeSlotId?>(), Arg.Any<CancellationToken>())
			.Returns([]);
		_sut = new AdminShadowDeleteOrganizationCommandHandler(
			_dbContext, _engagementReadRepository, _fileStorage, NullLogger<AdminShadowDeleteOrganizationCommandHandler>.Instance);
	}

	private static Organization CreateOrganization(Guid id) =>
		Organization.Create(OrganizationId.Create(id).GetValueOrThrow(), "Test Org").Value;

	private VolunteerOpportunity CreateOpportunity(OrganizationId orgId) =>
		VolunteerOpportunity.Create(orgId, "Titel", null, "Beschreibung", null, false, DefaultAddress, Occurrence.OneTime, ParticipationType.ScheduledSlots, CheckInMethod.None, _pinGenerator, status: OpportunityStatus.Draft).Value;

	[Test]
	public async Task Handle_ShouldShadowDeleteOrganization_EvenWithMultipleMembers(
		CancellationToken cancellationToken)
	{
		// Arrange

		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		_organizationRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);

		// Act
		var result = await _sut.Handle(new AdminShadowDeleteOrganizationCommand(orgId, DefaultAdminUserId), cancellationToken);

		// Assert

		result.Should().BeTrue();
		organization.IsDeleted.Should().BeTrue();
		_organizationRepo.DidNotReceive().Delete(Arg.Any<Organization>());
		await _auditLogRepo.Received(1).AddAsync(
			Arg.Is<AuditLog>(a => a!.ActorUserId == DefaultAdminUserId
				&& a.ActionType == AuditActionType.OrganizationShadowDeleted
				&& a.SubjectType == AuditSubjectType.Organization
				&& a.SubjectId == orgId),
			cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldCascadeShadowDeleteOpportunities_EvenWithFutureTimeSlotsOrActiveEngagements(
		CancellationToken cancellationToken)
	{
		// Arrange

		var orgId = Guid.NewGuid();
		var organizationId = OrganizationId.Create(orgId).GetValueOrThrow();
		var organization = CreateOrganization(orgId);
		_organizationRepo.FindAsync(organizationId, cancellationToken).Returns(organization);

		var opportunity = CreateOpportunity(organizationId);
		opportunity.AddTimeSlot(DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(2), 5, DateTimeOffset.UtcNow);
		_dbContext
			.GetOpportunitiesForOrganizationAsync(organizationId, cancellationToken)
			.Returns([opportunity]);

		// Act
		await _sut.Handle(new AdminShadowDeleteOrganizationCommand(orgId, DefaultAdminUserId), cancellationToken);

		// Assert
		opportunity.IsDeleted.Should().BeTrue();
		_opportunityRepo.DidNotReceive().Delete(Arg.Any<VolunteerOpportunity>());
	}

	[Test]
	public async Task Handle_ShouldMarkOpenReportsActioned_OnOrganizationAndItsOpportunities(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organizationId = OrganizationId.Create(orgId).GetValueOrThrow();
		var organization = CreateOrganization(orgId);
		_organizationRepo.FindAsync(organizationId, cancellationToken).Returns(organization);

		var opportunity = CreateOpportunity(organizationId);
		_dbContext
			.GetOpportunitiesForOrganizationAsync(organizationId, cancellationToken)
			.Returns([opportunity]);

		var orgReport = Report.Create(ReportTargetType.Organization, orgId, UserId.New(), ReportReason.Fraud, null).Value;
		_dbContext
			.GetOpenReportsForTargetAsync(ReportTargetType.Organization, orgId, cancellationToken)
			.Returns([orgReport]);

		var opportunityReport = Report.Create(ReportTargetType.VolunteerOpportunity, opportunity.Id.Value, UserId.New(), ReportReason.Spam, null).Value;
		_dbContext
			.GetOpenReportsForTargetAsync(ReportTargetType.VolunteerOpportunity, opportunity.Id.Value, cancellationToken)
			.Returns([opportunityReport]);

		// Act
		await _sut.Handle(new AdminShadowDeleteOrganizationCommand(orgId, DefaultAdminUserId), cancellationToken);

		// Assert
		orgReport.Status.Should().Be(ReportStatus.Actioned);
		opportunityReport.Status.Should().Be(ReportStatus.Actioned);
	}

	[Test]
	public async Task Handle_ShouldCancelAndRaiseEventCarryingTheOpportunityTitle_ForEachCascadedEngagement(
		CancellationToken cancellationToken)
	{
		// Arrange

		var orgId = Guid.NewGuid();
		var organizationId = OrganizationId.Create(orgId).GetValueOrThrow();
		var organization = CreateOrganization(orgId);
		_organizationRepo.FindAsync(organizationId, cancellationToken).Returns(organization);

		var timeSlotId = TimeSlotId.New();
		var opportunityA = CreateOpportunity(organizationId);
		var engagementA = Engagement.CreateSlotSignUp(opportunityA.Id, UserId.New(), timeSlotId);
		engagementA.Confirm();
		var opportunityB = CreateOpportunity(organizationId);
		var engagementB = Engagement.CreateSlotSignUp(opportunityB.Id, UserId.New(), timeSlotId);
		engagementB.Confirm();

		_dbContext
			.GetOpportunitiesForOrganizationAsync(organizationId, cancellationToken)
			.Returns([opportunityA, opportunityB]);
		_dbContext
			.GetActiveEngagementsForOpportunityAsync(opportunityA.Id, cancellationToken)
			.Returns([engagementA]);
		_dbContext
			.GetActiveEngagementsForOpportunityAsync(opportunityB.Id, cancellationToken)
			.Returns([engagementB]);

		// Act
		await _sut.Handle(new AdminShadowDeleteOrganizationCommand(orgId, DefaultAdminUserId), cancellationToken);

		// Assert
		engagementA.Status.Should().Be(EngagementStatus.Cancelled);
		engagementA.Events.Should().ContainSingle(e => e is EngagementCancelledDomainEvent)
			.Which.Should().BeOfType<EngagementCancelledDomainEvent>()
			.Which.OpportunityTitle.Should().Be("Titel");
		engagementB.Status.Should().Be(EngagementStatus.Cancelled);
		engagementB.Events.Should().ContainSingle(e => e is EngagementCancelledDomainEvent);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenOrganizationNotFound(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		_organizationRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns((Organization?)null);

		// Act
		Func<Task> act = async () => await _sut.Handle(new AdminShadowDeleteOrganizationCommand(orgId, DefaultAdminUserId), cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.NotFound);
	}

	[Test]
	public async Task Handle_ShouldQuarantineTheLogoObject_WhenOrganizationHasALogo(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		organization.SetLogoUrl("https://example.com/organization-logos/logo.png");
		_organizationRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/organization-logos/logo.png")
			.Returns($"organization-logos/{orgId}.png");

		// Act
		await _sut.Handle(new AdminShadowDeleteOrganizationCommand(orgId, DefaultAdminUserId), cancellationToken);

		// Assert
		await _fileStorage.Received(1).QuarantineAsync($"organization-logos/{orgId}.png", cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldNotAttemptQuarantine_WhenOrganizationHasNoLogo(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		_organizationRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);

		// Act
		await _sut.Handle(new AdminShadowDeleteOrganizationCommand(orgId, DefaultAdminUserId), cancellationToken);

		// Assert
		await _fileStorage.DidNotReceive().QuarantineAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldNotThrow_WhenQuarantiningTheLogoObjectFails(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		organization.SetLogoUrl("https://example.com/organization-logos/logo.png");
		_organizationRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/organization-logos/logo.png")
			.Returns($"organization-logos/{orgId}.png");
		_fileStorage
			.QuarantineAsync($"organization-logos/{orgId}.png", Arg.Any<CancellationToken>())
			.ThrowsAsync(new InvalidOperationException("Storage unavailable"));

		// Act
		Func<Task> act = async () => await _sut.Handle(new AdminShadowDeleteOrganizationCommand(orgId, DefaultAdminUserId), cancellationToken);

		// Assert
		await act.Should().NotThrowAsync();
	}

	[Test]
	public async Task Handle_ShouldQuarantineTheBannerObject_OfACascadedOpportunity_WhenItHasOne(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organizationId = OrganizationId.Create(orgId).GetValueOrThrow();
		var organization = CreateOrganization(orgId);
		_organizationRepo.FindAsync(organizationId, cancellationToken).Returns(organization);

		var opportunity = CreateOpportunity(organizationId);
		opportunity.SetBannerImageUrl("https://example.com/opportunity-banners/banner.png");
		_dbContext
			.GetOpportunitiesForOrganizationAsync(organizationId, cancellationToken)
			.Returns([opportunity]);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/opportunity-banners/banner.png")
			.Returns($"opportunity-banners/{opportunity.Id.Value}.png");

		// Act
		await _sut.Handle(new AdminShadowDeleteOrganizationCommand(orgId, DefaultAdminUserId), cancellationToken);

		// Assert
		await _fileStorage.Received(1).QuarantineAsync($"opportunity-banners/{opportunity.Id.Value}.png", cancellationToken);
	}
}
