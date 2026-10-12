using Application.Common.Exceptions;
using Application.Common.Keycloak;
using Application.Common.Persistence;
using Application.Organizations.CreateInvitation.v1;
using AwesomeAssertions;
using Domain.Organizations;
using Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Organizations.CreateInvitation;

public class CreateInvitationCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
	private readonly IKeycloakOrganizationService _keycloakOrgService = Substitute.For<IKeycloakOrganizationService>();
	private readonly IKeycloakUserService _keycloakUserService = Substitute.For<IKeycloakUserService>();
	private readonly IAggregateRepository<Organization, OrganizationId> _orgRepo =
		Substitute.For<IAggregateRepository<Organization, OrganizationId>>();
	private readonly CreateInvitationCommandHandler _sut;

	private static readonly OrganizationId DefaultOrgId = OrganizationId.New();
	private static readonly UserId DefaultInvitedById = UserId.New();
	private static readonly UserId DefaultInviteeId = UserId.New();

	public CreateInvitationCommandHandlerTests()
	{
		_dbContext.Organizations.Returns(_orgRepo);
		_orgRepo.FindAsync(DefaultOrgId, Arg.Any<CancellationToken>())
			.Returns(Organization.Create(DefaultOrgId, "Test Org").Value);
		_dbContext
			.IsOrganizerAsync(DefaultOrgId, DefaultInvitedById, Arg.Any<CancellationToken>())
			.Returns(true);
		_dbContext
			.TryCreateInvitationAsync(Arg.Any<OrganizationInvitation>(), Arg.Any<CancellationToken>())
			.Returns(true);
		_keycloakOrgService
			.GetMembersAsync(DefaultOrgId.Value, Arg.Any<CancellationToken>())
			.Returns([]);
		_keycloakUserService
			.GetUserAsync(DefaultInviteeId.Value, Arg.Any<CancellationToken>())
			.Returns(new KeycloakUserProfile(DefaultInviteeId.Value, "vera", "Vera", "Miller", "vera@test.de"));
		_sut = new CreateInvitationCommandHandler(
			_dbContext, _unitOfWork, _keycloakOrgService, _keycloakUserService);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenRequestingUserIsNotMemberOfTheOrganization(
		CancellationToken cancellationToken)
	{
		// Arrange
		_dbContext
			.IsOrganizerAsync(DefaultOrgId, DefaultInvitedById, cancellationToken)
			.Returns(false);
		var command = new CreateInvitationCommand(DefaultOrgId, DefaultInviteeId, OrganizationMemberRole.Organizer, DefaultInvitedById);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>()
			.WithMessage("*permission*");
		await _keycloakUserService.DidNotReceive().GetUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
		await _dbContext.DidNotReceive().TryCreateInvitationAsync(Arg.Any<OrganizationInvitation>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldCreateInvitation_WhenRequestingUserIsOrgMember(
		CancellationToken cancellationToken)
	{
		// Arrange
		var command = new CreateInvitationCommand(DefaultOrgId, DefaultInviteeId, OrganizationMemberRole.Member, DefaultInvitedById);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().NotBeNull();
		await _dbContext.Received(1).TryCreateInvitationAsync(
			Arg.Is<OrganizationInvitation>(i => i != null && i.IntendedRole == OrganizationMemberRole.Member),
			cancellationToken);
		await _unitOfWork.Received(1).SaveChangesAsync(cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldThrowConflict_WhenAPendingInvitationAlreadyExists(
		CancellationToken cancellationToken)
	{
		_dbContext
			.TryCreateInvitationAsync(Arg.Any<OrganizationInvitation>(), Arg.Any<CancellationToken>())
			.Returns(false);
		var command = new CreateInvitationCommand(DefaultOrgId, DefaultInviteeId, OrganizationMemberRole.Member, DefaultInvitedById);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>()
			.WithMessage("*pending invitation*");
		await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
	}
}
