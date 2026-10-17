namespace Domain.Engagements;

/// <summary>
/// Where a meeting takes place. Decides what <see cref="Meeting.Location"/> holds:
/// an address or meeting point for <see cref="InPerson"/>, an https link for
/// <see cref="Online"/>.
/// </summary>
public enum MeetingFormat
{
	InPerson,
	Online
}
