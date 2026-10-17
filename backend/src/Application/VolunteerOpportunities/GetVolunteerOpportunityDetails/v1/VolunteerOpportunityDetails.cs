namespace Application.VolunteerOpportunities.GetVolunteerOpportunityDetails.v1;

public sealed record VolunteerOpportunityDetails(
	Guid Id,
	string TitleDe,
	string? TitleEn,
	string? DescriptionDe,
	string? DescriptionEn,
	Guid OrganizationId,
	string OrganizationName,
	string? Street,
	string? HouseNumber,
	string? ZipCode,
	string? City,
	double? Latitude,
	double? Longitude,
	bool IsRemote,
	string Occurrence,
	string ParticipationType,
	string CheckInMethod,
	string? Category,
	IReadOnlyList<string> Tags,
	IReadOnlyList<TimeSlotDetail> TimeSlots,
	DateTimeOffset CreatedOn,
	DateTimeOffset? ValidUntil,
	int CurrentParticipantCount,
	string Status,
	string? BannerImageUrl,
	IReadOnlyList<CurrentUserEngagementInfo> CurrentUserEngagements,
	bool MembersOnly = false);

public sealed record TimeSlotDetail(
	Guid Id,
	DateTimeOffset StartDateTime,
	DateTimeOffset EndDateTime,
	int? MaxParticipants,
	int BookedCount,
	Guid? SeriesId,
	string? RecurrenceFrequency,
	int? RecurrenceCount);

public sealed record CurrentUserEngagementInfo(
	Guid Id,
	string Status,
	Guid? TimeSlotId,
	bool IsCheckedIn,

	int RemainingReactivations);
