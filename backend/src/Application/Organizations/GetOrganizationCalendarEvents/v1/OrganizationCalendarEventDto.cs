namespace Application.Organizations.GetOrganizationCalendarEvents.v1;

public sealed record OrganizationCalendarEventDto(
	Guid OpportunityId,
	string TitleDe,
	string? TitleEn,
	string? Color,
	// The opportunity's own status, so a caller can tell a live shift from a
	// draft nobody can sign up to yet. The calendar deliberately shows both -
	// it is where an organizer plans - but a list of what still needs staffing
	// must not present a draft as a shift that is short of people.
	string Status,
	IReadOnlyList<CalendarTimeSlotDto> TimeSlots,
	// Internal opportunities look like any other shift on the board, so organizers get
	// told - nobody should assume a members-only shift is publicly staffed (afunto#2406).
	bool MembersOnly = false);

public sealed record CalendarTimeSlotDto(
	Guid TimeSlotId,
	DateTimeOffset StartDateTime,
	DateTimeOffset EndDateTime,
	int? MaxParticipants,
	int BookedCount);
