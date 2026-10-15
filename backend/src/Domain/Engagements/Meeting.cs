using Domain.Primitives;

namespace Domain.Engagements;

/// <summary>
/// A proposal to meet in person or online, attached to one engagement. A proposal
/// is never edited: moving a meeting means proposing a new one, which replaces it
/// (<see cref="Engagement.ProposeMeeting"/>). That keeps an answer from landing on a
/// time the answering side never saw. A group meeting is the same proposal, sharing
/// one <see cref="Id"/>, on several engagements of one opportunity.
/// </summary>
public sealed record Meeting : IValueObject
{
	public const int MaxLocationLength = 300;

	public const int MaxNoteLength = 500;

	public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(12);

	public static readonly TimeSpan MaxLeadTime = TimeSpan.FromDays(365);

	public MeetingId Id { get; }

	public DateTimeOffset StartsAt { get; }

	public DateTimeOffset EndsAt { get; }

	public string Location { get; }

	public string? Note { get; }

	public MeetingParty ProposedBy { get; }

	public MeetingResponse Response { get; }

	private Meeting(
		MeetingId id,
		DateTimeOffset startsAt,
		DateTimeOffset endsAt,
		string location,
		string? note,
		MeetingParty proposedBy,
		MeetingResponse response)
	{
		Id = id;
		StartsAt = startsAt;
		EndsAt = endsAt;
		Location = location;
		Note = note;
		ProposedBy = proposedBy;
		Response = response;
	}

	public static Result<Meeting> Propose(
		MeetingId id,
		MeetingParty proposedBy,
		DateTimeOffset startsAt,
		DateTimeOffset endsAt,
		string? location,
		string? note,
		DateTimeOffset now)
	{
		if (startsAt <= now)
			return Result.Failure<Meeting>(Error.Validation("Meeting.StartsInPast", "A meeting has to start in the future."));

		if (startsAt > now + MaxLeadTime)
			return Result.Failure<Meeting>(Error.Validation("Meeting.TooFarAhead", "A meeting can be proposed at most one year ahead."));

		if (endsAt <= startsAt)
			return Result.Failure<Meeting>(Error.Validation("Meeting.EndBeforeStart", "A meeting has to end after it starts."));

		if (endsAt - startsAt > MaxDuration)
			return Result.Failure<Meeting>(Error.Validation("Meeting.TooLong", "A meeting can last at most 12 hours."));

		var trimmedLocation = location?.Trim();
		if (string.IsNullOrEmpty(trimmedLocation))
			return Result.Failure<Meeting>(Error.Validation("Meeting.LocationRequired", "A meeting needs a place or an online link."));

		if (trimmedLocation.Length > MaxLocationLength)
			return Result.Failure<Meeting>(Error.Validation("Meeting.LocationTooLong", $"The place must not exceed {MaxLocationLength} characters."));

		var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
		if (trimmedNote is not null && trimmedNote.Length > MaxNoteLength)
			return Result.Failure<Meeting>(Error.Validation("Meeting.NoteTooLong", $"The note must not exceed {MaxNoteLength} characters."));

		return new Meeting(id, startsAt, endsAt, trimmedLocation, trimmedNote, proposedBy, MeetingResponse.Pending);
	}

	public bool IsOver(DateTimeOffset now) => EndsAt <= now;

	internal Meeting WithResponse(MeetingResponse response) =>
		new(Id, StartsAt, EndsAt, Location, Note, ProposedBy, response);
}
