using Application.Common.Keycloak;
using Application.Common.Persistence;
using Application.Users.GetUserProfile.v1;
using AwesomeAssertions;
using Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Users.GetUserProfile;

public class GetUserProfileQueryHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IKeycloakUserService _keycloakUserService = Substitute.For<IKeycloakUserService>();
	private readonly GetUserProfileQueryHandler _sut;

	private static readonly UserId DefaultUserId = UserId.New();

	public GetUserProfileQueryHandlerTests()
	{
		_keycloakUserService
			.GetUserAsync(DefaultUserId.Value, Arg.Any<CancellationToken>())
			.Returns(new KeycloakUserProfile(DefaultUserId.Value, "vera", "Vera", "Volunteer", "vera@example.com"));
		_sut = new GetUserProfileQueryHandler(_keycloakUserService, _dbContext);
	}

	[Test]
	public async Task Handle_ShouldReturnPhone_WhenUserRowHasOne(
		CancellationToken cancellationToken)
	{
		var userId = UserId.New();
		_keycloakUserService
			.GetUserAsync(userId.Value, cancellationToken)
			.Returns(new KeycloakUserProfile(userId.Value, "vera", "Vera", "Volunteer", "vera@test.de"));

		var user = User.Create(userId);
		user.SetPhone("+49 30 1234567");
		_dbContext.GetOrCreateUserAsync(userId, cancellationToken).Returns(user);

		var result = await _sut.Handle(new GetUserProfileQuery(userId), cancellationToken);

		result.Phone.Should().Be("+49 30 1234567");
	}

	[Test]
	public async Task Handle_ShouldDelegateLazyRowCreation_ToTheIdempotentGetOrCreate(
		CancellationToken cancellationToken)
	{
		var user = User.Create(DefaultUserId);
		_dbContext.GetOrCreateUserAsync(DefaultUserId, cancellationToken).Returns(user);
		var query = new GetUserProfileQuery(DefaultUserId);

		await _sut.Handle(query, cancellationToken);

		await _dbContext.Received(1).GetOrCreateUserAsync(DefaultUserId, cancellationToken);
	}
}
