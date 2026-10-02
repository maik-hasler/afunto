using Application.Common.Exceptions;
using Application.Common.Persistence;
using Application.Common.Storage;
using Application.Users.AdminShadowDeleteUser.v1;
using AwesomeAssertions;
using Domain.AuditLogs;
using Domain.Primitives;
using Domain.Reports;
using Domain.Users;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Application.UnitTests.Users.AdminShadowDeleteUser;

public class AdminShadowDeleteUserCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<User, UserId> _userRepo =
		Substitute.For<IAggregateRepository<User, UserId>>();
	private readonly IAggregateRepository<AuditLog, AuditLogId> _auditLogRepo =
		Substitute.For<IAggregateRepository<AuditLog, AuditLogId>>();
	private readonly IFileStorageService _fileStorage = Substitute.For<IFileStorageService>();
	private readonly AdminShadowDeleteUserCommandHandler _sut;

	private static readonly UserId DefaultAdminUserId = UserId.New();

	public AdminShadowDeleteUserCommandHandlerTests()
	{
		_dbContext.Users.Returns(_userRepo);
		_dbContext.AuditLogs.Returns(_auditLogRepo);
		_dbContext
			.GetOpenReportsForTargetAsync(Arg.Any<ReportTargetType>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns(new List<Report>());
		_sut = new AdminShadowDeleteUserCommandHandler(_dbContext, _fileStorage);
	}

	[Test]
	public async Task Handle_ShouldShadowDeleteUser_AndWriteAuditLog(
		CancellationToken cancellationToken)
	{
		// Arrange
		var userId = Guid.NewGuid();
		var user = User.Create(UserId.Create(userId).GetValueOrThrow());
		_userRepo.FindAsync(UserId.Create(userId).GetValueOrThrow(), cancellationToken).Returns(user);

		// Act
		var result = await _sut.Handle(new AdminShadowDeleteUserCommand(userId, DefaultAdminUserId), cancellationToken);

		// Assert
		result.Should().BeTrue();
		user.IsDeleted.Should().BeTrue();
		await _auditLogRepo.Received(1).AddAsync(
			Arg.Is<AuditLog>(a => a!.ActorUserId == DefaultAdminUserId
				&& a.ActionType == AuditActionType.UserShadowDeleted
				&& a.SubjectType == AuditSubjectType.User
				&& a.SubjectId == userId),
			cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldMarkOpenReportsActioned_WhenUserShadowDeleted(
		CancellationToken cancellationToken)
	{
		// Arrange
		var userId = Guid.NewGuid();
		var user = User.Create(UserId.Create(userId).GetValueOrThrow());
		_userRepo.FindAsync(UserId.Create(userId).GetValueOrThrow(), cancellationToken).Returns(user);
		var report = Report.Create(ReportTargetType.User, userId, UserId.New(), ReportReason.Harassment, null).Value;
		_dbContext
			.GetOpenReportsForTargetAsync(ReportTargetType.User, userId, cancellationToken)
			.Returns([report]);

		// Act
		await _sut.Handle(new AdminShadowDeleteUserCommand(userId, DefaultAdminUserId), cancellationToken);

		// Assert
		report.Status.Should().Be(ReportStatus.Actioned);
		report.ResolvedByUserId.Should().Be(DefaultAdminUserId);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenUserNotFound(
		CancellationToken cancellationToken)
	{
		// Arrange
		var userId = Guid.NewGuid();
		_userRepo.FindAsync(UserId.Create(userId).GetValueOrThrow(), cancellationToken).Returns((User?)null);

		// Act
		Func<Task> act = async () => await _sut.Handle(new AdminShadowDeleteUserCommand(userId, DefaultAdminUserId), cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.NotFound);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenUserAlreadyShadowDeleted(
		CancellationToken cancellationToken)
	{
		// Arrange
		var userId = Guid.NewGuid();
		var user = User.Create(UserId.Create(userId).GetValueOrThrow());
		user.MarkDeleted(DateTimeOffset.UtcNow);
		_userRepo.FindAsync(UserId.Create(userId).GetValueOrThrow(), cancellationToken).Returns(user);

		// Act
		Func<Task> act = async () => await _sut.Handle(new AdminShadowDeleteUserCommand(userId, DefaultAdminUserId), cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>().WithMessage("*already shadow-deleted*");
		await _auditLogRepo.DidNotReceive().AddAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldQuarantineTheAvatarObject_WhenUserHasAnAvatar(
		CancellationToken cancellationToken)
	{
		// Arrange
		var userId = Guid.NewGuid();
		var user = User.Create(UserId.Create(userId).GetValueOrThrow());
		user.SetAvatarUrl("https://example.com/user-avatars/avatar.png");
		_userRepo.FindAsync(UserId.Create(userId).GetValueOrThrow(), cancellationToken).Returns(user);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/user-avatars/avatar.png")
			.Returns("user-avatars/avatar.png");

		// Act
		await _sut.Handle(new AdminShadowDeleteUserCommand(userId, DefaultAdminUserId), cancellationToken);

		// Assert
		await _fileStorage.Received(1).QuarantineAsync("user-avatars/avatar.png", cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldNotAttemptQuarantine_WhenUserHasNoAvatar(
		CancellationToken cancellationToken)
	{
		// Arrange
		var userId = Guid.NewGuid();
		var user = User.Create(UserId.Create(userId).GetValueOrThrow());
		_userRepo.FindAsync(UserId.Create(userId).GetValueOrThrow(), cancellationToken).Returns(user);

		// Act
		await _sut.Handle(new AdminShadowDeleteUserCommand(userId, DefaultAdminUserId), cancellationToken);

		// Assert
		await _fileStorage.DidNotReceive().QuarantineAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldNotThrow_WhenQuarantiningTheAvatarObjectFails(
		CancellationToken cancellationToken)
	{
		// Arrange
		var userId = Guid.NewGuid();
		var user = User.Create(UserId.Create(userId).GetValueOrThrow());
		user.SetAvatarUrl("https://example.com/user-avatars/avatar.png");
		_userRepo.FindAsync(UserId.Create(userId).GetValueOrThrow(), cancellationToken).Returns(user);
		_fileStorage
			.GetObjectKeyFromPublicUrl("https://example.com/user-avatars/avatar.png")
			.Returns("user-avatars/avatar.png");
		_fileStorage
			.QuarantineAsync("user-avatars/avatar.png", Arg.Any<CancellationToken>())
			.ThrowsAsync(new InvalidOperationException("Storage unavailable"));

		// Act
		Func<Task> act = async () => await _sut.Handle(new AdminShadowDeleteUserCommand(userId, DefaultAdminUserId), cancellationToken);

		// Assert
		await act.Should().NotThrowAsync();
	}
}
