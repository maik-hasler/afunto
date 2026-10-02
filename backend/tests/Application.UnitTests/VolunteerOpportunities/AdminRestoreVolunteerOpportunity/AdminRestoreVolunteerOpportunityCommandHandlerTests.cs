using Application.Common.Exceptions;
using Application.Common.Persistence;
using Application.Common.Storage;
using Application.VolunteerOpportunities.AdminRestoreVolunteerOpportunity.v1;
using AwesomeAssertions;
using Domain.AuditLogs;
using Domain.Common;
using Domain.Organizations;
using Domain.Primitives;
using Domain.Users;
using Domain.VolunteerOpportunities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Application.UnitTests.VolunteerOpportunities.AdminRestoreVolunteerOpportunity;

public class AdminRestoreVolunteerOpportunityCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<AuditLog, AuditLogId> _auditLogRepo =
		Substitute.For<IAggregateRepository<AuditLog, AuditLogId>>();
	private readonly IFileStorageService _fileStorage = Substitute.For<IFileStorageService>();
	private readonly IPinGenerator _pinGenerator = Substitute.For<IPinGenerator>();
	private readonly AdminRestoreVolunteerOpportunityCommandHandler _sut;

	private static readonly Address DefaultAddress = Address.Create("Hauptstraße", "1", "12345", "Berlin").Value;
	private static readonly OrganizationId DefaultOrgId = OrganizationId.New();
	private static readonly UserId DefaultAdminUserId = UserId.New();

	public AdminRestoreVolunteerOpportunityCommandHandlerTests()
	{
		_dbContext.AuditLogs.Returns(_auditLogRepo);
		_sut = new AdminRestoreVolunteerOpportunityCommandHandler(_dbContext, _fileStorage);
	}

	private VolunteerOpportunity CreateOpportunity() =>
		VolunteerOpportunity.Create(DefaultOrgId, "Titel", null, "Beschreibung", null, false, DefaultAddress, Occurrence.OneTime, ParticipationType.ScheduledSlots, CheckInMethod.None, _pinGenerator, status: OpportunityStatus.Draft).Value;

	[Test]
	public async Task Handle_ShouldRestoreOpportunity_AndWriteAuditLog(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		opportunity.MarkDeleted(DateTimeOffset.UtcNow);
		_dbContext
			.FindVolunteerOpportunityIncludingDeletedAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		// Act
		var result = await _sut.Handle(new AdminRestoreVolunteerOpportunityCommand(opportunityId, DefaultAdminUserId), cancellationToken);

		// Assert
		result.Should().BeTrue();
		opportunity.IsDeleted.Should().BeFalse();
		await _auditLogRepo.Received(1).AddAsync(
			Arg.Is<AuditLog>(a => a!.ActorUserId == DefaultAdminUserId
				&& a.ActionType == AuditActionType.VolunteerOpportunityRestored
				&& a.SubjectType == AuditSubjectType.VolunteerOpportunity
				&& a.SubjectId == opportunityId),
			cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenOpportunityNotFound(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		_dbContext
			.FindVolunteerOpportunityIncludingDeletedAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns((VolunteerOpportunity?)null);

		// Act
		Func<Task> act = async () => await _sut.Handle(new AdminRestoreVolunteerOpportunityCommand(opportunityId, DefaultAdminUserId), cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.NotFound);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenOpportunityNotShadowDeleted(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		_dbContext
			.FindVolunteerOpportunityIncludingDeletedAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		// Act
		Func<Task> act = async () => await _sut.Handle(new AdminRestoreVolunteerOpportunityCommand(opportunityId, DefaultAdminUserId), cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>().WithMessage("*not shadow-deleted*");
		await _auditLogRepo.DidNotReceive().AddAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldUnquarantineTheBannerObject_WhenOpportunityHasABanner(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		opportunity.SetBannerImageUrl("https://example.com/opportunity-banners/banner.png");
		opportunity.MarkDeleted(DateTimeOffset.UtcNow);
		_dbContext
			.FindVolunteerOpportunityIncludingDeletedAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/opportunity-banners/banner.png")
			.Returns($"opportunity-banners/{opportunityId}.png");

		// Act
		await _sut.Handle(new AdminRestoreVolunteerOpportunityCommand(opportunityId, DefaultAdminUserId), cancellationToken);

		// Assert
		await _fileStorage.Received(1).UnquarantineAsync($"opportunity-banners/{opportunityId}.png", cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldNotAttemptUnquarantine_WhenOpportunityHasNoBanner(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		opportunity.MarkDeleted(DateTimeOffset.UtcNow);
		_dbContext
			.FindVolunteerOpportunityIncludingDeletedAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		// Act
		await _sut.Handle(new AdminRestoreVolunteerOpportunityCommand(opportunityId, DefaultAdminUserId), cancellationToken);

		// Assert
		await _fileStorage.DidNotReceive().UnquarantineAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldNotThrow_WhenUnquarantiningTheBannerObjectFails(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		opportunity.SetBannerImageUrl("https://example.com/opportunity-banners/banner.png");
		opportunity.MarkDeleted(DateTimeOffset.UtcNow);
		_dbContext
			.FindVolunteerOpportunityIncludingDeletedAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/opportunity-banners/banner.png")
			.Returns($"opportunity-banners/{opportunityId}.png");
		_fileStorage
			.UnquarantineAsync($"opportunity-banners/{opportunityId}.png", Arg.Any<CancellationToken>())
			.ThrowsAsync(new InvalidOperationException("Storage unavailable"));

		// Act
		Func<Task> act = async () => await _sut.Handle(new AdminRestoreVolunteerOpportunityCommand(opportunityId, DefaultAdminUserId), cancellationToken);

		// Assert
		await act.Should().NotThrowAsync();
	}
}
