using Application.Common.Email;
using Application.Common.Exceptions;
using Application.Common.Keycloak;
using Application.Common.Persistence;
using Application.Organizations.ResendInvitation.v1;
using AwesomeAssertions;
using Domain.Organizations;
using Domain.Primitives;
using Domain.Users;
using NSubstitute;


namespace Application.UnitTests.Organizations.ResendInvitation;

public class ResendInvitationCommandHandlerTests
{
	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
	private readonly IKeycloakUserService _keycloakUserService = Substitute.For<IKeycloakUserService>();
	private readonly IEmailService _emailService = Substitute.For<IEmailService>();
	private readonly IEmailTemplateRenderer _emailTemplateRenderer = Substitute.For<IEmailTemplateRenderer>();
	private readonly IEmailLinkBuilder _emailLinkBuilder = Substitute.For<IEmailLinkBuilder>();
	private readonly IAggregateRepository<OrganizationInvitation, OrganizationInvitationId> _invitationRepo =
		Substitute.For<IAggregateRepository<OrganizationInvitation, OrganizationInvitationId>>();
	private readonly IAggregateRepository<Organization, OrganizationId> _orgRepo =
		Substitute.For<IAggregateRepository<Organization, OrganizationId>>();
	private readonly ResendInvitationCommandHandler _sut;

	private static readonly OrganizationId DefaultOrgId = OrganizationId.New();
	private static readonly UserId DefaultRequestingUserId = UserId.New();
	private static readonly UserId DefaultInviteeId = UserId.New();

	public ResendInvitationCommandHandlerTests()
	{
		_dbContext.OrganizationInvitations.Returns(_invitationRepo);
		_dbContext.Organizations.Returns(_orgRepo);
		_orgRepo.FindAsync(DefaultOrgId, Arg.Any<CancellationToken>())
			.Returns(Organization.Create(DefaultOrgId, "Test Org").Value);
		_dbContext
			.IsOrganizerAsync(DefaultOrgId, DefaultRequestingUserId, Arg.Any<CancellationToken>())
			.Returns(true);
		_keycloakUserService
			.GetUserAsync(DefaultInviteeId.Value, Arg.Any<CancellationToken>())
			.Returns(new KeycloakUserProfile(DefaultInviteeId.Value, "vera", "Vera", "Miller", "vera@test.de"));
		_dbContext.GetOrCreateUsersAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
			.Returns(call => ((IReadOnlyCollection<UserId>)call[0]!).Select(User.Create).ToList());
		_emailTemplateRenderer
			.Render(Arg.Any<EmailDraft>())
			.Returns(new RenderedEmail("Test Subject", "Test Body", "<p>Test Body</p>", null));
		_sut = new ResendInvitationCommandHandler(
			_dbContext, _unitOfWork, _keycloakUserService, _emailService, _emailTemplateRenderer, _emailLinkBuilder);
	}

	private static OrganizationInvitation CreateExpiredInvitation(OrganizationId orgId)
	{
		var now = DateTimeOffset.UtcNow;
		var invitation = OrganizationInvitation.Create(
			orgId, DefaultInviteeId, UserId.New(), OrganizationMemberRole.Organizer, now);
		invitation.Expire(now.AddDays(OrganizationInvitation.ExpiryWindowDays)).ThrowIfFailure();
		return invitation;
	}

	private static OrganizationInvitation CreatePendingInvitation(OrganizationId orgId) =>
		OrganizationInvitation.Create(
			orgId, DefaultInviteeId, UserId.New(), OrganizationMemberRole.Organizer, DateTimeOffset.UtcNow);

