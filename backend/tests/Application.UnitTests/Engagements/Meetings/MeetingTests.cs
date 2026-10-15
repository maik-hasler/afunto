using AwesomeAssertions;
using Domain.Engagements;
using Domain.Users;
using Domain.VolunteerOpportunities;

namespace Application.UnitTests.Engagements.Meetings;

public class MeetingTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Start = Now.AddDays(3);
	private static readonly DateTimeOffset End = Start.AddHours(1);

	private static Engagement InterestEngagement() =>
		Engagement.CreateIndividualContact(VolunteerOpportunityId.New(), UserId.New(), "Ich helfe gern.").Value;

	private static MeetingId ProposeByOrganization(Engagement engagement, DateTimeOffset? start = null)
	{
		var meetingId = MeetingId.New();
		var startsAt = start ?? Start;
		engagement.ProposeMeeting(meetingId, MeetingParty.Organization, startsAt, startsAt.AddHours(1), "Vereinsheim, Hauptstraße 1", null, Now)
			.IsSuccess.Should().BeTrue();
		return meetingId;
	}

	[Test]
	public void ProposeMeeting_ShouldAttachPendingMeeting()
	{
		var engagement = InterestEngagement();
		var meetingId = MeetingId.New();

		var result = engagement.ProposeMeeting(meetingId, MeetingParty.Organization, Start, End, "  https://meet.example.org/abc  ", "  Kurzes Kennenlernen  ", Now);

		result.IsSuccess.Should().BeTrue();
		engagement.Meeting.Should().NotBeNull();
		engagement.Meeting!.Id.Should().Be(meetingId);
		engagement.Meeting.ProposedBy.Should().Be(MeetingParty.Organization);
		engagement.Meeting.Response.Should().Be(MeetingResponse.Pending);
		engagement.Meeting.Location.Should().Be("https://meet.example.org/abc");
		engagement.Meeting.Note.Should().Be("Kurzes Kennenlernen");
		engagement.MeetingProposalCount.Should().Be(1);
	}

	[Test]
	public void ProposeMeeting_ShouldStoreBlankNoteAsNull()
	{
		var engagement = InterestEngagement();

		engagement.ProposeMeeting(MeetingId.New(), MeetingParty.Volunteer, Start, End, "Online", "   ", Now);

		engagement.Meeting!.Note.Should().BeNull();
	}

	[Test]
	public void ProposeMeeting_ShouldReplaceTheCurrentProposal()
	{
		var engagement = InterestEngagement();
		var first = ProposeByOrganization(engagement);
		var second = MeetingId.New();

		var result = engagement.ProposeMeeting(second, MeetingParty.Volunteer, Start.AddDays(1), End.AddDays(1), "Online", null, Now);

		result.IsSuccess.Should().BeTrue();
		engagement.Meeting!.Id.Should().Be(second).And.NotBe(first);
		engagement.Meeting.ProposedBy.Should().Be(MeetingParty.Volunteer);
		engagement.MeetingProposalCount.Should().Be(2);
	}

	[Test]
	public void ProposeMeeting_ShouldFail_ForSlotSignUp()
	{
		var engagement = Engagement.CreateSlotSignUp(VolunteerOpportunityId.New(), UserId.New(), TimeSlotId.New());

		var result = engagement.ProposeMeeting(MeetingId.New(), MeetingParty.Organization, Start, End, "Online", null, Now);

		result.Error.Code.Should().Be("Meeting.RequiresIndividualContact");
	}

	[Test]
	public void ProposeMeeting_ShouldFail_WhenEngagementIsWithdrawn()
	{
		var engagement = InterestEngagement();
		engagement.Withdraw();

		var result = engagement.ProposeMeeting(MeetingId.New(), MeetingParty.Organization, Start, End, "Online", null, Now);

		result.Error.Code.Should().Be("Engagement.AlreadyTerminated");
	}

	[Test]
	public void ProposeMeeting_ShouldFail_WhenEngagementIsAnonymized()
	{
		var engagement = InterestEngagement();
		engagement.Anonymize();

		var result = engagement.ProposeMeeting(MeetingId.New(), MeetingParty.Organization, Start, End, "Online", null, Now);

		result.Error.Code.Should().Be("Engagement.Anonymized");
	}

	[Test]
	[Arguments(-1, 1, "Meeting.StartsInPast")]
	[Arguments(0, 1, "Meeting.StartsInPast")]
	[Arguments(400 * 24, 1, "Meeting.TooFarAhead")]
	[Arguments(24, 0, "Meeting.EndBeforeStart")]
	[Arguments(24, -1, "Meeting.EndBeforeStart")]
	[Arguments(24, 13, "Meeting.TooLong")]
	public void ProposeMeeting_ShouldReject_InvalidTimes(int startOffsetHours, int durationHours, string expectedCode)
	{
		var engagement = InterestEngagement();
		var startsAt = Now.AddHours(startOffsetHours);

		var result = engagement.ProposeMeeting(MeetingId.New(), MeetingParty.Organization, startsAt, startsAt.AddHours(durationHours), "Online", null, Now);

		result.Error.Code.Should().Be(expectedCode);
		engagement.Meeting.Should().BeNull();
		engagement.MeetingProposalCount.Should().Be(0);
	}

	[Test]
	[Arguments(null, "Meeting.LocationRequired")]
	[Arguments("   ", "Meeting.LocationRequired")]
	public void ProposeMeeting_ShouldRequireLocation(string? location, string expectedCode)
	{
		var engagement = InterestEngagement();

		var result = engagement.ProposeMeeting(MeetingId.New(), MeetingParty.Organization, Start, End, location, null, Now);

		result.Error.Code.Should().Be(expectedCode);
	}

	[Test]
	public void ProposeMeeting_ShouldReject_TooLongLocationAndNote()
	{
		var engagement = InterestEngagement();

		engagement.ProposeMeeting(MeetingId.New(), MeetingParty.Organization, Start, End, new string('a', Meeting.MaxLocationLength + 1), null, Now)
			.Error.Code.Should().Be("Meeting.LocationTooLong");
		engagement.ProposeMeeting(MeetingId.New(), MeetingParty.Organization, Start, End, "Online", new string('a', Meeting.MaxNoteLength + 1), Now)
			.Error.Code.Should().Be("Meeting.NoteTooLong");
	}

	[Test]
	public void ProposeMeeting_ShouldFail_WhenProposalLimitIsReached()
	{
		var engagement = InterestEngagement();
		for (var i = 0; i < Engagement.MaxMeetingProposals; i++)
			ProposeByOrganization(engagement);

		var result = engagement.ProposeMeeting(MeetingId.New(), MeetingParty.Volunteer, Start, End, "Online", null, Now);

		result.Error.Code.Should().Be("Meeting.ProposalLimitReached");
	}

	[Test]
	public void RespondToMeeting_ShouldAccept_WhenOtherSideAnswers()
	{
		var engagement = InterestEngagement();
		var meetingId = ProposeByOrganization(engagement);

		var result = engagement.RespondToMeeting(meetingId, MeetingParty.Volunteer, accept: true, Now);

		result.IsSuccess.Should().BeTrue();
		engagement.Meeting!.Response.Should().Be(MeetingResponse.Accepted);
	}

	[Test]
	public void RespondToMeeting_ShouldAllowDeclining_AfterAccepting()
	{
		var engagement = InterestEngagement();
		var meetingId = ProposeByOrganization(engagement);
		engagement.RespondToMeeting(meetingId, MeetingParty.Volunteer, accept: true, Now);

		var result = engagement.RespondToMeeting(meetingId, MeetingParty.Volunteer, accept: false, Now);

		result.IsSuccess.Should().BeTrue();
		engagement.Meeting!.Response.Should().Be(MeetingResponse.Declined);
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public void RespondToMeeting_ShouldFail_AfterDeclining(bool accept)
	{
		var engagement = InterestEngagement();
		var meetingId = ProposeByOrganization(engagement);
		engagement.RespondToMeeting(meetingId, MeetingParty.Volunteer, accept: false, Now);

		var result = engagement.RespondToMeeting(meetingId, MeetingParty.Volunteer, accept, Now);

		result.Error.Code.Should().Be("Meeting.AlreadyAnswered");
	}

	[Test]
	public void RespondToMeeting_ShouldFail_ForOwnProposal()
	{
		var engagement = InterestEngagement();
		var meetingId = ProposeByOrganization(engagement);

		var result = engagement.RespondToMeeting(meetingId, MeetingParty.Organization, accept: true, Now);

		result.Error.Code.Should().Be("Meeting.OwnProposal");
	}

	[Test]
	public void RespondToMeeting_ShouldFail_ForReplacedProposal()
	{
		var engagement = InterestEngagement();
		var stale = ProposeByOrganization(engagement);
		ProposeByOrganization(engagement, Start.AddDays(1));

		var result = engagement.RespondToMeeting(stale, MeetingParty.Volunteer, accept: true, Now);

		result.Error.Code.Should().Be("Meeting.Superseded");
		engagement.Meeting!.Response.Should().Be(MeetingResponse.Pending);
	}

	[Test]
	public void RespondToMeeting_ShouldFail_WhenMeetingIsOver()
	{
		var engagement = InterestEngagement();
		var meetingId = ProposeByOrganization(engagement);

		var result = engagement.RespondToMeeting(meetingId, MeetingParty.Volunteer, accept: true, End);

		result.Error.Code.Should().Be("Meeting.AlreadyOver");
	}

	[Test]
	public void CancelMeeting_ShouldRemoveMeeting_WhenProposerCancels()
	{
		var engagement = InterestEngagement();
		var meetingId = ProposeByOrganization(engagement);

		var result = engagement.CancelMeeting(meetingId, MeetingParty.Organization, Now);

		result.IsSuccess.Should().BeTrue();
		engagement.Meeting.Should().BeNull();
	}

	[Test]
	public void CancelMeeting_ShouldFail_ForTheOtherSide()
	{
		var engagement = InterestEngagement();
		var meetingId = ProposeByOrganization(engagement);

		var result = engagement.CancelMeeting(meetingId, MeetingParty.Volunteer, Now);

		result.Error.Code.Should().Be("Meeting.NotProposer");
		engagement.Meeting.Should().NotBeNull();
	}

	[Test]
	public void CancelMeeting_ShouldFail_WithoutMeeting()
	{
		var engagement = InterestEngagement();

		var result = engagement.CancelMeeting(MeetingId.New(), MeetingParty.Organization, Now);

		result.Error.Code.Should().Be("Meeting.Superseded");
	}

	[Test]
	public void Withdraw_ShouldDropMeeting()
	{
		var engagement = InterestEngagement();
		ProposeByOrganization(engagement);

		engagement.Withdraw();

		engagement.Meeting.Should().BeNull();
	}

	[Test]
	public void Cancel_ShouldDropMeeting()
	{
		var engagement = InterestEngagement();
		ProposeByOrganization(engagement);

		engagement.Cancel();

		engagement.Meeting.Should().BeNull();
	}

	[Test]
	public void Anonymize_ShouldDropMeeting()
	{
		var engagement = InterestEngagement();
		ProposeByOrganization(engagement);

		engagement.Anonymize();

		engagement.Meeting.Should().BeNull();
	}

	[Test]
	public void Reactivate_ShouldNotBringBackOldMeeting()
	{
		var engagement = InterestEngagement();
		ProposeByOrganization(engagement);
		engagement.Withdraw();

		engagement.Reactivate(timeSlotId: null, message: "Doch wieder dabei.");

		engagement.Meeting.Should().BeNull();
	}
}
