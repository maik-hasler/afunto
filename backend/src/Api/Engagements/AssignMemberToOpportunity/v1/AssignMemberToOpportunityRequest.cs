using System.ComponentModel.DataAnnotations;

namespace Api.Engagements.AssignMemberToOpportunity.v1;

public sealed record AssignMemberToOpportunityRequest(
	Guid MemberId,
	Guid? TimeSlotId,
	[MaxLength(500)] string? Message);
