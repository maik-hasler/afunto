using Application.Common.Exceptions;
using Application.Common.Persistence;
using Application.Common.Storage;
using Application.VolunteerOpportunities.UploadOpportunityBanner.v1;
using AwesomeAssertions;
using Domain.Organizations;
using Domain.Primitives;
using Domain.Users;
using Domain.VolunteerOpportunities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Application.UnitTests.VolunteerOpportunities.UploadOpportunityBanner;

public class UploadOpportunityBannerCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId> _opportunityRepo =
		Substitute.For<IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId>>();
	private readonly IFileStorageService _fileStorage = Substitute.For<IFileStorageService>();
	private readonly IPinGenerator _pinGenerator = Substitute.For<IPinGenerator>();
	private readonly UploadOpportunityBannerCommandHandler _sut;

	private static readonly OrganizationId DefaultOrgId = OrganizationId.New();
	private static readonly UserId DefaultRequestingUserId = UserId.New();
	private static byte[] PngBytes => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

	public UploadOpportunityBannerCommandHandlerTests()
	{
		_dbContext.VolunteerOpportunities.Returns(_opportunityRepo);
		_dbContext
			.IsOrganizerAsync(Arg.Any<OrganizationId>(), Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(true);
		_fileStorage
			.UploadAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns("https://example.com/opportunity-banners/banner.png");
		_sut = new UploadOpportunityBannerCommandHandler(_dbContext, _fileStorage);
	}

	private VolunteerOpportunity CreateOpportunity() =>
		VolunteerOpportunity.Create(
			DefaultOrgId, "Titel", null, "Beschreibung", null, true, null, Occurrence.OneTime, ParticipationType.ScheduledSlots, CheckInMethod.None, _pinGenerator,
			status: OpportunityStatus.Draft).Value;

	[Test]
	public async Task Handle_ShouldSetBannerImageUrl_WhenOpportunityExists(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);

		var command = new UploadOpportunityBannerCommand(opportunityId, PngBytes, "image/png", DefaultRequestingUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
		opportunity.BannerImageUrl.Should().Be("https://example.com/opportunity-banners/banner.png");
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

		var command = new UploadOpportunityBannerCommand(opportunityId, PngBytes, "image/png", DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.Forbidden);
		opportunity.BannerImageUrl.Should().BeNull();
		await _fileStorage.DidNotReceive().UploadAsync(
			Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldDeleteThePreviousBannerObject_WhenReuploadedWithADifferentExtension(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		opportunity.SetBannerImageUrl("https://example.com/opportunity-banners/old.jpg");
		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/opportunity-banners/old.jpg")
			.Returns($"opportunity-banners/{opportunityId}.jpg");
		var command = new UploadOpportunityBannerCommand(opportunityId, PngBytes, "image/png", DefaultRequestingUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
		await _fileStorage.Received(1).DeleteAsync($"opportunity-banners/{opportunityId}.jpg", cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldNotDeleteAnything_WhenReuploadedWithTheSameExtension(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		opportunity.SetBannerImageUrl("https://example.com/opportunity-banners/old.png");
		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/opportunity-banners/old.png")
			.Returns($"opportunity-banners/{opportunityId}.png");
		var command = new UploadOpportunityBannerCommand(opportunityId, PngBytes, "image/png", DefaultRequestingUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert - re-uploading with the same extension overwrote the object at
		// that key in place, so deleting it now would delete the file just uploaded.
		await _fileStorage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldNotAttemptDeletion_WhenOpportunityHadNoPreviousBanner(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);
		var command = new UploadOpportunityBannerCommand(opportunityId, PngBytes, "image/png", DefaultRequestingUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _fileStorage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldNotThrow_WhenDeletingThePreviousBannerObjectFails(
		CancellationToken cancellationToken)
	{
		// Arrange
		var opportunityId = Guid.CreateVersion7();
		var opportunity = CreateOpportunity();
		opportunity.SetBannerImageUrl("https://example.com/opportunity-banners/old.jpg");
		_opportunityRepo
			.FindAsync(VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow(), cancellationToken)
			.Returns(opportunity);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/opportunity-banners/old.jpg")
			.Returns($"opportunity-banners/{opportunityId}.jpg");
		_fileStorage
			.DeleteAsync($"opportunity-banners/{opportunityId}.jpg", Arg.Any<CancellationToken>())
			.ThrowsAsync(new InvalidOperationException("Storage unavailable"));
		var command = new UploadOpportunityBannerCommand(opportunityId, PngBytes, "image/png", DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().NotThrowAsync();
	}
}
