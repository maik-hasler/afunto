using Application.Common.Keycloak;
using Application.Common.Persistence;
using Application.Users.UpdateUserProfile.v1;
using AwesomeAssertions;
using Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Users.UpdateUserProfile;

public class UpdateUserProfileCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IKeycloakUserService _keycloakUserService = Substitute.For<IKeycloakUserService>();
	private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
	private readonly UpdateUserProfileCommandHandler _sut;

	public UpdateUserProfileCommandHandlerTests()
	{
		_sut = new UpdateUserProfileCommandHandler(_keycloakUserService, _dbContext, _unitOfWork);
	}

	[Test]
	public async Task Handle_ShouldSetPhone_OnExistingUserRow(
		CancellationToken cancellationToken)
	{
		var userId = UserId.New();
		var user = User.Create(userId);
		_dbContext.GetOrCreateUserAsync(userId, cancellationToken).Returns(user);

		var command = new UpdateUserProfileCommand(
			userId, "Vera", "Volunteer", "Bio", "+49 30 1234567", [], [], null);

		await _sut.Handle(command, cancellationToken);

		user.Phone.Should().Be("+49 30 1234567");
	}

	[Test]
	public async Task Handle_ShouldClearPhone_WhenNullGiven(
		CancellationToken cancellationToken)
	{
		var userId = UserId.New();
		var user = User.Create(userId);
		user.SetPhone("+49 30 1234567");
		_dbContext.GetOrCreateUserAsync(userId, cancellationToken).Returns(user);

		var command = new UpdateUserProfileCommand(
			userId, "Vera", "Volunteer", "Bio", null, [], [], null);

		await _sut.Handle(command, cancellationToken);

		user.Phone.Should().BeNull();
	}

	[Test]
	public async Task Handle_ShouldSetPhone_OnALazilyCreatedUserRow(
		CancellationToken cancellationToken)
	{
		var userId = UserId.New();
		var user = User.Create(userId);
		_dbContext.GetOrCreateUserAsync(userId, cancellationToken).Returns(user);

		var command = new UpdateUserProfileCommand(
			userId, "Vera", "Volunteer", "Bio", "+49 30 1234567", [], [], null);

		await _sut.Handle(command, cancellationToken);

		user.Phone.Should().Be("+49 30 1234567");
	}
}
