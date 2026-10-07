using Application.Common.Persistence;
using Application.Users.GetNotificationPreferences.v1;
using AwesomeAssertions;
using Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Users.GetNotificationPreferences;

public class GetNotificationPreferencesQueryHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
	private readonly IAggregateRepository<User, UserId> _userRepo =
		Substitute.For<IAggregateRepository<User, UserId>>();
	private readonly GetNotificationPreferencesQueryHandler _sut;

	public GetNotificationPreferencesQueryHandlerTests()
	{
		_dbContext.Users.Returns(_userRepo);
		_sut = new GetNotificationPreferencesQueryHandler(_dbContext, _unitOfWork);
	}

	[Test]
	public async Task Handle_ShouldReturnCurrentPreferences_WhenUserRowAlreadyExists(
		CancellationToken cancellationToken)
	{
		// Arrange
		var userId = UserId.New();
		var user = User.Create(userId);
		user.UpdateNotificationPreferences(
			notifyOnNewSignUp: false,
			notifyOnWithdrawal: true,
			notifyOnEngagementConfirmed: true,
			notifyOnEngagementReminder: false);
		_userRepo.FindAsync(userId, cancellationToken).Returns(user);

		// Act
		var result = await _sut.Handle(new GetNotificationPreferencesQuery(userId), cancellationToken);

		// Assert
		result.NotifyOnNewSignUp.Should().BeFalse();
		result.NotifyOnWithdrawal.Should().BeTrue();
		result.NotifyOnEngagementConfirmed.Should().BeTrue();
		result.NotifyOnEngagementReminder.Should().BeFalse();
	}

	[Test]
	public async Task Handle_ShouldLazilyCreateAndPersistAUser_WhenNoRowExistsYet(
		CancellationToken cancellationToken)
	{
		// Arrange
		var userId = UserId.New();
		_userRepo.FindAsync(userId, cancellationToken).Returns((User?)null);

		// Act
		var result = await _sut.Handle(new GetNotificationPreferencesQuery(userId), cancellationToken);

		// Assert
		result.NotifyOnNewSignUp.Should().BeTrue();
		result.NotifyOnEngagementReminder.Should().BeTrue();
		await _userRepo.Received(1).AddAsync(Arg.Any<User>(), cancellationToken);
		await _unitOfWork.Received(1).SaveChangesAsync(cancellationToken);
	}
}
