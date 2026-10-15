using Application.Common.Exceptions;
using Application.Common.Persistence;
using Application.Engagements.CancelMeetingAsOrganizer.v1;
using Application.Engagements.ProposeMeeting.v1;
using Application.Engagements.ProposeMeetingAsVolunteer.v1;
using Application.Engagements.RespondToMeetingAsOrganizer.v1;
using Application.Engagements.RespondToMeetingAsVolunteer.v1;
using AwesomeAssertions;
using Domain.Engagements;
using Domain.Notifications;
using Domain.Organizations;
using Domain.Users;
using Domain.VolunteerOpportunities;
using NSubstitute;

namespace Application.UnitTests.Engagements.Meetings;

public class MeetingCommandHandlerTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Start = Now.AddDays(3);
	private static readonly DateTimeOffset End = Start.AddHours(1);

	private readonly IApplicationDbContext _dbContext = Substitute.For<IApplicationDbContext>();
	private readonly IAggregateRepository<Engagement, EngagementId> _engagementRepo =
		Substitute.For<IAggregateRepository<Engagement, EngagementId>>();
	private readonly IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId> _opportunityRepo =
		Substitute.For<IAggregateRepository<VolunteerOpportunity, VolunteerOpportunityId>>();
	private readonly IAggregateRepository<Notification, NotificationId> _notificationRepo =
		Substitute.For<IAggregateRepository<Notification, NotificationId>>();
	private readonly FakeTimeProvider _timeProvider = new(Now);
	private readonly UserId _organizerId = UserId.New();

	public MeetingCommandHandlerTests()
	{
		_dbContext.Engagements.Returns(_engagementRepo);
		_dbContext.VolunteerOpportunities.Returns(_opportunityRepo);
		_dbContext.Notifications.Returns(_notificationRepo);
		_dbContext.IsOrganizerAsync(Arg.Any<OrganizationId>(), _organizerId, Arg.Any<CancellationToken>())
			.Returns(true);
		_dbContext.GetOrganizerUserIdsAsync(Arg.Any<OrganizationId>(), Arg.Any<CancellationToken>())
			.Returns([_organizerId.Value]);
	}

	private VolunteerOpportunity Opportunity(ParticipationType participationType = ParticipationType.IndividualContact)
	{
		var opportunity = VolunteerOpportunity.Create(
			OrganizationId.New(), "Kennenlernen", null, "Beschreibung", null, isRemote: true, address: null,
			Occurrence.OneTime, participationType, CheckInMethod.None, Substitute.For<IPinGenerator>(),
			status: OpportunityStatus.Draft,
			validUntil: participationType == ParticipationType.IndividualContact ? Now.AddDays(1) : null,
			now: Now).Value;
		_opportunityRepo.FindAsync(opportunity.Id, Arg.Any<CancellationToken>()).Returns(opportunity);
		return opportunity;
	}

	private Engagement Interest(VolunteerOpportunity opportunity, UserId? volunteerId = null)
	{
		var engagement = Engagement.CreateIndividualContact(opportunity.Id, volunteerId ?? UserId.New(), "Ich helfe gern.").Value;
		_engagementRepo.FindAsync(engagement.Id, Arg.Any<CancellationToken>()).Returns(engagement);
		return engagement;
	}

	private void ActiveEngagements(VolunteerOpportunity opportunity, params Engagement[] engagements)
	{
		_dbContext.GetEngagementsByIdsAsync(Arg.Any<IReadOnlyCollection<EngagementId>>(), Arg.Any<CancellationToken>())
			.Returns(engagements.ToList());
		_dbContext.GetActiveEngagementsForOpportunityAsync(opportunity.Id, Arg.Any<CancellationToken>())
			.Returns(engagements.ToList());
	}

	private ProposeMeetingCommand Proposal(VolunteerOpportunity opportunity, params Engagement[] engagements) =>
		new(opportunity.Id, engagements.Select(e => e.Id).ToList(), Start, End, "Vereinsheim", null, _organizerId);

	[Test]
	public async Task ProposeMeeting_ShouldGiveEveryParticipantTheSameMeeting_AndNotifyEach(CancellationToken cancellationToken)
	{
		var opportunity = Opportunity();
		var first = Interest(opportunity);
		var second = Interest(opportunity);
		ActiveEngagements(opportunity, first, second);
		var sut = new ProposeMeetingCommandHandler(_dbContext, _timeProvider);

		var meetingId = await sut.Handle(Proposal(opportunity, first, second), cancellationToken);

		first.Meeting!.Id.Should().Be(meetingId);
		second.Meeting!.Id.Should().Be(meetingId);
		await _notificationRepo.Received(2).AddAsync(
			Arg.Is<Notification>(n => n.Kind == NotificationKind.MeetingProposed),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task ProposeMeeting_ShouldMoveGroupMeeting_ForEveryoneStillCarryingIt(CancellationToken cancellationToken)
	{
		var opportunity = Opportunity();
		var first = Interest(opportunity);
		var second = Interest(opportunity);
		ActiveEngagements(opportunity, first, second);
		var sut = new ProposeMeetingCommandHandler(_dbContext, _timeProvider);
		var original = await sut.Handle(Proposal(opportunity, first, second), cancellationToken);

		var moved = await sut.Handle(
			Proposal(opportunity) with { StartsAt = Start.AddDays(1), EndsAt = End.AddDays(1), ReplacesMeetingId = original },
			cancellationToken);

		moved.Should().NotBe(original);
		first.Meeting!.Id.Should().Be(moved);
		second.Meeting!.Id.Should().Be(moved);
		first.Meeting.StartsAt.Should().Be(Start.AddDays(1));
	}

	[Test]
	public async Task ProposeMeeting_ShouldFail_WhenReplacedMeetingIsGone(CancellationToken cancellationToken)
	{
		var opportunity = Opportunity();
		ActiveEngagements(opportunity, Interest(opportunity));
		var sut = new ProposeMeetingCommandHandler(_dbContext, _timeProvider);

		var act = async () => await sut.Handle(Proposal(opportunity) with { ReplacesMeetingId = MeetingId.New() }, cancellationToken);

		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Code.Should().Be("Meeting.Superseded");
	}

	[Test]
	public async Task ProposeMeeting_ShouldReject_ScheduledSlotsOpportunity(CancellationToken cancellationToken)
	{
		var opportunity = Opportunity(ParticipationType.ScheduledSlots);
		var engagement = Interest(opportunity);
		ActiveEngagements(opportunity, engagement);
		var sut = new ProposeMeetingCommandHandler(_dbContext, _timeProvider);

		var act = async () => await sut.Handle(Proposal(opportunity, engagement), cancellationToken);

		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Code.Should().Be("Meeting.RequiresIndividualContact");
	}

	[Test]
	public async Task ProposeMeeting_ShouldReject_EngagementOfAnotherOpportunity(CancellationToken cancellationToken)
	{
		var opportunity = Opportunity();
		var foreign = Interest(Opportunity());
		ActiveEngagements(opportunity, foreign);
		var sut = new ProposeMeetingCommandHandler(_dbContext, _timeProvider);

		var act = async () => await sut.Handle(Proposal(opportunity, foreign), cancellationToken);

		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Code.Should().Be("Engagement.NotFound");
		foreign.Meeting.Should().BeNull();
	}

	[Test]
	public async Task ProposeMeeting_ShouldReject_NonOrganizer(CancellationToken cancellationToken)
	{
		var opportunity = Opportunity();
		var engagement = Interest(opportunity);
		ActiveEngagements(opportunity, engagement);
		var sut = new ProposeMeetingCommandHandler(_dbContext, _timeProvider);

		var act = async () => await sut.Handle(Proposal(opportunity, engagement) with { RequestingUserId = UserId.New() }, cancellationToken);

		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Code.Should().Be("Organization.NotOrganizer");
	}

	[Test]
	public async Task ProposeMeeting_ShouldReject_EmptyParticipantList(CancellationToken cancellationToken)
	{
		var opportunity = Opportunity();
		var sut = new ProposeMeetingCommandHandler(_dbContext, _timeProvider);

		var act = async () => await sut.Handle(Proposal(opportunity), cancellationToken);

		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Code.Should().Be("Meeting.NoParticipants");
	}

	[Test]
	public async Task CancelMeetingAsOrganizer_ShouldCancelForEveryParticipant(CancellationToken cancellationToken)
	{
		var opportunity = Opportunity();
		var first = Interest(opportunity);
		var second = Interest(opportunity);
		ActiveEngagements(opportunity, first, second);
		var meetingId = await new ProposeMeetingCommandHandler(_dbContext, _timeProvider)
			.Handle(Proposal(opportunity, first, second), cancellationToken);
		_notificationRepo.ClearReceivedCalls();
		var sut = new CancelMeetingAsOrganizerCommandHandler(_dbContext, _timeProvider);

		var cancelled = await sut.Handle(new CancelMeetingAsOrganizerCommand(opportunity.Id, meetingId, _organizerId), cancellationToken);

		cancelled.Should().Be(2);
		first.Meeting.Should().BeNull();
		second.Meeting.Should().BeNull();
		await _notificationRepo.Received(2).AddAsync(
			Arg.Is<Notification>(n => n.Kind == NotificationKind.MeetingCancelled),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task CancelMeetingAsOrganizer_ShouldFail_ForUnknownMeeting(CancellationToken cancellationToken)
	{
		var opportunity = Opportunity();
		ActiveEngagements(opportunity, Interest(opportunity));
		var sut = new CancelMeetingAsOrganizerCommandHandler(_dbContext, _timeProvider);

		var act = async () => await sut.Handle(new CancelMeetingAsOrganizerCommand(opportunity.Id, MeetingId.New(), _organizerId), cancellationToken);

		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Code.Should().Be("Meeting.Superseded");
	}

	[Test]
	public async Task ProposeMeetingAsVolunteer_ShouldNotifyOrganizers(CancellationToken cancellationToken)
	{
		var opportunity = Opportunity();
		var volunteerId = UserId.New();
		var engagement = Interest(opportunity, volunteerId);
		var sut = new ProposeMeetingAsVolunteerCommandHandler(_dbContext, _timeProvider);

		var meetingId = await sut.Handle(
			new ProposeMeetingAsVolunteerCommand(engagement.Id, Start, End, "Online", null, volunteerId.Value),
			cancellationToken);

		engagement.Meeting!.Id.Should().Be(meetingId);
		engagement.Meeting.ProposedBy.Should().Be(MeetingParty.Volunteer);
		await _notificationRepo.Received(1).AddAsync(
			Arg.Is<Notification>(n => n.Kind == NotificationKind.MeetingProposed && n.RecipientId == _organizerId),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task ProposeMeetingAsVolunteer_ShouldReject_SomeoneElse(CancellationToken cancellationToken)
	{
		var opportunity = Opportunity();
		var engagement = Interest(opportunity);
		var sut = new ProposeMeetingAsVolunteerCommandHandler(_dbContext, _timeProvider);

		var act = async () => await sut.Handle(
			new ProposeMeetingAsVolunteerCommand(engagement.Id, Start, End, "Online", null, Guid.NewGuid()),
			cancellationToken);

		(await act.Should().ThrowAsync<ResultFailureException>())
			.Which.Error.Code.Should().Be("Engagement.NotOwner");
	}

	[Test]
	public async Task RespondToMeetingAsVolunteer_ShouldAcceptAndNotifyOrganizers(CancellationToken cancellationToken)
	{
		var opportunity = Opportunity();
		var volunteerId = UserId.New();
		var engagement = Interest(opportunity, volunteerId);
		ActiveEngagements(opportunity, engagement);
		var meetingId = await new ProposeMeetingCommandHandler(_dbContext, _timeProvider)
			.Handle(Proposal(opportunity, engagement), cancellationToken);
		var sut = new RespondToMeetingAsVolunteerCommandHandler(_dbContext, _timeProvider);

		await sut.Handle(new RespondToMeetingAsVolunteerCommand(engagement.Id, meetingId, Accept: true, volunteerId.Value), cancellationToken);

		engagement.Meeting!.Response.Should().Be(MeetingResponse.Accepted);
		await _notificationRepo.Received(1).AddAsync(
			Arg.Is<Notification>(n => n.Kind == NotificationKind.MeetingAccepted && n.RecipientId == _organizerId),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task RespondToMeetingAsOrganizer_ShouldDeclineVolunteerProposal_AndNotifyVolunteer(CancellationToken cancellationToken)
	{
		var opportunity = Opportunity();
		var volunteerId = UserId.New();
		var engagement = Interest(opportunity, volunteerId);
		ActiveEngagements(opportunity, engagement);
		var meetingId = await new ProposeMeetingAsVolunteerCommandHandler(_dbContext, _timeProvider)
			.Handle(new ProposeMeetingAsVolunteerCommand(engagement.Id, Start, End, "Online", null, volunteerId.Value), cancellationToken);
		var sut = new RespondToMeetingAsOrganizerCommandHandler(_dbContext, _timeProvider);

		await sut.Handle(new RespondToMeetingAsOrganizerCommand(opportunity.Id, meetingId, Accept: false, _organizerId), cancellationToken);

		engagement.Meeting!.Response.Should().Be(MeetingResponse.Declined);
		await _notificationRepo.Received(1).AddAsync(
			Arg.Is<Notification>(n => n.Kind == NotificationKind.MeetingDeclined && n.RecipientId == volunteerId),
			Arg.Any<CancellationToken>());
	}

	private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => utcNow;
	}
}
