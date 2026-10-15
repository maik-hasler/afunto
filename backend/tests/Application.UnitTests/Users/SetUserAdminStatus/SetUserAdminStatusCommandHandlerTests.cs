using Application.Common.Exceptions;
using Application.Common.Keycloak;
using Application.Common.Persistence;
using Application.Users.SetUserAdminStatus.v1;
using AwesomeAssertions;
using Domain.AuditLogs;
using Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Users.SetUserAdminStatus;

public class SetUserAdminStatusCommandHandlerTests
{
	private readonly IKeycloakUserService _keycloakService = Substitute.For<IKeycloakUserService>();
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<AuditLog, AuditLogId> _auditLogRepo =
		Substitute.For<IAggregateRepository<AuditLog, AuditLogId>>();
	private readonly SetUserAdminStatusCommandHandler _sut;
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 14, 0, 0, TimeSpan.Zero);

	public SetUserAdminStatusCommandHandlerTests()
	{
		_dbContext.AuditLogs.Returns(_auditLogRepo);
		_dbContext.GetOrCreateUserAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>())
			.Returns(call => User.Create(call.Arg<UserId>()));
		_sut = new SetUserAdminStatusCommandHandler(_keycloakService, _dbContext, new FakeTimeProvider(Now));
	}

	[Test]
	public async Task Handle_ShouldAssignAdminRole_WhenPromotingAnotherUser(
		CancellationToken cancellationToken)
	{
		// Arrange
		var targetUserId = Guid.NewGuid();
		var actingUserId = Guid.NewGuid();
		_keycloakService.IsServiceAccountAsync(targetUserId, cancellationToken).Returns(false);
		var command = new SetUserAdminStatusCommand(targetUserId, actingUserId, IsAdmin: true);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
		await _keycloakService.Received(1).AssignAdminRoleAsync(targetUserId, cancellationToken);
		await _keycloakService.DidNotReceive().RemoveAdminRoleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
		await _auditLogRepo.Received(1).AddAsync(
			Arg.Is<AuditLog>(a => a!.ActorUserId.Value == actingUserId
				&& a.ActionType == AuditActionType.UserPromotedToAdmin
				&& a.SubjectType == AuditSubjectType.User
				&& a.SubjectId == targetUserId),
			cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldRemoveAdminRole_WhenDemotingAnotherUser(
		CancellationToken cancellationToken)
	{
		// Arrange
		var targetUserId = Guid.NewGuid();
		var actingUserId = Guid.NewGuid();
		_keycloakService.IsServiceAccountAsync(targetUserId, cancellationToken).Returns(false);
		var command = new SetUserAdminStatusCommand(targetUserId, actingUserId, IsAdmin: false);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
		await _keycloakService.Received(1).RemoveAdminRoleAsync(targetUserId, cancellationToken);
		await _keycloakService.DidNotReceive().AssignAdminRoleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
		await _auditLogRepo.Received(1).AddAsync(
			Arg.Is<AuditLog>(a => a!.ActionType == AuditActionType.UserDemotedFromAdmin),
			cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldThrowConflict_WhenActorDemotesThemselves(
		CancellationToken cancellationToken)
	{
		// Arrange
		var actingUserId = Guid.NewGuid();
		_keycloakService.IsServiceAccountAsync(actingUserId, cancellationToken).Returns(false);
		var command = new SetUserAdminStatusCommand(actingUserId, actingUserId, IsAdmin: false);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>()
			.WithMessage("*own admin access*");
		await _keycloakService.DidNotReceive().RemoveAdminRoleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldAllowSelfPromotion_BecauseGuardOnlyBlocksDemoting(
		CancellationToken cancellationToken)
	{
		// Arrange
		var actingUserId = Guid.NewGuid();
		_keycloakService.IsServiceAccountAsync(actingUserId, cancellationToken).Returns(false);
		var command = new SetUserAdminStatusCommand(actingUserId, actingUserId, IsAdmin: true);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
		await _keycloakService.Received(1).AssignAdminRoleAsync(actingUserId, cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldThrowForbidden_WhenTargetIsAServiceAccount(
		CancellationToken cancellationToken)
	{
		// Arrange
		var targetUserId = Guid.NewGuid();
		var actingUserId = Guid.NewGuid();
		_keycloakService.IsServiceAccountAsync(targetUserId, cancellationToken).Returns(true);
		var command = new SetUserAdminStatusCommand(targetUserId, actingUserId, IsAdmin: true);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>()
			.WithMessage("*service account*");
		await _keycloakService.DidNotReceive().AssignAdminRoleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldRecordPermissionsChangedOnTarget_WhenDemotingAnotherUser(
		CancellationToken cancellationToken)
	{
		// Arrange
		var targetUserId = Guid.NewGuid();
		var targetUser = User.Create(UserId.Create(targetUserId).Value);
		_dbContext.GetOrCreateUserAsync(targetUser.Id, cancellationToken).Returns(targetUser);
		var command = new SetUserAdminStatusCommand(targetUserId, Guid.NewGuid(), IsAdmin: false);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		targetUser.PermissionsChangedOn.Should().Be(Now);
	}

	[Test]
	public async Task Handle_ShouldNotRecordPermissionsChanged_WhenActorDemotesThemselves(
		CancellationToken cancellationToken)
	{
		// Arrange
		var actingUserId = Guid.NewGuid();
		var command = new SetUserAdminStatusCommand(actingUserId, actingUserId, IsAdmin: false);

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
		var command = new SetUserAdminStatusCommand(targetUserId, Guid.NewGuid(), IsAdmin: false);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>();
		await _dbContext.DidNotReceive().GetOrCreateUserAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>());
	}

	private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => utcNow;
	}
}
