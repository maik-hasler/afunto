using Application.Common.Exceptions;
using Application.Common.Pagination;
using Application.Common.Sitemap;
using Application.Common.Time;
using Application.Organizations.GetOrganizationCalendarEvents.v1;
using Application.VolunteerOpportunities;
using Application.VolunteerOpportunities.GetVolunteerOpportunities.v1;
using Application.VolunteerOpportunities.GetVolunteerOpportunityDateAvailability.v1;
using Application.VolunteerOpportunities.GetVolunteerOpportunityDetails.v1;
using Domain.Engagements;
using Domain.Organizations;
using Domain.Users;
using Domain.VolunteerOpportunities;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Infrastructure.Persistence.Repositories;

internal sealed class VolunteerOpportunityReadRepository(
	ApplicationDbContext dbContext)
	: IVolunteerOpportunityReadRepository
{
	// The date-availability window is already capped at 62 days (see
	// GetVolunteerOpportunityDateAvailabilityEndpoint.MaxWindowDays), but the number of
	// publicly-listed opportunities with slots inside that window has no ceiling - it grows
	// with the platform's total published data. This bounds the raw row count regardless,
	// ordered soonest-first so a cap that ever kicks in drops the least actionable slots.
	private const int MaxDateAvailabilitySlotRows = 10_000;

	public async ValueTask<IReadOnlyList<SitemapEntry>> GetPublishedForSitemapAsync(
		CancellationToken cancellationToken = default)
	{
		var now = DateTimeOffset.UtcNow;

		return await dbContext.VolunteerOpportunitiesQuery
			.Where(vo => vo.Status == OpportunityStatus.Published)
			.Where(vo => vo.TimeSlots.Any(ts => ts.EndDateTime >= now) || (!vo.TimeSlots.Any() && vo.ValidUntil != null && vo.ValidUntil >= now))
			.Select(vo => new SitemapEntry(vo.Id.Value, vo.ModifiedOn ?? vo.CreatedOn))
			.ToListAsync(cancellationToken);
	}

	public async ValueTask<PagedList<VolunteerOpportunitySummary>> GetPagedSummariesAsync(
		VolunteerOpportunityFilter filter,
		CancellationToken cancellationToken = default)
	{
		var now = DateTimeOffset.UtcNow;

		var query = ApplyPubliclyListedFilters(
			dbContext.VolunteerOpportunitiesQuery,
			now,
			filter.Occurrence,
			filter.ParticipationType,
			filter.IsRemote,
			filter.Categories,
			filter.Tag,
			filter.Keyword,
			ResolveBoundingBox(filter.CenterLatitude, filter.CenterLongitude, filter.RadiusKm));

		// DateFrom/DateTo describe one window, so a slot has to satisfy both bounds at once.
		// Testing them as two separate Any() calls let a series with slots on 03/10/17 match a
		// 05-06 window (one slot clears the lower bound, a different one clears the upper) even
		// though no single slot falls inside it (#2319).
		//
		// Opportunities without time slots (IndividualContact - see VolunteerOpportunity.AddTimeSlot)
		// have no slot dates to compare against, but they are not dateless: ApplyPubliclyListedFilters
		// only lists them while ValidUntil is still in the future, so they are on offer over
		// [now, ValidUntil]. Keeping them for any window at all (the "slot-less is never filtered
		// out" convention from #1059) let them match impossible ones - a window in 2020, or one
		// years past their ValidUntil - so intersect against that window instead.
		if (filter.DateFrom is not null || filter.DateTo is not null)
		{
			var dateFrom = filter.DateFrom;
			var dateTo = filter.DateTo;

			// [now, ValidUntil] intersects [DateFrom, DateTo] when DateTo is not already
			// behind us and ValidUntil is not before DateFrom. The first half needs no
			// column, so settle it here rather than making the database compare two
			// parameters on every row.
			var windowReachesToday = dateTo is null || dateTo >= now;

			query = query.Where(vo =>
				vo.TimeSlots.Any(ts =>
					(dateFrom == null || ts.StartDateTime >= dateFrom) &&
					(dateTo == null || ts.StartDateTime <= dateTo)) ||
				(!vo.TimeSlots.Any() &&
					windowReachesToday &&
					(dateFrom == null || (vo.ValidUntil != null && vo.ValidUntil >= dateFrom))));
		}

		var baseQuery = query
			.OrderByDescending(vo => vo.CreatedOn)
			.ThenBy(vo => vo.Id)
			.Select(vo => new
			{
				vo.Id,
				vo.TitleDe,
				vo.TitleEn,
				vo.DescriptionDe,
				vo.DescriptionEn,
				OrganizationId = vo.OrganizationId.Value,
				Street = vo.Address != null ? vo.Address.Street : null,
				HouseNumber = vo.Address != null ? vo.Address.HouseNumber : null,
				ZipCode = vo.Address != null ? vo.Address.ZipCode : null,
				City = vo.Address != null ? vo.Address.City : null,
				Latitude = vo.Address != null ? vo.Address.Latitude : null,
				Longitude = vo.Address != null ? vo.Address.Longitude : null,
				vo.IsRemote,
				vo.Occurrence,
				vo.ParticipationType,
				vo.CheckInMethod,
				vo.Category,
				vo.Tags,
				vo.CreatedOn,
				vo.ValidUntil,
				NextTimeSlotStart = vo.TimeSlots
					.Where(ts => ts.EndDateTime >= now)
					.OrderBy(ts => ts.StartDateTime)
					.Select(ts => (DateTimeOffset?)ts.StartDateTime)
					.FirstOrDefault(),
				NextTimeSlotEnd = vo.TimeSlots
					.Where(ts => ts.EndDateTime >= now)
					.OrderBy(ts => ts.StartDateTime)
					.Select(ts => (DateTimeOffset?)ts.EndDateTime)
					.FirstOrDefault(),
				vo.Status,
				vo.BannerImageUrl,
			});

		if (filter.HasRadius)
		{
			var centerLat = filter.CenterLatitude!.Value;
			var centerLon = filter.CenterLongitude!.Value;
			var radiusKm = filter.RadiusKm!.Value;

			var withDistance = baseQuery
				.Where(s => s.Latitude.HasValue && s.Longitude.HasValue)
				.Select(s => new
				{
					s.Id,
					s.TitleDe,
					s.TitleEn,
					s.DescriptionDe,
					s.DescriptionEn,
					s.OrganizationId,
					s.Street,
					s.HouseNumber,
					s.ZipCode,
					s.City,
					s.Latitude,
					s.Longitude,
					s.IsRemote,
					s.Occurrence,
					s.ParticipationType,
					s.CheckInMethod,
					s.Category,
					s.Tags,
					s.CreatedOn,
					s.ValidUntil,
					s.NextTimeSlotStart,
					s.NextTimeSlotEnd,
					s.Status,
					s.BannerImageUrl,
					DistanceKm = 6371.0 * 2.0 * Math.Atan2(
						Math.Sqrt(Math.Pow(Math.Sin((s.Latitude!.Value - centerLat) * Math.PI / 180.0 / 2.0), 2) +
							Math.Cos(centerLat * Math.PI / 180.0) * Math.Cos(s.Latitude.Value * Math.PI / 180.0) *
							Math.Pow(Math.Sin((s.Longitude!.Value - centerLon) * Math.PI / 180.0 / 2.0), 2)),
						Math.Sqrt(1.0 - (Math.Pow(Math.Sin((s.Latitude.Value - centerLat) * Math.PI / 180.0 / 2.0), 2) +
							Math.Cos(centerLat * Math.PI / 180.0) * Math.Cos(s.Latitude.Value * Math.PI / 180.0) *
							Math.Pow(Math.Sin((s.Longitude.Value - centerLon) * Math.PI / 180.0 / 2.0), 2)))),
				})
				.Where(s => s.DistanceKm <= radiusKm);

			var matchedCount = await withDistance.CountAsync(cancellationToken);

			var page = await withDistance
				.OrderBy(s => s.DistanceKm)
				.ThenBy(s => s.Id)
				.Skip((filter.PageNumber - 1) * filter.PageSize)
				.Take(filter.PageSize)
				.ToListAsync(cancellationToken);

			var pageGuids = page.Select(x => x.Id.Value).ToList();
			var (maxPMap, partCountMap) = await LoadParticipantStatsAsync(pageGuids, includeEndedSlots: false, cancellationToken);
			var orgMap = await LoadOrganizationSummariesAsync(page.Select(x => x.OrganizationId), cancellationToken);

			var summaries = page
				.Where(x => orgMap.ContainsKey(x.OrganizationId))
				.Select(x =>
				{
					var (orgName, orgLogoUrl) = orgMap[x.OrganizationId];
					return ToSummary(x.Id.Value, x.TitleDe, x.TitleEn, x.DescriptionDe, x.DescriptionEn, x.OrganizationId, orgName, orgLogoUrl,
						x.Street, x.HouseNumber, x.ZipCode, x.City, x.Latitude, x.Longitude, x.IsRemote, x.Occurrence,
						x.ParticipationType, x.CheckInMethod, x.Category, x.Tags, x.CreatedOn, x.ValidUntil, x.NextTimeSlotStart, x.NextTimeSlotEnd,
						x.Status, x.BannerImageUrl,
						maxPMap.GetValueOrDefault(x.Id.Value, 0), partCountMap.GetValueOrDefault(x.Id.Value, 0));
				})
				.ToList();

			return new PagedList<VolunteerOpportunitySummary>(summaries, matchedCount, filter.PageNumber, filter.PageSize);
		}

		var total = await query.CountAsync(cancellationToken);
		var rows = await baseQuery
			.Skip((filter.PageNumber - 1) * filter.PageSize)
			.Take(filter.PageSize)
			.ToListAsync(cancellationToken);

		if (rows.Count == 0)
			return new PagedList<VolunteerOpportunitySummary>([], total, filter.PageNumber, filter.PageSize);

		var guids = rows.Select(x => x.Id.Value).ToList();
		var (maxParticipantsMap, participantCountMap) = await LoadParticipantStatsAsync(guids, includeEndedSlots: false, cancellationToken);
		var organizationSummaries = await LoadOrganizationSummariesAsync(rows.Select(x => x.OrganizationId), cancellationToken);

		var result = rows
			.Where(x => organizationSummaries.ContainsKey(x.OrganizationId))
			.Select(x =>
			{
				var (orgName, orgLogoUrl) = organizationSummaries[x.OrganizationId];
				return ToSummary(x.Id.Value, x.TitleDe, x.TitleEn, x.DescriptionDe, x.DescriptionEn, x.OrganizationId, orgName, orgLogoUrl,
					x.Street, x.HouseNumber, x.ZipCode, x.City, x.Latitude, x.Longitude, x.IsRemote, x.Occurrence,
					x.ParticipationType, x.CheckInMethod, x.Category, x.Tags, x.CreatedOn, x.ValidUntil, x.NextTimeSlotStart, x.NextTimeSlotEnd,
					x.Status, x.BannerImageUrl,
					maxParticipantsMap.GetValueOrDefault(x.Id.Value, 0), participantCountMap.GetValueOrDefault(x.Id.Value, 0));
			})
			.ToList();

		return new PagedList<VolunteerOpportunitySummary>(result, total, filter.PageNumber, filter.PageSize);
	}

	public async ValueTask<IReadOnlyList<VolunteerOpportunityAvailableDate>> GetDateAvailabilityAsync(
		VolunteerOpportunityDateAvailabilityFilter filter,
		CancellationToken cancellationToken = default)
	{
		var now = DateTimeOffset.UtcNow;

		var query = ApplyPubliclyListedFilters(
			dbContext.VolunteerOpportunitiesQuery,
			now,
			filter.Occurrence,
			filter.ParticipationType,
			filter.IsRemote,
			filter.Categories,
			filter.Tag,
			filter.Keyword,
			ResolveBoundingBox(filter.CenterLatitude, filter.CenterLongitude, filter.RadiusKm));

		var slots = await query
			.SelectMany(vo => vo.TimeSlots
				.Where(ts => ts.StartDateTime >= filter.From && ts.StartDateTime <= filter.To)
				.Select(ts => new
				{
					OpportunityId = vo.Id.Value,
					ts.StartDateTime,
					Latitude = vo.Address != null ? vo.Address.Latitude : null,
					Longitude = vo.Address != null ? vo.Address.Longitude : null,
				}))
			.OrderBy(s => s.StartDateTime)
			.Take(MaxDateAvailabilitySlotRows)
			.ToListAsync(cancellationToken);

		var withinRadius = filter.HasRadius
			? slots.Where(s => s.Latitude.HasValue && s.Longitude.HasValue &&
				GeoMath.DistanceKm(
					filter.CenterLatitude!.Value,
					filter.CenterLongitude!.Value,
					s.Latitude.Value,
					s.Longitude.Value) <= filter.RadiusKm!.Value)
			: slots;

		var timeZone = CanonicalTimeZone.Resolve(filter.Timezone);

		return withinRadius
			.GroupBy(s => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.StartDateTime, timeZone).DateTime))
			.OrderBy(g => g.Key)
			.Select(g => new VolunteerOpportunityAvailableDate(
				g.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				g.Select(s => s.OpportunityId).Distinct().Count()))
			.ToList();
	}

	private async Task<Dictionary<Guid, (string Name, string? LogoUrl)>> LoadOrganizationSummariesAsync(
		IEnumerable<Guid> organizationGuids,
		CancellationToken cancellationToken)
	{
		var organizationIds = organizationGuids
			.Distinct()
			.Select(g => OrganizationId.Create(g).GetValueOrThrow())
			.ToList();

		if (organizationIds.Count == 0)
			return [];

		return await dbContext.OrganizationsQuery
			.Where(o => organizationIds.Contains(o.Id))
			.Select(o => new { o.Id, o.Name, o.LogoUrl })
			.ToDictionaryAsync(x => x.Id.Value, x => (x.Name, x.LogoUrl), cancellationToken);
	}

	private IQueryable<VolunteerOpportunity> ApplyPubliclyListedFilters(
		IQueryable<VolunteerOpportunity> source,
		DateTimeOffset now,
		string? occurrence,
		string? participationType,
		bool? isRemote,
		string[]? categories,
		string? tag,
		string? keyword,
		GeoBoundingBox? boundingBox)
	{
		var query = source
			.Where(vo => vo.Status == OpportunityStatus.Published)

			.Where(vo => vo.TimeSlots.Any(ts => ts.EndDateTime >= now) || (!vo.TimeSlots.Any() && vo.ValidUntil != null && vo.ValidUntil >= now));

		if (!string.IsNullOrWhiteSpace(occurrence) && Enum.TryParse<Occurrence>(occurrence, ignoreCase: true, out var occ))
			query = query.Where(vo => vo.Occurrence == occ);

		if (!string.IsNullOrWhiteSpace(participationType) && Enum.TryParse<ParticipationType>(participationType, ignoreCase: true, out var pt))
			query = query.Where(vo => vo.ParticipationType == pt);

		if (isRemote is bool isRemoteValue)
			query = query.Where(vo => vo.IsRemote == isRemoteValue);

		if (categories is { Length: > 0 })
		{
			var parsedCategories = categories
				.Select(c => Enum.TryParse<Domain.VolunteerOpportunities.Category>(c, ignoreCase: true, out var cat)
					? (Domain.VolunteerOpportunities.Category?)cat
					: null)
				.Where(c => c.HasValue)
				.Select(c => c!.Value)
				.ToList();

			if (parsedCategories.Count > 0)
				query = query.Where(vo => vo.Category.HasValue && parsedCategories.Contains(vo.Category.Value));
		}

		if (!string.IsNullOrWhiteSpace(tag))
			query = query.Where(vo => vo.Tags.Contains(tag));

		if (!string.IsNullOrWhiteSpace(keyword))
		{
			// Folds German umlauts/eszett the way people actually type them on a
			// phone keyboard ("Muenchen", "Fussball") so a search matches the native
			// spelling and vice versa (#2221). The fold is repeated inline on every
			// column instead of factored into a shared method: EF Core translates
			// ToLower/Replace/Contains calls to SQL individually, but can't look
			// inside a call to a method of our own to do the same.
			var normalizedKeyword = FoldGermanText(keyword);
			query = query.Where(vo =>
				vo.TitleDe.ToLower().Replace("ß", "ss").Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Contains(normalizedKeyword) ||
				(vo.TitleEn != null && vo.TitleEn.ToLower().Replace("ß", "ss").Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Contains(normalizedKeyword)) ||
				vo.DescriptionDe.ToLower().Replace("ß", "ss").Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Contains(normalizedKeyword) ||
				(vo.DescriptionEn != null && vo.DescriptionEn.ToLower().Replace("ß", "ss").Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Contains(normalizedKeyword)) ||
				dbContext.OrganizationsQuery.Any(o => o.Id == vo.OrganizationId &&
					o.Name.ToLower().Replace("ß", "ss").Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Contains(normalizedKeyword)));
		}

		if (boundingBox is GeoBoundingBox box)
			query = query.Where(vo =>
				vo.Address != null &&
				vo.Address.Latitude != null && vo.Address.Longitude != null &&
				vo.Address.Latitude >= box.South && vo.Address.Latitude <= box.North &&
				vo.Address.Longitude >= box.West && vo.Address.Longitude <= box.East);

		return query;
	}

	// Kept in sync by hand with the inline column fold in ApplyPubliclyListedFilters's
	// keyword predicate above - EF Core can translate the same ToLower/Replace chain
	// applied to a column, but not a call to this method itself, so it can't be shared.
	private static string FoldGermanText(string value) =>
		value.ToLower().Replace("ß", "ss").Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue");

	private static GeoBoundingBox? ResolveBoundingBox(double? centerLatitude, double? centerLongitude, double? radiusKm) =>
		centerLatitude.HasValue && centerLongitude.HasValue && radiusKm is > 0
			? GeoMath.BoundingBoxFor(centerLatitude.Value, centerLongitude.Value, radiusKm.Value)
			: null;

	/// <param name="includeEndedSlots">
	/// <c>true</c> for the organizer-facing lists, where the tally is a record of everything
	/// that was ever signed up for. <c>false</c> for anything a volunteer reads as
	/// "spots left": seats in a slot that has already ended can never be booked, so counting
	/// them advertised capacity that does not exist (afunto#2318).
	/// </param>
	private async Task<(Dictionary<Guid, int?> MaxParticipants, Dictionary<Guid, int> ParticipantCounts)>
		LoadParticipantStatsAsync(
			List<Guid> opportunityGuids,
			bool includeEndedSlots,
			CancellationToken cancellationToken)
	{
		var opportunityIds = opportunityGuids
			.Select(g => VolunteerOpportunityId.Create(g).GetValueOrThrow())
			.ToList();

		// A cutoff rather than a conditional predicate, so both queries keep one shape EF
		// can always translate.
		var slotCutoff = includeEndedSlots ? DateTimeOffset.MinValue : DateTimeOffset.UtcNow;

		var maxParticipants = await dbContext.VolunteerOpportunitiesQuery
			.Where(vo => opportunityIds.Contains(vo.Id))
			.Select(vo => new
			{
				OpportunityId = vo.Id.Value,
				MaxParticipants = vo.TimeSlots.Any(ts => ts.EndDateTime >= slotCutoff && ts.MaxParticipants == null)
					? (int?)null
					: vo.TimeSlots.Where(ts => ts.EndDateTime >= slotCutoff).Sum(ts => ts.MaxParticipants) ?? 0,
			})
			.ToListAsync(cancellationToken);

		// An IndividualContact engagement has no time slot at all - it stays counted either way.
		var participantCounts = await dbContext.EngagementsQuery
			.Where(e => opportunityIds.Contains(e.OpportunityId) &&
				(e.Status == EngagementStatus.Pending || e.Status == EngagementStatus.Confirmed) &&
				(e.TimeSlotId == null || dbContext.TimeSlotsQuery
					.Any(ts => ts.Id == e.TimeSlotId && ts.EndDateTime >= slotCutoff)))
			.GroupBy(e => e.OpportunityId)
			.Select(g => new { OpportunityId = g.Key.Value, Count = g.Count() })
			.ToListAsync(cancellationToken);

		return (
			maxParticipants.ToDictionary(x => x.OpportunityId, x => x.MaxParticipants),
			participantCounts.ToDictionary(x => x.OpportunityId, x => x.Count)
		);
	}

	private static VolunteerOpportunitySummary ToSummary(
		Guid id,
		string titleDe,
		string? titleEn,
		string? descriptionDe,
		string? descriptionEn,
		Guid organizationId,
		string orgName,
		string? orgLogoUrl,
		string? street,
		string? houseNumber,
		string? zipCode,
		string? city,
		double? latitude,
		double? longitude,
		bool isRemote,
		Occurrence occurrence,
		ParticipationType participationType,
		CheckInMethod checkInMethod,
		Category? category,
		IReadOnlyList<string> tags,
		DateTimeOffset createdOn,
		DateTimeOffset? validUntil,
		DateTimeOffset? nextTimeSlotStart,
		DateTimeOffset? nextTimeSlotEnd,
		OpportunityStatus status,
		string? bannerImageUrl,
		int? totalMaxParticipants,
		int currentParticipantCount) =>
		new(
			id,
			titleDe,
			titleEn,
			descriptionDe,
			descriptionEn,
			organizationId,
			orgName,
			street,
			houseNumber,
			zipCode,
			city,
			latitude,
			longitude,
			isRemote,
			occurrence.ToString(),
			participationType.ToString(),
			checkInMethod.ToString(),
			category?.ToString(),
			tags,
			createdOn,
			validUntil,
			nextTimeSlotStart,
			nextTimeSlotEnd,
			totalMaxParticipants,
			currentParticipantCount,
			status.ToString(),
			bannerImageUrl,
			orgLogoUrl);

	public async ValueTask<VolunteerOpportunityDetails?> GetDetailsAsync(
		Guid opportunityId,
		Guid? requestingUserId = null,
		CancellationToken cancellationToken = default)
	{
		var opportunityId_ = VolunteerOpportunityId.Create(opportunityId).GetValueOrThrow();

		var result = await dbContext.VolunteerOpportunitiesQuery
			.Where(vo => vo.Id == opportunityId_)
			.Join(
				dbContext.OrganizationsQuery,
				vo => vo.OrganizationId,
				org => org.Id,
				(vo, org) => new { vo, org })
			.Select(x => new
			{
				x.vo.Id,
				x.vo.TitleDe,
				x.vo.TitleEn,
				x.vo.DescriptionDe,
				x.vo.DescriptionEn,
				x.vo.OrganizationId,
				x.org.Name,
				x.vo.Address,
				x.vo.IsRemote,
				x.vo.Occurrence,
				x.vo.ParticipationType,
				x.vo.CheckInMethod,
				x.vo.Category,
				x.vo.Tags,
				x.vo.CreatedOn,
				x.vo.ValidUntil,
				x.vo.Status,
				BannerImageUrl = x.vo.BannerImageUrl
			})
			.FirstOrDefaultAsync(cancellationToken);

		if (result is null)
			return null;

		if (result.Status != OpportunityStatus.Published)
		{
			var isOrganizer = requestingUserId is Guid requestingUserId_ &&
				await dbContext.IsOrganizerAsync(
					result.OrganizationId,
					UserId.Create(requestingUserId_).GetValueOrThrow(),
					cancellationToken);

			if (!isOrganizer)
				return null;
		}

		var timeSlots = await dbContext.VolunteerOpportunitiesQuery
			.Where(vo => vo.Id == opportunityId_)
			.SelectMany(vo => vo.TimeSlots)
			.OrderBy(ts => ts.StartDateTime)
			.Select(ts => new TimeSlotDetail(
				ts.Id.Value,
				ts.StartDateTime,
				ts.EndDateTime,
				ts.MaxParticipants,
				dbContext.EngagementsQuery.Count(e =>
					e.TimeSlotId == ts.Id &&
					(e.Status == EngagementStatus.Pending || e.Status == EngagementStatus.Confirmed)),
				ts.SeriesId,
				ts.RecurrenceFrequency,
				ts.RecurrenceCount))
			.ToListAsync(cancellationToken);

		var viewerId = requestingUserId is Guid requestingUserIdValue
			? UserId.Create(requestingUserIdValue).GetValueOrThrow()
			: (UserId?)null;

		// Deliberately viewer-relative: "how many *others* have joined". It is therefore not
		// a capacity figure - an absolute "spots left" label must be derived from the
		// viewer-independent TimeSlotDetail.BookedCount above (afunto#2318).
		var currentParticipantCount = await dbContext.EngagementsQuery
			.CountAsync(e =>
				e.OpportunityId == opportunityId_ &&
				(e.Status == EngagementStatus.Pending || e.Status == EngagementStatus.Confirmed) &&
				(viewerId == null || e.VolunteerId != viewerId),
				cancellationToken);

		IReadOnlyList<CurrentUserEngagementInfo> currentUserEngagements = [];
		if (viewerId is UserId userId_)
		{
			var engagements = await dbContext.EngagementsQuery
				.Where(e =>
					e.OpportunityId == opportunityId_ &&
					e.VolunteerId == userId_ &&
					(e.Status == EngagementStatus.Pending || e.Status == EngagementStatus.Confirmed))
				.OrderBy(e => e.CreatedOn)
				.Select(e => new { e.Id, e.Status, e.TimeSlotId, e.IsCheckedIn, e.ReactivationCount })
				.ToListAsync(cancellationToken);

			currentUserEngagements = engagements
				.Select(e => new CurrentUserEngagementInfo(
					e.Id.Value,
					e.Status.ToString(),
					e.TimeSlotId?.Value,
					e.IsCheckedIn,
					Engagement.MaxReactivationCount - e.ReactivationCount))
				.ToList();
		}

		return new VolunteerOpportunityDetails(
			result.Id.Value,
			result.TitleDe,
			result.TitleEn,
			result.DescriptionDe,
			result.DescriptionEn,
			result.OrganizationId.Value,
			result.Name,
			result.Address?.Street,
			result.Address?.HouseNumber,
			result.Address?.ZipCode,
			result.Address?.City,
			result.Address?.Latitude,
			result.Address?.Longitude,
			result.IsRemote,
			result.Occurrence.ToString(),
			result.ParticipationType.ToString(),
			result.CheckInMethod.ToString(),
			result.Category?.ToString(),
			result.Tags,
			timeSlots,
			result.CreatedOn,
			result.ValidUntil,
			currentParticipantCount,
			result.Status.ToString(),
			result.BannerImageUrl,
			currentUserEngagements);
	}

	public async ValueTask<IReadOnlyList<VolunteerOpportunitySummary>> GetSummariesByOrganizationAsync(
		Guid organizationId,
		OpportunityStatus? status = null,
		CancellationToken cancellationToken = default)
	{
		var organizationId_ = OrganizationId.Create(organizationId).GetValueOrThrow();
		var now = DateTimeOffset.UtcNow;

		var orgQuery = dbContext.VolunteerOpportunitiesQuery
			.Where(vo => vo.OrganizationId == organizationId_);

		if (status is OpportunityStatus s)
			orgQuery = orgQuery.Where(vo => vo.Status == s);

		// Published doesn't mean still open - an opportunity whose only time slots have
		// ended, or whose IndividualContact deadline has passed, must not keep surfacing
		// on the organization's public profile (afunto#2212). Matches the predicate
		// ApplyPubliclyListedFilters already applies to the browse listing.
		if (status == OpportunityStatus.Published)
			orgQuery = orgQuery.Where(vo =>
				vo.TimeSlots.Any(ts => ts.EndDateTime >= now) ||
				(!vo.TimeSlots.Any() && vo.ValidUntil != null && vo.ValidUntil >= now));

		var rows = await orgQuery
			.Join(
				dbContext.OrganizationsQuery,
				vo => vo.OrganizationId,
				org => org.Id,
				(vo, org) => new { vo, org })
			.OrderByDescending(x => x.vo.CreatedOn)
			.Select(x => new
			{
				Id = x.vo.Id.Value,
				x.vo.TitleDe,
				x.vo.TitleEn,
				x.vo.DescriptionDe,
				x.vo.DescriptionEn,
				OrganizationId = x.vo.OrganizationId.Value,
				OrgName = x.org.Name,
				OrgLogoUrl = x.org.LogoUrl,
				Street = x.vo.Address != null ? x.vo.Address.Street : null,
				HouseNumber = x.vo.Address != null ? x.vo.Address.HouseNumber : null,
				ZipCode = x.vo.Address != null ? x.vo.Address.ZipCode : null,
				City = x.vo.Address != null ? x.vo.Address.City : null,
				Latitude = x.vo.Address != null ? x.vo.Address.Latitude : null,
				Longitude = x.vo.Address != null ? x.vo.Address.Longitude : null,
				x.vo.IsRemote,
				x.vo.Occurrence,
				x.vo.ParticipationType,
				x.vo.CheckInMethod,
				x.vo.Category,
				x.vo.Tags,
				x.vo.CreatedOn,
				x.vo.ValidUntil,
				NextTimeSlotStart = x.vo.TimeSlots
					.Where(ts => ts.EndDateTime >= now)
					.OrderBy(ts => ts.StartDateTime)
					.Select(ts => (DateTimeOffset?)ts.StartDateTime)
					.FirstOrDefault(),
				NextTimeSlotEnd = x.vo.TimeSlots
					.Where(ts => ts.EndDateTime >= now)
					.OrderBy(ts => ts.StartDateTime)
					.Select(ts => (DateTimeOffset?)ts.EndDateTime)
					.FirstOrDefault(),
				x.vo.Status,
				x.vo.BannerImageUrl,
			})
			.ToListAsync(cancellationToken);

		if (rows.Count == 0)
			return [];

		var guids = rows.Select(x => x.Id).ToList();
		var (maxParticipantsMap, participantCountMap) = await LoadParticipantStatsAsync(guids, includeEndedSlots: false, cancellationToken);

		return rows
			.Select(x => ToSummary(x.Id, x.TitleDe, x.TitleEn, x.DescriptionDe, x.DescriptionEn, x.OrganizationId, x.OrgName, x.OrgLogoUrl,
				x.Street, x.HouseNumber, x.ZipCode, x.City, x.Latitude, x.Longitude, x.IsRemote, x.Occurrence,
				x.ParticipationType, x.CheckInMethod, x.Category, x.Tags, x.CreatedOn, x.ValidUntil, x.NextTimeSlotStart, x.NextTimeSlotEnd,
				x.Status, x.BannerImageUrl,
				maxParticipantsMap.GetValueOrDefault(x.Id, 0), participantCountMap.GetValueOrDefault(x.Id, 0)))
			.ToList();
	}

	public async ValueTask<PagedList<VolunteerOpportunitySummary>> GetPagedSummariesByOrganizationAsync(
		Guid organizationId,
		OpportunityStatus status,
		int pageNumber,
		int pageSize,
		CancellationToken cancellationToken = default)
	{
		var organizationId_ = OrganizationId.Create(organizationId).GetValueOrThrow();
		var now = DateTimeOffset.UtcNow;

		var orgQuery = dbContext.VolunteerOpportunitiesQuery
			.Where(vo => vo.OrganizationId == organizationId_ && vo.Status == status);

		var totalCount = await orgQuery.CountAsync(cancellationToken);

		var rows = await orgQuery
			.Join(
				dbContext.OrganizationsQuery,
				vo => vo.OrganizationId,
				org => org.Id,
				(vo, org) => new { vo, org })
			.OrderByDescending(x => x.vo.CreatedOn)
			.ThenBy(x => x.vo.Id)
			.Skip((pageNumber - 1) * pageSize)
			.Take(pageSize)
			.Select(x => new
			{
				Id = x.vo.Id.Value,
				x.vo.TitleDe,
				x.vo.TitleEn,
				x.vo.DescriptionDe,
				x.vo.DescriptionEn,
				OrganizationId = x.vo.OrganizationId.Value,
				OrgName = x.org.Name,
				OrgLogoUrl = x.org.LogoUrl,
				Street = x.vo.Address != null ? x.vo.Address.Street : null,
				HouseNumber = x.vo.Address != null ? x.vo.Address.HouseNumber : null,
				ZipCode = x.vo.Address != null ? x.vo.Address.ZipCode : null,
				City = x.vo.Address != null ? x.vo.Address.City : null,
				Latitude = x.vo.Address != null ? x.vo.Address.Latitude : null,
				Longitude = x.vo.Address != null ? x.vo.Address.Longitude : null,
				x.vo.IsRemote,
				x.vo.Occurrence,
				x.vo.ParticipationType,
				x.vo.CheckInMethod,
				x.vo.Category,
				x.vo.Tags,
				x.vo.CreatedOn,
				x.vo.ValidUntil,
				NextTimeSlotStart = x.vo.TimeSlots
					.Where(ts => ts.EndDateTime >= now)
					.OrderBy(ts => ts.StartDateTime)
					.Select(ts => (DateTimeOffset?)ts.StartDateTime)
					.FirstOrDefault(),
				NextTimeSlotEnd = x.vo.TimeSlots
					.Where(ts => ts.EndDateTime >= now)
					.OrderBy(ts => ts.StartDateTime)
					.Select(ts => (DateTimeOffset?)ts.EndDateTime)
					.FirstOrDefault(),
				x.vo.Status,
				x.vo.BannerImageUrl,
			})
			.ToListAsync(cancellationToken);

		if (rows.Count == 0)
			return new PagedList<VolunteerOpportunitySummary>([], totalCount, pageNumber, pageSize);

		var guids = rows.Select(x => x.Id).ToList();
		var (maxParticipantsMap, participantCountMap) = await LoadParticipantStatsAsync(guids, includeEndedSlots: true, cancellationToken);

		var items = rows
			.Select(x => ToSummary(x.Id, x.TitleDe, x.TitleEn, x.DescriptionDe, x.DescriptionEn, x.OrganizationId, x.OrgName, x.OrgLogoUrl,
				x.Street, x.HouseNumber, x.ZipCode, x.City, x.Latitude, x.Longitude, x.IsRemote, x.Occurrence,
				x.ParticipationType, x.CheckInMethod, x.Category, x.Tags, x.CreatedOn, x.ValidUntil, x.NextTimeSlotStart, x.NextTimeSlotEnd,
				x.Status, x.BannerImageUrl,
				maxParticipantsMap.GetValueOrDefault(x.Id, 0), participantCountMap.GetValueOrDefault(x.Id, 0)))
			.ToList();

		return new PagedList<VolunteerOpportunitySummary>(items, totalCount, pageNumber, pageSize);
	}

	public async ValueTask<IReadOnlyList<OrganizationCalendarEventDto>> GetCalendarEventsAsync(
		Guid organizationId,
		DateTimeOffset from,
		DateTimeOffset to,
		CancellationToken cancellationToken = default)
	{
		var orgId = OrganizationId.Create(organizationId).GetValueOrThrow();

		var rows = await dbContext.VolunteerOpportunitiesQuery
			.Where(vo => vo.OrganizationId == orgId)
			.Where(vo => vo.TimeSlots.Any(ts => ts.StartDateTime >= from && ts.StartDateTime <= to))
			.OrderBy(vo => vo.TimeSlots
				.Where(ts => ts.StartDateTime >= from && ts.StartDateTime <= to)
				.Min(ts => ts.StartDateTime))
			.Select(vo => new
			{
				Id = vo.Id.Value,
				vo.TitleDe,
				vo.TitleEn,
				vo.Color,
				vo.Status,
				TimeSlots = vo.TimeSlots
					.Where(ts => ts.StartDateTime >= from && ts.StartDateTime <= to)
					.OrderBy(ts => ts.StartDateTime)
					.Select(ts => new { SlotId = ts.Id.Value, ts.StartDateTime, ts.EndDateTime, ts.MaxParticipants })
					.ToList(),
			})
			.ToListAsync(cancellationToken);

		if (rows.Count == 0)
			return [];

		var slotIds = rows
			.SelectMany(r => r.TimeSlots)
			.Select(ts => (TimeSlotId?)TimeSlotId.Create(ts.SlotId).GetValueOrThrow())
			.ToList();

		var activeSlotIds = await dbContext.EngagementsQuery
			.Where(e => slotIds.Contains(e.TimeSlotId) &&
				(e.Status == EngagementStatus.Pending || e.Status == EngagementStatus.Confirmed))
			.Select(e => e.TimeSlotId)
			.ToListAsync(cancellationToken);

		var slotCounts = activeSlotIds
			.GroupBy(id => id!.Value.Value)
			.ToDictionary(g => g.Key, g => g.Count());

		return rows
			.Select(r => new OrganizationCalendarEventDto(
				r.Id,
				r.TitleDe,
				r.TitleEn,
				r.Color,
				r.Status.ToString(),
				r.TimeSlots
					.Select(ts => new CalendarTimeSlotDto(
						ts.SlotId,
						ts.StartDateTime,
						ts.EndDateTime,
						ts.MaxParticipants,
						slotCounts.GetValueOrDefault(ts.SlotId, 0)))
					.ToList()))
			.ToList();
	}
}
