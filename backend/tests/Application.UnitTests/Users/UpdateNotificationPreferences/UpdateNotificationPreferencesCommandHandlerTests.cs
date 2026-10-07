using Application.Common.Persistence;
using Application.Users.UpdateNotificationPreferences.v1;
using AwesomeAssertions;
using Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Users.UpdateNotificationPreferences;

public class UpdateNotificationPreferencesCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
	private readonly IAggregateRepository<User, UserId> _userRepo =
		Substitute.For<IAggregateRepository<User, UserId>>();
	private readonly UpdateNotificationPreferencesCommandHandler _sut;

	public UpdateNotificationPreferencesCommandHandlerTests()
	{
		_dbContext.Users.Returns(_userRepo);
		_sut = new UpdateNotificationPreferencesCommandHandler(_dbContext, _unitOfWork);
	}

	[Test]
	public async Task Handle_ShouldApplyAllFiveFlags_WhenUserRowAlreadyExists(
		CancellationToken cancellationToken)
	{
		// Arrange
		var userId = UserId.New();
		var user = User.Create(userId);
		_userRepo.FindAsync(userId, cancellationToken).Returns(user);

		var command = new UpdateNotificationPreferencesCommand(
			userId,
			NotifyOnNewSignUp: false,
			NotifyOnWithdrawal: false,
			NotifyOnEngagementConfirmed: true,
			NotifyOnEngagementReminder: false);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		user.NotifyOnNewSignUp.Should().BeFalse();
		user.NotifyOnWithdrawal.Should().BeFalse();
		user.NotifyOnEngagementConfirmed.Should().BeTrue();
		user.NotifyOnEngagementReminder.Should().BeFalse();
		await _unitOfWork.Received(1).SaveChangesAsync(cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldLazilyCreateAUser_WhenNoRowExistsYet(
		CancellationToken cancellationToken)
	{
		// Arrange
		var userId = UserId.New();
		_userRepo.FindAsync(userId, cancellationToken).Returns((User?)null);

		var command = new UpdateNotificationPreferencesCommand(
			userId,
			NotifyOnNewSignUp: false,
			NotifyOnWithdrawal: true,
			NotifyOnEngagementConfirmed: true,
			NotifyOnEngagementReminder: true);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		await _userRepo.Received(1).AddAsync(
			Arg.Is<User>(u => u!.Id == userId && !u.NotifyOnNewSignUp),
			cancellationToken);
	}
}
