using Application.Common.Exceptions;
using Application.Common.Persistence;
using Application.Common.Storage;
using Application.Organizations.AdminRestoreOrganization.v1;
using AwesomeAssertions;
using Domain.AuditLogs;
using Domain.Organizations;
using Domain.Primitives;
using Domain.Users;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Application.UnitTests.Organizations.AdminRestoreOrganization;

public class AdminRestoreOrganizationCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<AuditLog, AuditLogId> _auditLogRepo =
		Substitute.For<IAggregateRepository<AuditLog, AuditLogId>>();
	private readonly IFileStorageService _fileStorage = Substitute.For<IFileStorageService>();
	private readonly AdminRestoreOrganizationCommandHandler _sut;

	private static readonly UserId DefaultAdminUserId = UserId.New();

	public AdminRestoreOrganizationCommandHandlerTests()
	{
		_dbContext.AuditLogs.Returns(_auditLogRepo);
		_sut = new AdminRestoreOrganizationCommandHandler(_dbContext, _fileStorage);
	}

	private static Organization CreateShadowDeletedOrganization(OrganizationId organizationId)
	{
		var organization = Organization.Create(organizationId, "Test Org").Value;
		organization.MarkDeleted(DateTimeOffset.UtcNow);
		return organization;
	}

	[Test]
	public async Task Handle_ShouldRestoreOrganization_AndWriteAuditLog(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organizationId = OrganizationId.Create(orgId).GetValueOrThrow();
		var organization = CreateShadowDeletedOrganization(organizationId);
		_dbContext.FindOrganizationIncludingDeletedAsync(organizationId, cancellationToken).Returns(organization);

		// Act
		var result = await _sut.Handle(new AdminRestoreOrganizationCommand(orgId, DefaultAdminUserId), cancellationToken);

		// Assert
		result.Should().BeTrue();
		organization.IsDeleted.Should().BeFalse();
		await _auditLogRepo.Received(1).AddAsync(
			Arg.Is<AuditLog>(a => a!.ActorUserId == DefaultAdminUserId
				&& a.ActionType == AuditActionType.OrganizationRestored
				&& a.SubjectType == AuditSubjectType.Organization
				&& a.SubjectId == orgId),
			cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenOrganizationNotFound(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organizationId = OrganizationId.Create(orgId).GetValueOrThrow();
		_dbContext.FindOrganizationIncludingDeletedAsync(organizationId, cancellationToken).Returns((Organization?)null);

		// Act
		Func<Task> act = async () => await _sut.Handle(new AdminRestoreOrganizationCommand(orgId, DefaultAdminUserId), cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.NotFound);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenOrganizationNotShadowDeleted(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organizationId = OrganizationId.Create(orgId).GetValueOrThrow();
		var organization = Organization.Create(organizationId, "Test Org").Value;
		_dbContext.FindOrganizationIncludingDeletedAsync(organizationId, cancellationToken).Returns(organization);

		// Act
		Func<Task> act = async () => await _sut.Handle(new AdminRestoreOrganizationCommand(orgId, DefaultAdminUserId), cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>().WithMessage("*not shadow-deleted*");
		await _auditLogRepo.DidNotReceive().AddAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldUnquarantineTheLogoObject_WhenOrganizationHasALogo(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organizationId = OrganizationId.Create(orgId).GetValueOrThrow();
		var organization = CreateShadowDeletedOrganization(organizationId);
		organization.SetLogoUrl("https://example.com/organization-logos/logo.png");
		_dbContext.FindOrganizationIncludingDeletedAsync(organizationId, cancellationToken).Returns(organization);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/organization-logos/logo.png")
			.Returns($"organization-logos/{orgId}.png");

		// Act
		await _sut.Handle(new AdminRestoreOrganizationCommand(orgId, DefaultAdminUserId), cancellationToken);

		// Assert
		await _fileStorage.Received(1).UnquarantineAsync($"organization-logos/{orgId}.png", cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldNotAttemptUnquarantine_WhenOrganizationHasNoLogo(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organizationId = OrganizationId.Create(orgId).GetValueOrThrow();
		var organization = CreateShadowDeletedOrganization(organizationId);
		_dbContext.FindOrganizationIncludingDeletedAsync(organizationId, cancellationToken).Returns(organization);

		// Act
		await _sut.Handle(new AdminRestoreOrganizationCommand(orgId, DefaultAdminUserId), cancellationToken);

		// Assert
		await _fileStorage.DidNotReceive().UnquarantineAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldNotThrow_WhenUnquarantiningTheLogoObjectFails(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organizationId = OrganizationId.Create(orgId).GetValueOrThrow();
		var organization = CreateShadowDeletedOrganization(organizationId);
		organization.SetLogoUrl("https://example.com/organization-logos/logo.png");
		_dbContext.FindOrganizationIncludingDeletedAsync(organizationId, cancellationToken).Returns(organization);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/organization-logos/logo.png")
			.Returns($"organization-logos/{orgId}.png");
		_fileStorage
			.UnquarantineAsync($"organization-logos/{orgId}.png", Arg.Any<CancellationToken>())
			.ThrowsAsync(new InvalidOperationException("Storage unavailable"));

		// Act
		Func<Task> act = async () => await _sut.Handle(new AdminRestoreOrganizationCommand(orgId, DefaultAdminUserId), cancellationToken);

		// Assert
		await act.Should().NotThrowAsync();
	}
}
