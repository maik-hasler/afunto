using Application.Common.Exceptions;
using Application.Common.Pagination;
using Application.Engagements;
using Domain.Common;
using Domain.Engagements;
using Domain.Organizations;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

internal sealed class EngagementReadRepository(
	ApplicationDbContext dbContext)
	: IEngagementReadRepository
{
	public async ValueTask<List<EngagementSummary>> GetByOpportunityAsync(
		VolunteerOpportunityId opportunityId,
		CancellationToken cancellationToken = default)
	{
		var raw = await dbContext.EngagementsQuery
			.Where(e => e.OpportunityId == opportunityId)
			.Join(
				dbContext.VolunteerOpportunitiesQuery.Select(o => new { o.Id, o.TitleDe, o.TitleEn, o.OrganizationId, o.CheckInMethod }),
				e => e.OpportunityId,
				o => o.Id,
				(e, o) => new
				{
					e.Id,
					e.OpportunityId,
					OpportunityTitle = o.TitleDe,
					OpportunityTitleEn = o.TitleEn,
					o.OrganizationId,
					o.CheckInMethod,
					e.VolunteerId,
					e.TimeSlotId,
					e.Message,
					e.Status,
					e.IsCheckedIn,
					e.FeedbackSubmittedAt,
					e.CreatedOn,
					e.CancellationReason,
				})
			.Join(
				dbContext.OrganizationsQuery.Select(org => new { org.Id, org.Name }),
				x => x.OrganizationId,
				org => org.Id,
				(x, org) => new
				{
					x.Id,
					x.OpportunityId,
					x.OpportunityTitle,
					x.OpportunityTitleEn,
					OrganizationId = org.Id,
					OrganizationName = org.Name,
					x.CheckInMethod,
					x.VolunteerId,
					x.TimeSlotId,
					x.Message,
					x.Status,
					x.IsCheckedIn,
					x.FeedbackSubmittedAt,
					x.CreatedOn,
					x.CancellationReason,
				})
			.OrderByDescending(x => x.CreatedOn)
			.ToListAsync(cancellationToken);

		return raw.Select(x => new EngagementSummary(
			x.Id.Value,
			x.OpportunityId.Value,
			x.OpportunityTitle,
			x.OrganizationId.Value,
			x.OrganizationName,
			x.VolunteerId?.Value,
			x.TimeSlotId?.Value,
			x.Message,
			x.Status.ToString(),
			x.IsCheckedIn,
			x.FeedbackSubmittedAt.HasValue,
			x.CreatedOn,
			CancellationReason: x.CancellationReason,
			CheckInMethod: x.CheckInMethod.ToString(),
			OpportunityTitleEn: x.OpportunityTitleEn)).ToList();
	}

	public async ValueTask<PagedList<EngagementSummary>> GetPagedByOpportunityAsync(
		VolunteerOpportunityId opportunityId,
		int pageNumber,
		int pageSize,
		EngagementStatus? status = null,
		TimeSlotId? timeSlotId = null,
		IReadOnlyList<Guid>? volunteerIds = null,
		CancellationToken cancellationToken = default)
	{
		var scopedQuery = dbContext.EngagementsQuery.Where(e => e.OpportunityId == opportunityId);

		if (status is not null)
			scopedQuery = scopedQuery.Where(e => e.Status == status.Value);

		if (timeSlotId is not null)
			scopedQuery = scopedQuery.Where(e => e.TimeSlotId == timeSlotId);

		scopedQuery = ApplyVolunteerIdsFilter(scopedQuery, volunteerIds);

		return await BuildPagedResultAsync(scopedQuery, pageNumber, pageSize, cancellationToken);
	}

	public async ValueTask<PagedList<EngagementSummary>> GetPagedByOrganizationAsync(
		OrganizationId organizationId,
		int pageNumber,
		int pageSize,
		EngagementStatus? status = null,
		IReadOnlyList<Guid>? volunteerIds = null,
		CancellationToken cancellationToken = default)
	{
		var orgOpportunityIds = dbContext.VolunteerOpportunitiesQuery
			.Where(vo => vo.OrganizationId == organizationId)
			.Select(vo => vo.Id);

		var scopedQuery = dbContext.EngagementsQuery.Where(e => orgOpportunityIds.Contains(e.OpportunityId));

		if (status is not null)
			scopedQuery = scopedQuery.Where(e => e.Status == status.Value);

		scopedQuery = ApplyVolunteerIdsFilter(scopedQuery, volunteerIds);

		return await BuildPagedResultAsync(scopedQuery, pageNumber, pageSize, cancellationToken);
	}

	private static IQueryable<Engagement> ApplyVolunteerIdsFilter(
		IQueryable<Engagement> query,
		IReadOnlyList<Guid>? volunteerIds)
	{
		if (volunteerIds is null)
			return query;

		var candidateIds = volunteerIds
			.Select(id => (UserId?)UserId.Create(id).GetValueOrThrow())
			.ToList();
		return query.Where(e => candidateIds.Contains(e.VolunteerId));
	}

	private async Task<PagedList<EngagementSummary>> BuildPagedResultAsync(
		IQueryable<Engagement> scopedQuery,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken)
	{
		var totalCount = await scopedQuery.CountAsync(cancellationToken);

		var raw = await scopedQuery
			.Join(
				dbContext.VolunteerOpportunitiesQuery.Select(o => new { o.Id, o.TitleDe, o.TitleEn, o.OrganizationId, o.CheckInMethod }),
				e => e.OpportunityId,
				o => o.Id,
				(e, o) => new
				{
					e.Id,
					e.OpportunityId,
					OpportunityTitle = o.TitleDe,
					OpportunityTitleEn = o.TitleEn,
					o.OrganizationId,
					o.CheckInMethod,
					e.VolunteerId,
					e.TimeSlotId,
					e.Message,
					e.Status,
					e.IsCheckedIn,
					e.FeedbackSubmittedAt,
					e.CreatedOn,
					e.CancellationReason,
					e.Meeting,
				})
			.Join(
				dbContext.OrganizationsQuery.Select(org => new { org.Id, org.Name }),
				x => x.OrganizationId,
				org => org.Id,
				(x, org) => new
				{
					x.Id,
					x.OpportunityId,
					x.OpportunityTitle,
					x.OpportunityTitleEn,
					OrganizationId = org.Id,
					OrganizationName = org.Name,
					x.CheckInMethod,
					x.VolunteerId,
					x.TimeSlotId,
					x.Message,
					x.Status,
					x.IsCheckedIn,
					x.FeedbackSubmittedAt,
					x.CreatedOn,
					x.CancellationReason,
					x.Meeting,
				})
			.OrderByDescending(x => x.CreatedOn)
			.ThenBy(x => x.Id)
			.Skip((pageNumber - 1) * pageSize)
			.Take(pageSize)
			.ToListAsync(cancellationToken);

		var pageVolunteerIds = raw
			.Where(x => x.VolunteerId is not null)
			.Select(x => x.VolunteerId!.Value)
			.Distinct()
			.ToList();

		Dictionary<Guid, string?> phonesByVolunteerId = [];
		if (pageVolunteerIds.Count > 0)
		{
			phonesByVolunteerId = await dbContext.UsersQuery
				.Where(u => pageVolunteerIds.Contains(u.Id))
				.ToDictionaryAsync(u => u.Id.Value, u => u.Phone, cancellationToken);
		}

		var participantCounts = await CountMeetingParticipantsAsync(raw.Select(x => x.Meeting), cancellationToken);

		var items = raw.Select(x => new EngagementSummary(
			x.Id.Value,
			x.OpportunityId.Value,
			x.OpportunityTitle,
			x.OrganizationId.Value,
			x.OrganizationName,
			x.VolunteerId?.Value,
			x.TimeSlotId?.Value,
			x.Message,
			x.Status.ToString(),
			x.IsCheckedIn,
			x.FeedbackSubmittedAt.HasValue,
			x.CreatedOn,
			VolunteerPhone: x.VolunteerId is not null
				? phonesByVolunteerId.GetValueOrDefault(x.VolunteerId.Value.Value)
				: null,
			CancellationReason: x.CancellationReason,
			CheckInMethod: x.CheckInMethod.ToString(),
			OpportunityTitleEn: x.OpportunityTitleEn,
			Meeting: ToMeetingSummary(x.Meeting, participantCounts))).ToList();

		return new PagedList<EngagementSummary>(items, totalCount, pageNumber, pageSize);
	}

	public async ValueTask<PagedList<EngagementSummary>> GetByVolunteerAsync(
		UserId volunteerId,
		bool upcoming,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken = default)
	{
		var now = DateTimeOffset.UtcNow;

		var scopedQuery =
			from e in dbContext.EngagementsQuery
			where e.VolunteerId == volunteerId
			join ts in dbContext.TimeSlotsQuery on e.TimeSlotId equals ts.Id into tsGroup
			from ts in tsGroup.DefaultIfEmpty()
			join o in dbContext.VolunteerOpportunitiesQuery on e.OpportunityId equals o.Id into oGroup
			from o in oGroup.DefaultIfEmpty()
			select new
			{
				Engagement = e,
				TimeSlotStart = (DateTimeOffset?)ts.StartDateTime,
				TimeSlotEnd = (DateTimeOffset?)ts.EndDateTime,
				OpportunityExists = o != null,
				OpportunityValidUntil = (DateTimeOffset?)o.ValidUntil,
			};

		// Bucketing depends only on the engagement's own timeframe - never on
		// EngagementStatus - so a withdrawn or cancelled engagement whose
		// timeframe is still open stays "upcoming" instead of being dumped
		// into "past" purely because of its status (#2240). The timeframe is
		// the time slot's end when there is one; for a slot-less individual
		// contact engagement, check-in is the completion signal (there is no
		// later timestamp to anchor to), otherwise the opportunity's
		// application deadline; with none of those, it never ends on its own.
		// A meeting still ahead keeps an expression of interest upcoming past its
		// deadline: arranging that meeting is exactly what happens after it (#2413).
		scopedQuery = upcoming
			? scopedQuery.Where(x =>
				x.OpportunityExists
				&& ((x.TimeSlotEnd != null && x.TimeSlotEnd >= now)
					|| (x.TimeSlotEnd == null
						&& !x.Engagement.IsCheckedIn
						&& (x.OpportunityValidUntil == null
							|| x.OpportunityValidUntil >= now
							|| (x.Engagement.Meeting != null && x.Engagement.Meeting.EndsAt >= now)))))
			: scopedQuery.Where(x =>
				!x.OpportunityExists
				|| (x.TimeSlotEnd != null && x.TimeSlotEnd < now)
				|| (x.TimeSlotEnd == null
					&& (x.Engagement.IsCheckedIn
						|| (x.OpportunityValidUntil != null
							&& x.OpportunityValidUntil < now
							&& (x.Engagement.Meeting == null || x.Engagement.Meeting.EndsAt < now)))));

		var totalCount = await scopedQuery.CountAsync(cancellationToken);

		var orderedQuery = upcoming
			? scopedQuery.OrderBy(x => x.TimeSlotStart ?? DateTimeOffset.MaxValue).ThenBy(x => x.Engagement.Id)
			: scopedQuery.OrderByDescending(x => x.Engagement.CreatedOn).ThenBy(x => x.Engagement.Id);

		var engagements = await orderedQuery
			.Select(x => x.Engagement)
			.Skip((pageNumber - 1) * pageSize)
			.Take(pageSize)
			.ToListAsync(cancellationToken);

		var items = await MapToSummariesAsync(engagements, cancellationToken);

		return new PagedList<EngagementSummary>(items, totalCount, pageNumber, pageSize);
	}

	public async ValueTask<List<EngagementSummary>> GetAllByVolunteerAsync(
		UserId volunteerId,
		CancellationToken cancellationToken = default)
	{
		// Same no-inner-join rationale as GetByVolunteerAsync above (#667) - a
		// deleted opportunity must not drop the volunteer's own engagement from
		// their data export.
		var engagements = await dbContext.EngagementsQuery
			.Where(e => e.VolunteerId == volunteerId)
			.OrderByDescending(e => e.CreatedOn)
			.ToListAsync(cancellationToken);

		return await MapToSummariesAsync(engagements, cancellationToken);
	}

	public async ValueTask<List<EngagementSummary>> GetCheckedInByVolunteerAsync(
		UserId volunteerId,
		CancellationToken cancellationToken = default)
	{
		// Same no-inner-join rationale as GetByVolunteerAsync above (#667) - a
		// deleted opportunity must not drop the volunteer's own engagement from
		// their engagement record.
		var engagements = await dbContext.EngagementsQuery
			.Where(e => e.VolunteerId == volunteerId && e.IsCheckedIn)
			.OrderByDescending(e => e.CreatedOn)
			.ToListAsync(cancellationToken);

		return await MapToSummariesAsync(engagements, cancellationToken);
	}

	private async Task<List<EngagementSummary>> MapToSummariesAsync(
		List<Engagement> engagements,
		CancellationToken cancellationToken)
	{
		var opportunityIds = engagements.Select(e => e.OpportunityId).Distinct().ToList();
		var opportunities = await dbContext.VolunteerOpportunitiesQuery
			.Where(o => opportunityIds.Contains(o.Id))
			.Select(o => new { o.Id, o.TitleDe, o.TitleEn, o.IsRemote, o.Address, o.OrganizationId, o.CheckInMethod, o.ValidUntil })
			.ToDictionaryAsync(o => o.Id, cancellationToken);

		var organizationIds = opportunities.Values.Select(o => o.OrganizationId).Distinct().ToList();
		var organizations = await dbContext.OrganizationsQuery
			.Where(org => organizationIds.Contains(org.Id))
			.Select(org => new { org.Id, org.Name })
			.ToDictionaryAsync(org => org.Id, cancellationToken);

		var timeSlotIds = engagements
			.Where(e => e.TimeSlotId is not null)
			.Select(e => e.TimeSlotId!.Value)
			.Distinct()
			.ToList();

		Dictionary<TimeSlotId, TimeSlot> timeSlots = [];
		if (timeSlotIds.Count > 0)
		{
			timeSlots = await dbContext.TimeSlotsQuery
				.Where(ts => timeSlotIds.Contains(ts.Id))
				.ToDictionaryAsync(ts => ts.Id, cancellationToken);
		}

		var participantCounts = await CountMeetingParticipantsAsync(engagements.Select(e => e.Meeting), cancellationToken);

		return engagements.Select(e =>
		{
			opportunities.TryGetValue(e.OpportunityId, out var opportunity);
			var organization = opportunity is not null
				&& organizations.TryGetValue(opportunity.OrganizationId, out var org)
					? org
					: null;

			TimeSlot? timeSlot = e.TimeSlotId is not null
				&& timeSlots.TryGetValue(e.TimeSlotId.Value, out var slot)
					? slot
					: null;

			var location = opportunity is null
				? null
				: opportunity.IsRemote ? "Remote" : FormatAddress(opportunity.Address);

			return new EngagementSummary(
				e.Id.Value,
				e.OpportunityId.Value,
				opportunity?.TitleDe,
				organization?.Id.Value,
				organization?.Name,
				e.VolunteerId?.Value,
				e.TimeSlotId?.Value,
				e.Message,
				e.Status.ToString(),
				e.IsCheckedIn,
				e.FeedbackSubmittedAt.HasValue,
				e.CreatedOn,

				TimeSlotStartDateTime: timeSlot?.StartDateTime ?? e.TimeSlotStartDateTime,
				TimeSlotEndDateTime: timeSlot?.EndDateTime ?? e.TimeSlotEndDateTime,
				Location: location,
				CancellationReason: e.CancellationReason,
				FeedbackRating: e.FeedbackRating,
				FeedbackComment: e.FeedbackComment,
				FeedbackSubmittedAt: e.FeedbackSubmittedAt,
				CheckInMethod: (opportunity?.CheckInMethod ?? CheckInMethod.None).ToString(),
				OpportunityValidUntil: opportunity?.ValidUntil,
				RemainingReactivations: Engagement.MaxReactivationCount - e.ReactivationCount,
				OpportunityTitleEn: opportunity?.TitleEn,
				Meeting: ToMeetingSummary(e.Meeting, participantCounts));
		}).ToList();
	}

	private async Task<Dictionary<MeetingId, int>> CountMeetingParticipantsAsync(
		IEnumerable<Meeting?> meetings,
		CancellationToken cancellationToken)
	{
		var meetingIds = meetings
			.Where(m => m is not null)
			.Select(m => m!.Id)
			.Distinct()
			.ToList();

		if (meetingIds.Count == 0)
			return [];

		var counts = await dbContext.EngagementsQuery
			.Where(e => e.Meeting != null && meetingIds.Contains(e.Meeting.Id))
			.GroupBy(e => e.Meeting!.Id)
			.Select(g => new { MeetingId = g.Key, Count = g.Count() })
			.ToListAsync(cancellationToken);

		return counts.ToDictionary(x => x.MeetingId, x => x.Count);
	}

	private static EngagementMeetingSummary? ToMeetingSummary(
		Meeting? meeting,
		Dictionary<MeetingId, int> participantCounts) =>
		meeting is null
			? null
			: new EngagementMeetingSummary(
				meeting.Id.Value,
				meeting.StartsAt,
				meeting.EndsAt,
				meeting.Location,
				meeting.Note,
				meeting.ProposedBy.ToString(),
				meeting.Response.ToString(),
				participantCounts.GetValueOrDefault(meeting.Id, 1));

	private static string? FormatAddress(Address? address)
	{
		if (address is null)
			return null;

		var parts = new List<string>();
		var streetLine = $"{address.Street} {address.HouseNumber}".Trim();
		if (!string.IsNullOrWhiteSpace(streetLine))
			parts.Add(streetLine);
		var cityLine = $"{address.ZipCode} {address.City}".Trim();
		if (!string.IsNullOrWhiteSpace(cityLine))
			parts.Add(cityLine);
		return string.Join(", ", parts);
	}

	public async ValueTask<List<Guid>> GetActiveVolunteerIdsByOpportunityAsync(
		VolunteerOpportunityId opportunityId,
		TimeSlotId? timeSlotId,
		CancellationToken cancellationToken = default)
	{
		var query = dbContext.EngagementsQuery
			.Where(e => e.OpportunityId == opportunityId
				&& e.VolunteerId != null
				&& (e.Status == EngagementStatus.Pending || e.Status == EngagementStatus.Confirmed));

		if (timeSlotId is not null)
			query = query.Where(e => e.TimeSlotId == timeSlotId);

		var volunteerIds = await query
			.Select(e => e.VolunteerId)
			.Distinct()
			.ToListAsync(cancellationToken);

		return volunteerIds.Select(id => id!.Value.Value).ToList();
	}

	public async ValueTask<OpportunityFeedbackSummary> GetFeedbackByOpportunityAsync(
		VolunteerOpportunityId opportunityId,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken = default)
	{
		var scopedQuery = dbContext.EngagementsQuery
			.Where(e => e.OpportunityId == opportunityId && e.FeedbackSubmittedAt != null);

		var totalCount = await scopedQuery.CountAsync(cancellationToken);

		var avg = totalCount > 0
			? (double?)await scopedQuery.AverageAsync(e => e.FeedbackRating!.Value, cancellationToken)
			: null;

		var items = await scopedQuery
			.OrderByDescending(e => e.FeedbackSubmittedAt)
			.ThenBy(e => e.Id)
			.Skip((pageNumber - 1) * pageSize)
			.Take(pageSize)
			.Select(e => new FeedbackItemDto(
				e.FeedbackRating!.Value,
				e.FeedbackComment,
				e.FeedbackSubmittedAt!.Value))
			.ToListAsync(cancellationToken);

		return new OpportunityFeedbackSummary(avg, totalCount, new PagedList<FeedbackItemDto>(items, totalCount, pageNumber, pageSize));
	}
}
