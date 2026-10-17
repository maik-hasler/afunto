using Application.Common.Messaging;
using Domain.Common;
using Domain.Users;
using Domain.VolunteerOpportunities;

namespace Application.VolunteerOpportunities.UpdateVolunteerOpportunity.v1;

public sealed record UpdateVolunteerOpportunityCommand(
	Guid OpportunityId,
	string TitleDe,
	string? TitleEn,
	string DescriptionDe,
	string? DescriptionEn,
	bool IsRemote,
	Address? Address,
	Occurrence Occurrence,
	ParticipationType ParticipationType,
	CheckInMethod CheckInMethod,
	Category? Category,
	List<string> Tags,
	UserId RequestingUserId,
	string? CheckInPin = null,
	DateTimeOffset? ValidUntil = null,
	bool MembersOnly = false)
	: ICommand<bool>;
