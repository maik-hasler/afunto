using Application.Common.Exceptions;
using Application.Common.Persistence;
using Application.Common.Storage;
using Application.Organizations.UploadOrganizationLogo.v1;
using AwesomeAssertions;
using Domain.Organizations;
using Domain.Primitives;
using Domain.Users;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Application.UnitTests.Organizations.UploadOrganizationLogo;

public class UploadOrganizationLogoCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<Organization, OrganizationId> _orgRepo =
		Substitute.For<IAggregateRepository<Organization, OrganizationId>>();
	private readonly IFileStorageService _fileStorage = Substitute.For<IFileStorageService>();
	private readonly UploadOrganizationLogoCommandHandler _sut;

	private static readonly UserId DefaultRequestingUserId = UserId.New();
	private static byte[] PngBytes => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

	public UploadOrganizationLogoCommandHandlerTests()
	{
		_dbContext.Organizations.Returns(_orgRepo);
		_dbContext
			.IsOrganizerAsync(Arg.Any<OrganizationId>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(true);
		_fileStorage
			.UploadAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns("https://example.com/organization-logos/logo.png");
		_sut = new UploadOrganizationLogoCommandHandler(_dbContext, _fileStorage);
	}

	private static Organization CreateOrganization(Guid orgId) =>
		Organization.Create(OrganizationId.Create(orgId).GetValueOrThrow(), "Test Org").Value;

	[Test]
	public async Task Handle_ShouldSetLogoUrl_WhenOrganizationExists(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		_orgRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);

		var command = new UploadOrganizationLogoCommand(orgId, PngBytes, "image/png", DefaultRequestingUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
		organization.LogoUrl.Should().Be("https://example.com/organization-logos/logo.png");
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenRequestingUserIsNotOrganizer(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		_orgRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);
		_dbContext
			.IsOrganizerAsync(Arg.Any<OrganizationId>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(false);

		var command = new UploadOrganizationLogoCommand(orgId, PngBytes, "image/png", DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.Forbidden);
		organization.LogoUrl.Should().BeNull();
		await _fileStorage.DidNotReceive().UploadAsync(
			Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldDeleteThePreviousLogoObject_WhenReuploadedWithADifferentExtension(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		organization.SetLogoUrl("https://example.com/organization-logos/old.jpg");
		_orgRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/organization-logos/old.jpg")
			.Returns($"organization-logos/{orgId}.jpg");
		var command = new UploadOrganizationLogoCommand(orgId, PngBytes, "image/png", DefaultRequestingUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
		await _fileStorage.Received(1).DeleteAsync($"organization-logos/{orgId}.jpg", cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldNotDeleteAnything_WhenReuploadedWithTheSameExtension(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		organization.SetLogoUrl("https://example.com/organization-logos/old.png");
		_orgRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/organization-logos/old.png")
			.Returns($"organization-logos/{orgId}.png");
		var command = new UploadOrganizationLogoCommand(orgId, PngBytes, "image/png", DefaultRequestingUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert - re-uploading with the same extension overwrote the object at
		// that key in place, so deleting it now would delete the file just uploaded.
		await _fileStorage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldNotAttemptDeletion_WhenOrganizationHadNoPreviousLogo(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		_orgRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);
		var command = new UploadOrganizationLogoCommand(orgId, PngBytes, "image/png", DefaultRequestingUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _fileStorage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldNotThrow_WhenDeletingThePreviousLogoObjectFails(
		CancellationToken cancellationToken)
	{
		// Arrange
		var orgId = Guid.NewGuid();
		var organization = CreateOrganization(orgId);
		organization.SetLogoUrl("https://example.com/organization-logos/old.jpg");
		_orgRepo.FindAsync(OrganizationId.Create(orgId).GetValueOrThrow(), cancellationToken).Returns(organization);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/organization-logos/old.jpg")
			.Returns($"organization-logos/{orgId}.jpg");
		_fileStorage
			.DeleteAsync($"organization-logos/{orgId}.jpg", Arg.Any<CancellationToken>())
			.ThrowsAsync(new InvalidOperationException("Storage unavailable"));
		var command = new UploadOrganizationLogoCommand(orgId, PngBytes, "image/png", DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().NotThrowAsync();
	}
}
