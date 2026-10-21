using Application.Common.Messaging;
using Domain.Common;
using Domain.Organizations;
using Domain.Users;
using Domain.VolunteerOpportunities;

namespace Application.VolunteerOpportunities.CreateVolunteerOpportunity.v1;

public sealed record CreateVolunteerOpportunityCommand(
	string TitleDe,
	string? TitleEn,
	string DescriptionDe,
	string? DescriptionEn,
	OrganizationId OrganizationId,
	bool IsRemote,
	Address? Address,
	Occurrence Occurrence,
	ParticipationType ParticipationType,
	CheckInMethod CheckInMethod,
	Category? Category,
	List<string> Tags,
	OpportunityStatus Status,
	UserId RequestingUserId,
	string? CheckInPin = null,
	DateTimeOffset? ValidUntil = null,
	bool MembersOnly = false)
	: ICommand<VolunteerOpportunity>;
