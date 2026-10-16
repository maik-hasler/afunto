using Application.Common.Exceptions;
using Application.Common.Keycloak;
using Application.Common.Persistence;
using Application.UnitTests.Common.Time;
using Application.Users.SetUserEnabled.v1;
using AwesomeAssertions;
using Domain.AuditLogs;
using Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Users.SetUserEnabled;

public class SetUserEnabledCommandHandlerTests
{
	private readonly IKeycloakUserService _keycloakService = Substitute.For<IKeycloakUserService>();
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<AuditLog, AuditLogId> _auditLogRepo =
		Substitute.For<IAggregateRepository<AuditLog, AuditLogId>>();
	private readonly SetUserEnabledCommandHandler _sut;
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 14, 0, 0, TimeSpan.Zero);

	public SetUserEnabledCommandHandlerTests()
	{
		_dbContext.AuditLogs.Returns(_auditLogRepo);
		_dbContext.GetOrCreateUserAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(call => User.Create(call.Arg<UserId>()));
		_sut = new SetUserEnabledCommandHandler(_keycloakService, _dbContext, new FakeTimeProvider(Now));
	}

	[Test]
	public async Task Handle_ShouldDisableTargetUser_WhenNotSelf(
		CancellationToken cancellationToken)
	{
		// Arrange
		var targetUserId = Guid.NewGuid();
		var actingUserId = Guid.NewGuid();
		_keycloakService.IsServiceAccountAsync(targetUserId, cancellationToken).Returns(false);
		var command = new SetUserEnabledCommand(targetUserId, actingUserId, Enabled: false);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
		await _keycloakService.Received(1).SetUserEnabledAsync(targetUserId, false, cancellationToken);
		await _auditLogRepo.Received(1).AddAsync(
			Arg.Is<AuditLog>(a => a!.ActorUserId.Value == actingUserId
				&& a.ActionType == AuditActionType.UserDisabled
				&& a.SubjectType == AuditSubjectType.User
				&& a.SubjectId == targetUserId),
			cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldThrowConflict_WhenActorDisablesTheirOwnAccount(
		CancellationToken cancellationToken)
	{
		// Arrange
		var actingUserId = Guid.NewGuid();
		_keycloakService.IsServiceAccountAsync(actingUserId, cancellationToken).Returns(false);
		var command = new SetUserEnabledCommand(actingUserId, actingUserId, Enabled: false);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>()
			.WithMessage("*own account*");
		await _keycloakService.DidNotReceive().SetUserEnabledAsync(
			Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldAllowEnablingSelf_BecauseGuardOnlyBlocksDisabling(
		CancellationToken cancellationToken)
	{
		// Arrange
		var actingUserId = Guid.NewGuid();
		_keycloakService.IsServiceAccountAsync(actingUserId, cancellationToken).Returns(false);
		var command = new SetUserEnabledCommand(actingUserId, actingUserId, Enabled: true);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
		await _keycloakService.Received(1).SetUserEnabledAsync(actingUserId, true, cancellationToken);
		await _auditLogRepo.Received(1).AddAsync(
			Arg.Is<AuditLog>(a => a!.ActionType == AuditActionType.UserEnabled),
			cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldThrowForbidden_WhenTargetIsAServiceAccount(
		CancellationToken cancellationToken)
	{
		// Arrange
		var targetUserId = Guid.NewGuid();
		var actingUserId = Guid.NewGuid();
		_keycloakService.IsServiceAccountAsync(targetUserId, cancellationToken).Returns(true);
		var command = new SetUserEnabledCommand(targetUserId, actingUserId, Enabled: false);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>()
			.WithMessage("*service account*");
		await _keycloakService.DidNotReceive().SetUserEnabledAsync(
			Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldRecordPermissionsChangedOnTarget_WhenBlockingAnotherUser(
		CancellationToken cancellationToken)
	{
		// Arrange
		var targetUserId = Guid.NewGuid();
		var targetUser = User.Create(UserId.Create(targetUserId).Value);
		_dbContext.GetOrCreateUserAsync(targetUser.Id, cancellationToken).Returns(targetUser);
		var command = new SetUserEnabledCommand(targetUserId, Guid.NewGuid(), Enabled: false);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		targetUser.PermissionsChangedOn.Should().Be(Now);
	}

	[Test]
	public async Task Handle_ShouldNotRecordPermissionsChanged_WhenActorDisablesTheirOwnAccount(
		CancellationToken cancellationToken)
	{
		// Arrange
		var actingUserId = Guid.NewGuid();
		var command = new SetUserEnabledCommand(actingUserId, actingUserId, Enabled: false);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>();
		await _dbContext.DidNotReceive().GetOrCreateUserAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldNotRecordPermissionsChanged_WhenTargetIsAServiceAccount(
		CancellationToken cancellationToken)
	{
		// Arrange
		var targetUserId = Guid.NewGuid();
		_keycloakService.IsServiceAccountAsync(targetUserId, cancellationToken).Returns(true);
		var command = new SetUserEnabledCommand(targetUserId, Guid.NewGuid(), Enabled: false);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>();
		await _dbContext.DidNotReceive().GetOrCreateUserAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>());
	}
}