	[Test]
	public async Task Handle_ShouldThrow_WhenRequestingUserIsNotMemberOfTheOrganization(
		CancellationToken cancellationToken)
	{
		// Arrange
		_dbContext
			.IsOrganizerAsync(DefaultOrgId, DefaultRequestingUserId, cancellationToken)
			.Returns(false);
		var invitation = CreateExpiredInvitation(DefaultOrgId);
		var command = new ResendInvitationCommand(DefaultOrgId, invitation.Id, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		await act.Should().ThrowAsync<ResultFailureException>()
			.WithMessage("*permission*");
		await _invitationRepo.DidNotReceive().FindAsync(Arg.Any<OrganizationInvitationId>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldResetToPendingAndSendEmail_WhenInvitationIsExpired(
		CancellationToken cancellationToken)
	{
		// Arrange
		var invitation = CreateExpiredInvitation(DefaultOrgId);
		_invitationRepo.FindAsync(invitation.Id, cancellationToken).Returns(invitation);
		var command = new ResendInvitationCommand(DefaultOrgId, invitation.Id, DefaultRequestingUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
		invitation.Status.Should().Be(InvitationStatus.Pending);
		invitation.ExpiresOn.Should().BeAfter(DateTimeOffset.UtcNow.AddDays(OrganizationInvitation.ExpiryWindowDays - 1));
		await _unitOfWork.Received(1).SaveChangesAsync(cancellationToken);
		await _emailService.Received(1).SendAsync(
			Arg.Is<EmailMessage>(m => m.To == "vera@test.de"), cancellationToken);
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenInvitationDoesNotExist(
		CancellationToken cancellationToken)
	{
		// Arrange
		var invitationId = OrganizationInvitationId.New();
		_invitationRepo.FindAsync(invitationId, cancellationToken).Returns((OrganizationInvitation?)null);
		var command = new ResendInvitationCommand(DefaultOrgId, invitationId, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.NotFound);
		await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldThrow_WhenInvitationBelongsToADifferentOrganization(
		CancellationToken cancellationToken)
	{
		// Arrange
		var otherOrgId = OrganizationId.New();
		var invitation = CreateExpiredInvitation(otherOrgId);
		_invitationRepo.FindAsync(invitation.Id, cancellationToken).Returns(invitation);
		var command = new ResendInvitationCommand(DefaultOrgId, invitation.Id, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.Validation);
		await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldExtendExpiryAndSendEmail_WhenInvitationIsStillPending(
		CancellationToken cancellationToken)
	{
		// Arrange
		var invitation = CreatePendingInvitation(DefaultOrgId);
		var originalExpiry = invitation.ExpiresOn;
		_invitationRepo.FindAsync(invitation.Id, cancellationToken).Returns(invitation);
		var command = new ResendInvitationCommand(DefaultOrgId, invitation.Id, DefaultRequestingUserId);

		// Act
		var result = await _sut.Handle(command, cancellationToken);

		// Assert
		result.Should().BeTrue();
		invitation.Status.Should().Be(InvitationStatus.Pending);
		invitation.ExpiresOn.Should().BeOnOrAfter(originalExpiry);
		await _unitOfWork.Received(1).SaveChangesAsync(cancellationToken);
		await _emailService.Received(1).SendAsync(
			Arg.Is<EmailMessage>(m => m.To == "vera@test.de"), cancellationToken);
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task Handle_ShouldThrowConflict_WhenInvitationIsAlreadyAnswered(
		bool accepted,
		CancellationToken cancellationToken)
	{
		// Arrange
		var invitation = CreatePendingInvitation(DefaultOrgId);
		if (accepted)
			invitation.Accept().ThrowIfFailure();
		else
			invitation.Decline().ThrowIfFailure();
		_invitationRepo.FindAsync(invitation.Id, cancellationToken).Returns(invitation);
		var command = new ResendInvitationCommand(DefaultOrgId, invitation.Id, DefaultRequestingUserId);

		// Act
		Func<Task> act = async () => await _sut.Handle(command, cancellationToken);

		// Assert
		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Type.Should().Be(ErrorType.Conflict);
		await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
		await _emailService.DidNotReceive().SendAsync(
			Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Handle_ShouldRenderInvitationEmail_InInviteesPreferredLanguage(
		CancellationToken cancellationToken)
	{
		// Arrange
		var invitation = CreateExpiredInvitation(DefaultOrgId);
		_invitationRepo.FindAsync(invitation.Id, cancellationToken).Returns(invitation);
		var invitee = User.Create(DefaultInviteeId);
		invitee.SetPreferredLanguage("en");
		_dbContext.GetOrCreateUsersAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
			.Returns([invitee]);
		var command = new ResendInvitationCommand(DefaultOrgId, invitation.Id, DefaultRequestingUserId);

		// Act
		await _sut.Handle(command, cancellationToken);

		// Assert
		_emailTemplateRenderer.Received(1).Render(Arg.Is<EmailDraft>(d =>
			d.Kind == EmailTemplateKind.InvitationReceived
			&& d.Language == "en"
			&& d.RecipientName == "Vera"
			&& d.Placeholders["OrganizationName"] == "Test Org"
			&& d.Placeholders["ExpiryDays"] == "14"));
	}
}
