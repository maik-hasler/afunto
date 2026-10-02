using Application.Common.Exceptions;
using Application.Common.Keycloak;
using Application.Common.Persistence;
using Application.Common.Storage;
using Application.Engagements;
using Application.Organizations.DeleteOrganization.v1;
using AwesomeAssertions;
using Domain.Organizations;
using Domain.Reports;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Application.UnitTests.Organizations.DeleteOrganization;

public class DeleteOrganizationCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<Organization, OrganizationId> _organizationRepo =
		Substitute.For<IAggregateRepository<Organization, OrganizationId>>();
	private readonly IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId> _opportunityRepo =
		Substitute.For<IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId>>();
	private readonly IKeycloakOrganizationService _keycloakService = Substitute.For<IKeycloakOrganizationService>();
	private readonly IEngagementReadRepository _engagementReadRepository = Substitute.For<IEngagementReadRepository>();
	private readonly IFileStorageService _fileStorage = Substitute.For<IFileStorageService>();
	private readonly IPinGenerator _pinGenerator = Substitute.For<IPinGenerator>();
	private readonly DeleteOrganizationCommandHandler _sut;

	private static readonly UserId DefaultRequestingUserId = UserId.New();

	public DeleteOrganizationCommandHandlerTests()
	{
		_dbContext.Organizations.Returns(_organizationRepo);
		_dbContext.VolunteerOpportunities.Returns(_opportunityRepo);
		_dbContext
			.GetBlockingOpportunitiesForOrganizationAsync(Arg.Any<OrganizationId>(), Arg.Any<CancellationToken>())
			.Returns(new List<VolunteerOpportunity>());
		_dbContext
			.GetOpportunitiesForOrganizationAsync(Arg.Any<OrganizationId>(), Arg.Any<CancellationToken>())
			.Returns(new List<VolunteerOpportunity>());
		_dbContext
			.GetOpenReportsForTargetAsync(Arg.Any<ReportTargetType>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns(new List<Report>());
		_dbContext
			.GetActiveEngagementsForOpportunityAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<CancellationToken>())
			.Returns(new List<Domain.Engagements.Engagement>());

		_dbContext
			.GetOrganizerOrganizationsAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(new List<Organization>());
		_engagementReadRepository
			.GetActiveVolunteerIdsByOpportunityAsync(Arg.Any<VolunteerOpportunityId>(), Arg.Any<TimeSlotId?>(), Arg.Any<CancellationToken>())
			.Returns(new List<Guid>());
		_sut = new DeleteOrganizationCommandHandler(
			_dbContext, _keycloakService, _engagementReadRepository, _fileStorage, NullLogger<DeleteOrganizationCommandHandler>.Instance);
	}

	private void AllowRequestingUserInOrg(Guid orgId) =>
		_dbContext
			.IsOrganizerAsync(OrganizationId.Create(orgId).GetValueOrThrow(), DefaultRequestingUserId, Arg.Any<CancellationToken>())
			.Returns(true);

	private void SetMembers(Guid orgId, params Guid[] memberIds) =>
		_keycloakService
			.GetMembersAsync(orgId, Arg.Any<CancellationToken>())
			.Returns((IReadOnlyList<KeycloakOrganizationMember>)memberIds
				.Select(id => new KeycloakOrganizationMember(id, "user", "First", "Last", "user@example.com", false))
				.ToList());

	private void SetRemainingOrganizerOrganizations(params Organization[] organizations) =>
		_dbContext
			.GetOrganizerOrganizationsAsync(DefaultRequestingUserId, Arg.Any<CancellationToken>())
			.Returns(organizations.ToList());

	private static Organization CreateOrganization(Guid id) =>
		Organization.Create(OrganizationId.Create(id).GetValueOrThrow(), "Test Org").Value;

	private VolunteerOpportunity CreateOpportunityWithFutureTimeSlot(OrganizationId orgId)
	{
		var opportunity = VolunteerOpportunity.Create(
			orgId, "Titel", null, "Beschreibung", null, true, null, Occurrence.OneTime, ParticipationType.ScheduledSlots, CheckInMethod.None, _pinGenerator, status: OpportunityStatus.Draft).Value;
		opportunity.AddTimeSlot(DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(2), 5, DateTimeOffset.UtcNow);
		return opportunity;
	}

	[Test]
	public async Task Handle_ShouldDeleteOrganizationAndRaiseDeletedDomainEvent_WhenSoleMemberAndNoBlockingOpportunities(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		_organizationRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);
		AllowRequestingUserInOrg(orgId);
		SetMembers(orgId, DefaultRequestingUserId.Value);
		var command = new DeleteOrganizationCommand(orgId, DefaultRequestingUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
		_organizationRepo.Received(1).Delete(organization);

		organization.Events.Should().ContainSingle(e => e is OrganizationDeletedDomainEvent);
		((OrganizationDeletedDomainEvent)organization.Events.Single()).OrganizationId.Should().Be(organization.Id);
		await _keycloakService.DidNotReceive().DeleteOrganizationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
		await _dbContext.Received(1).RemoveDashboardLayoutsForOrganizationAsync(
			OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldMarkOpenReportsActioned_WhenOrganizationDeleted(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		_organizationRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);
		AllowRequestingUserInOrg(orgId);
		SetMembers(orgId, DefaultRequestingUserId.Value);
		var report = Report.Create(ReportTargetType.Organization, orgId, UserId.New(), ReportReason.Fraud, null).Value;
		_dbContext
			.GetOpenReportsForTargetAsync(ReportTargetType.Organization, orgId, cancellationToken)
			.Returns([report]);
		var command = new DeleteOrganizationCommand(orgId, DefaultRequestingUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		report.Status.Should().Be(ReportStatus.Actioned);
		report.ResolvedByUserId.Should().Be(DefaultRequestingUserId);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenOrganizationNotFound(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		_organizationRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns((Organization?)null);
		var command = new DeleteOrganizationCommand(orgId, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>()
			.WithMessage($"*{orgId}*");
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenRequestingUserIsNotAMember(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		_organizationRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);
		_dbContext
			.IsOrganizerAsync(OrganizationId.Create(orgId).GetValueOrThrow(), DefaultRequestingUserId, Arg.Any<CancellationToken>())
			.Returns(false);
		var command = new DeleteOrganizationCommand(orgId, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>();
		_organizationRepo.DidNotReceive().Delete(Arg.Any<Organization>());
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenOtherMembersRemain(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		_organizationRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);
		AllowRequestingUserInOrg(orgId);
		SetMembers(orgId, DefaultRequestingUserId.Value, Guid.NewGuid());
		var command = new DeleteOrganizationCommand(orgId, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>()
			.WithMessage("*sole remaining member*");
		_organizationRepo.DidNotReceive().Delete(Arg.Any<Organization>());
		await _keycloakService.DidNotReceive().DeleteOrganizationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldDeleteTheOrganizationsFinishedOpportunities_SoNoneSurviveAsOrphanRows(
		CancellationToken cancellationToken)
	{
		// Arrange

		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		_organizationRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);
		AllowRequestingUserInOrg(orgId);
		SetMembers(orgId, DefaultRequestingUserId.Value);
		var organizationId = OrganizationId.Create(orgId).GetValueOrThrow();
		var finishedOpportunity = VolunteerOpportunity.Create(
			organizationId, "Finished Opportunity", null, "Beschreibung", null, true, null, Occurrence.OneTime,
			ParticipationType.IndividualContact, CheckInMethod.None, _pinGenerator,
			status: OpportunityStatus.Published, validUntil: DateTimeOffset.UtcNow.AddDays(30)).Value;
		_dbContext
			.GetOpportunitiesForOrganizationAsync(organizationId, cancellationToken)
			.Returns([finishedOpportunity]);
		var command = new DeleteOrganizationCommand(orgId, DefaultRequestingUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		_opportunityRepo.Received(1).Delete(finishedOpportunity);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenOpportunityHasFutureTimeSlotOrActiveEngagement(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		_organizationRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);
		AllowRequestingUserInOrg(orgId);
		SetMembers(orgId, DefaultRequestingUserId.Value);
		var blockingOpportunity = CreateOpportunityWithFutureTimeSlot(OrganizationId.Create(orgId).GetValueOrThrow());
		_dbContext
			.GetBlockingOpportunitiesForOrganizationAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken)
			.Returns([blockingOpportunity]);
		var command = new DeleteOrganizationCommand(orgId, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>()
			.WithMessage("*Titel*");
		_organizationRepo.DidNotReceive().Delete(Arg.Any<Organization>());
		await _keycloakService.DidNotReceive().DeleteOrganizationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldRevokeKeycloakRole_WhenRequestingUserHasNoRemainingOrganizations(
		CancellationToken cancellationToken)
	{
		// Arrange

		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		_organizationRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);
		AllowRequestingUserInOrg(orgId);
		SetMembers(orgId, DefaultRequestingUserId.Value);
		SetRemainingOrganizerOrganizations();
		var command = new DeleteOrganizationCommand(orgId, DefaultRequestingUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _keycloakService.Received(1).RevokeOrganizerRoleAsync(DefaultRequestingUserId.Value, cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldNotRevokeKeycloakRole_WhenRequestingUserStillOrganizesAnotherOrganization(
		CancellationToken cancellationToken)
	{
		// Arrange

		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		_organizationRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);
		AllowRequestingUserInOrg(orgId);
		SetMembers(orgId, DefaultRequestingUserId.Value);
		var otherOrg = Organization.Create(OrganizationId.New(), "Other Org").GetValueOrThrow();
		SetRemainingOrganizerOrganizations(otherOrg);
		var command = new DeleteOrganizationCommand(orgId, DefaultRequestingUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _keycloakService.DidNotReceive().RevokeOrganizerRoleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldDeleteTheLogoObject_WhenOrganizationHasALogo(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		organization.SetLogoUrl("https://example.com/organization-logos/logo.png");
		_organizationRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);
		AllowRequestingUserInOrg(orgId);
		SetMembers(orgId, DefaultRequestingUserId.Value);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/organization-logos/logo.png")
			.Returns($"organization-logos/{orgId}.png");
		var command = new DeleteOrganizationCommand(orgId, DefaultRequestingUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _fileStorage.Received(1).DeleteAsync($"organization-logos/{orgId}.png", cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldNotAttemptDeletion_WhenOrganizationHasNoLogo(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		_organizationRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);
		AllowRequestingUserInOrg(orgId);
		SetMembers(orgId, DefaultRequestingUserId.Value);
		var command = new DeleteOrganizationCommand(orgId, DefaultRequestingUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _fileStorage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldNotThrow_WhenDeletingTheLogoObjectFails(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		organization.SetLogoUrl("https://example.com/organization-logos/logo.png");
		_organizationRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);
		AllowRequestingUserInOrg(orgId);
		SetMembers(orgId, DefaultRequestingUserId.Value);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/organization-logos/logo.png")
			.Returns($"organization-logos/{orgId}.png");
		_fileStorage
			.DeleteAsync($"organization-logos/{orgId}.png", Arg.Any<CancellationToken>())
			.ThrowsAsync(new InvalidOperationException("Storage unavailable"));
		var command = new DeleteOrganizationCommand(orgId, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().NotThrowAsync();
	}

	[Test]
	public async Task Handle_ShouldDeleteTheBannerObject_OfACascadedOpportunity_WhenItHasOne(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		_organizationRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);
		AllowRequestingUserInOrg(orgId);
		SetMembers(orgId, DefaultRequestingUserId.Value);
		var organizationId = OrganizationId.Create(orgId).GetValueOrThrow();
		var finishedOpportunity = VolunteerOpportunity.Create(
			organizationId, "Finished Opportunity", null, "Beschreibung", null, true, null, Occurrence.OneTime,
			ParticipationType.IndividualContact, CheckInMethod.None, _pinGenerator,
			status: OpportunityStatus.Published, validUntil: DateTimeOffset.UtcNow.AddDays(30)).Value;
		finishedOpportunity.SetBannerImageUrl("https://example.com/opportunity-banners/banner.png");
		_dbContext
			.GetOpportunitiesForOrganizationAsync(organizationId, cancellationToken)
			.Returns([finishedOpportunity]);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/opportunity-banners/banner.png")
			.Returns($"opportunity-banners/{finishedOpportunity.Id.Value}.png");
		var command = new DeleteOrganizationCommand(orgId, DefaultRequestingUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _fileStorage.Received(1).DeleteAsync($"opportunity-banners/{finishedOpportunity.Id.Value}.png", cancellationToken);
	}
}
