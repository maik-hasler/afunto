namespace Application.Engagements;

public sealed record EngagementSummary(
	Guid Id,
	Guid OpportunityId,
	string? OpportunityTitle,
	Guid? OrganizationId,
	string? OrganizationName,
	Guid? VolunteerId,
	Guid? TimeSlotId,
	string? Message,
	string Status,
	bool IsCheckedIn,
	bool HasFeedback,
	DateTimeOffset CreatedOn,
	string? VolunteerName = null,
	DateTimeOffset? TimeSlotStartDateTime = null,
	DateTimeOffset? TimeSlotEndDateTime = null,
	string? Location = null,
	string? VolunteerEmail = null,
	string? VolunteerPhone = null,
	string? CancellationReason = null,
	int? FeedbackRating = null,
	string? FeedbackComment = null,
	DateTimeOffset? FeedbackSubmittedAt = null,
	string CheckInMethod = "None",

	DateTimeOffset? OpportunityValidUntil = null,

	int? RemainingReactivations = null,

	// The German title travels in OpportunityTitle, matching every other
	// read model; this carries the optional English one so a client showing
	// the English UI can render the same title the opportunity's own page
	// does instead of falling back to German (#2328).
	string? OpportunityTitleEn = null,

	EngagementMeetingSummary? Meeting = null);
