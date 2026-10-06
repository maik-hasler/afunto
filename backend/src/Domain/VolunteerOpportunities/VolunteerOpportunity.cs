using Domain.Common;
using Domain.Organizations;
using Domain.Primitives;

namespace Domain.VolunteerOpportunities;

public sealed class VolunteerOpportunity
	: AggregateRoot<VolunteerOpportunityId>,
		IAuditableEntity,
		ISoftDeletableEntity
{
	public const int MaxTitleLength = 200;

	public const int MaxDescriptionLength = 5000;

	public const int MaxTagsCount = 20;

	public const int MaxTagLength = 50;

	private readonly List<TimeSlot> _timeSlots = [];

	private List<string> _tags = [];

	public OrganizationId OrganizationId { get; private set; }

	public string TitleDe { get; private set; }

	public string? TitleEn { get; private set; }

	public string DescriptionDe { get; private set; }

	public string? DescriptionEn { get; private set; }

	public bool IsRemote { get; private set; }

	public Address? Address { get; private set; }

	public bool AddressGeocodingFailed { get; private set; }

	public Occurrence Occurrence { get; private set; }

	public ParticipationType ParticipationType { get; private set; }

	public CheckInMethod CheckInMethod { get; private set; }

	public Category? Category { get; private set; }

	public IReadOnlyList<string> Tags => _tags.AsReadOnly();

	public OpportunityStatus Status { get; private set; }

	public string? CancellationReason { get; private set; }

	public string? BannerImageUrl { get; private set; }

	public string? Color { get; private set; }

	public string? CheckInPin { get; private set; }

	// Which occurrence CheckInPin currently covers, for ScheduledSlots opportunities -
	// null for IndividualContact (single ongoing arrangement, no occurrences to rotate
	// across) and for a ScheduledSlots series with no remaining slot to protect.
	// EnsureCurrentCheckInPin compares this against the occurrence `now` resolves to
	// and rotates the PIN whenever they differ, so one attended occurrence's PIN does
	// not double as a working credential for the rest of the series (afunto#2202).
	public TimeSlotId? CheckInPinTimeSlotId { get; private set; }

	public DateTimeOffset? ValidUntil { get; private set; }

	public IReadOnlyCollection<TimeSlot> TimeSlots => _timeSlots.AsReadOnly();

	public DateTimeOffset CreatedOn { get; private set; }

	public DateTimeOffset? ModifiedOn { get; private set; }

	public bool IsDeleted { get; private set; }

	public DateTimeOffset? DeletedOn { get; private set; }

#pragma warning disable CS8618
	private VolunteerOpportunity() : base(default) { }
#pragma warning restore CS8618

	private VolunteerOpportunity(
		VolunteerOpportunityId id,
		OrganizationId organizationId,
		string titleDe,
		string? titleEn,
		string descriptionDe,
		string? descriptionEn,
		bool isRemote,
		Address? address,
		Occurrence occurrence,
		ParticipationType participationType,
		CheckInMethod checkInMethod,
		Category? category,
		IReadOnlyCollection<string> tags,
		OpportunityStatus status,
		IPinGenerator pinGenerator,
		string? checkInPin,
		DateTimeOffset? validUntil,
		DateTimeOffset now)
		: base(id)
	{
		OrganizationId = organizationId;
		TitleDe = titleDe;
		TitleEn = titleEn;
		DescriptionDe = descriptionDe;
		DescriptionEn = descriptionEn;
		IsRemote = isRemote;
		Address = address;
		Occurrence = occurrence;
		ParticipationType = participationType;
		CheckInMethod = checkInMethod;
		Category = category;
		_tags = new List<string>(tags);
		Status = status;
		ValidUntil = validUntil;
		if (checkInMethod == CheckInMethod.PINCode)
			CheckInPin = checkInPin ?? pinGenerator.GeneratePin();
	}

	private static Result EnsureValidPin(string pin)
	{
		// Exactly 6, matching RandomPinGenerator's fixed-width output - a shorter
		// custom PIN would carry less entropy than the auto-generated default while
		// sharing the same ICheckInAttemptLimiter attempt budget (afunto#2202).
		if (pin.Length != 6 || !pin.All(char.IsAsciiDigit))
			return Result.Failure(Error.Validation("VolunteerOpportunity.InvalidCheckInPin", "Check-in PIN must be 6 digits."));

		if (CheckInPinPolicy.IsTrivial(pin))
			return Result.Failure(Error.Validation("VolunteerOpportunity.WeakCheckInPin", "This PIN is too easy to guess - choose a less predictable one."));

		return Result.Success();
	}

	private static Result EnsureValidTitleLength(string? title)
	{
		if (title is { Length: > MaxTitleLength })
			return Result.Failure(Error.Validation("VolunteerOpportunity.TitleTooLong", $"Title must not exceed {MaxTitleLength} characters."));

		return Result.Success();
	}

	private static Result EnsureValidDescriptionLength(string? description)
	{
		if (description is { Length: > MaxDescriptionLength })
			return Result.Failure(Error.Validation("VolunteerOpportunity.DescriptionTooLong", $"Description must not exceed {MaxDescriptionLength} characters."));

		return Result.Success();
	}

	private static Result EnsureValidTags(IReadOnlyCollection<string> tags)
	{
		if (tags.Count > MaxTagsCount)
			return Result.Failure(Error.Validation("VolunteerOpportunity.TooManyTags", $"An opportunity cannot have more than {MaxTagsCount} tags."));

		if (tags.Any(tag => tag.Length > MaxTagLength))
			return Result.Failure(Error.Validation("VolunteerOpportunity.TagTooLong", $"Each tag must not exceed {MaxTagLength} characters."));

		return Result.Success();
	}

	private static Result EnsureValidValidUntil(ParticipationType participationType, DateTimeOffset? validUntil, DateTimeOffset now)
	{
		if (validUntil is null)
			return Result.Success();

		if (participationType != ParticipationType.IndividualContact)
			return Result.Failure(Error.Validation(
				"VolunteerOpportunity.ValidUntilNotAllowed",
				"A deadline can only be set for Individual contact opportunities."));

		if (validUntil <= now)
			return Result.Failure(Error.Validation("VolunteerOpportunity.ValidUntilMustBeFuture", "Deadline must be in the future."));

		return Result.Success();
	}

	private static Result EnsureIndividualContactHasValidUntil(ParticipationType participationType, DateTimeOffset? validUntil)
	{
		if (participationType == ParticipationType.IndividualContact && validUntil is null)
			return Result.Failure(Error.Validation(
				"VolunteerOpportunity.IndividualContactRequiresValidUntil",
				"An Individual contact opportunity must have a deadline before it can be published."));

		return Result.Success();
	}

	public static Result<VolunteerOpportunity> Create(
		OrganizationId organizationId,
		string titleDe,
		string? titleEn,
		string descriptionDe,
		string? descriptionEn,
		bool isRemote,
		Address? address,
		Occurrence occurrence,
		ParticipationType participationType,
		CheckInMethod checkInMethod,
		IPinGenerator pinGenerator,
		Category? category = null,
		IReadOnlyCollection<string>? tags = null,
		OpportunityStatus status = OpportunityStatus.Published,
		string? checkInPin = null,
		DateTimeOffset? validUntil = null,
		DateTimeOffset? now = null)
	{
		var validTitleDeLength = EnsureValidTitleLength(titleDe);
		if (validTitleDeLength.IsFailure)
			return Result.Failure<VolunteerOpportunity>(validTitleDeLength.Error);

		var validTitleEnLength = EnsureValidTitleLength(titleEn);
		if (validTitleEnLength.IsFailure)
			return Result.Failure<VolunteerOpportunity>(validTitleEnLength.Error);

		var validDescriptionDeLength = EnsureValidDescriptionLength(descriptionDe);
		if (validDescriptionDeLength.IsFailure)
			return Result.Failure<VolunteerOpportunity>(validDescriptionDeLength.Error);

		var validDescriptionEnLength = EnsureValidDescriptionLength(descriptionEn);
		if (validDescriptionEnLength.IsFailure)
			return Result.Failure<VolunteerOpportunity>(validDescriptionEnLength.Error);

		var validTags = EnsureValidTags(tags ?? []);
		if (validTags.IsFailure)
			return Result.Failure<VolunteerOpportunity>(validTags.Error);

		if (checkInMethod == CheckInMethod.PINCode && checkInPin is not null)
		{
			var validPin = EnsureValidPin(checkInPin);
			if (validPin.IsFailure)
				return Result.Failure<VolunteerOpportunity>(validPin.Error);
		}

		var validValidUntil = EnsureValidValidUntil(participationType, validUntil, now ?? DateTimeOffset.UtcNow);
		if (validValidUntil.IsFailure)
			return Result.Failure<VolunteerOpportunity>(validValidUntil.Error);

		if (status == OpportunityStatus.Published)
		{
			var publishable = EnsurePublishable(titleDe, descriptionDe, isRemote, address);
			if (publishable.IsFailure)
				return Result.Failure<VolunteerOpportunity>(publishable.Error);

			if (participationType == ParticipationType.ScheduledSlots)
				return Result.Failure<VolunteerOpportunity>(Error.Validation(
					"VolunteerOpportunity.ScheduledSlotsMustStartAsDraft",
					"A Scheduled slots opportunity must be created as a draft and published after adding at least one time slot."));

			var hasValidUntil = EnsureIndividualContactHasValidUntil(participationType, validUntil);
			if (hasValidUntil.IsFailure)
				return Result.Failure<VolunteerOpportunity>(hasValidUntil.Error);
		}

		var opportunity = new VolunteerOpportunity(
			VolunteerOpportunityId.New(),
			organizationId,
			titleDe,
			titleEn,
			descriptionDe,
			descriptionEn,
			isRemote,
			address,
			occurrence,
			participationType,
			checkInMethod,
			category,
			tags ?? [],
			status,
			pinGenerator,
			checkInPin,
			validUntil,
			now ?? DateTimeOffset.UtcNow);

		if (!isRemote && address is not null && address.Latitude is null)
			opportunity.AddEvent(new VolunteerOpportunityGeocodingRequestedDomainEvent(opportunity.Id));

		return opportunity;
	}

	private static Result EnsurePublishable(
		string titleDe,
		string descriptionDe,
		bool isRemote,
		Address? address)
	{
		if (string.IsNullOrWhiteSpace(titleDe))
			return Result.Failure(Error.Validation("VolunteerOpportunity.TitleRequired", "Title must not be empty."));

		if (string.IsNullOrWhiteSpace(descriptionDe))
			return Result.Failure(Error.Validation("VolunteerOpportunity.DescriptionRequired", "Description must not be empty."));

		if (!isRemote && address is null)
			return Result.Failure(Error.Validation("VolunteerOpportunity.AddressRequired", "Address is required for non-remote opportunities."));

		return Result.Success();
	}

	public Result Publish(DateTimeOffset? now = null)
	{
		if (Status == OpportunityStatus.Published)
			return Result.Failure(Error.Conflict("VolunteerOpportunity.AlreadyPublished", "Opportunity is already published."));

		// Cancelled is terminal - unlike Unpublished, there is no way back to
		// Published. Without this guard, calling Publish() on a Cancelled
		// opportunity would silently resurrect it (Publish only otherwise checks
		// for "already Published", not the source state).
		if (Status == OpportunityStatus.Cancelled)
			return Result.Failure(Error.Conflict("VolunteerOpportunity.CannotPublishCancelled", "A cancelled opportunity cannot be published again."));

		var publishable = EnsurePublishable(TitleDe, DescriptionDe, IsRemote, Address);
		if (publishable.IsFailure)
			return publishable;

		if (ParticipationType == ParticipationType.ScheduledSlots && _timeSlots.Count == 0)
			return Result.Failure(Error.Validation(
				"VolunteerOpportunity.ScheduledSlotsRequiresTimeSlot",
				"A Scheduled slots opportunity must have at least one time slot before it can be published."));

		var hasValidUntil = EnsureIndividualContactHasValidUntil(ParticipationType, ValidUntil);
		if (hasValidUntil.IsFailure)
			return hasValidUntil;

		Status = OpportunityStatus.Published;
		AddEvent(new VolunteerOpportunityPublishedDomainEvent(Id, OrganizationId));
		return Result.Success();
	}

	public Result Unpublish()
	{
		if (Status != OpportunityStatus.Published)
			return Result.Failure(Error.Conflict("VolunteerOpportunity.NotPublished", "Only a published opportunity can be unpublished."));

		Status = OpportunityStatus.Unpublished;
		AddEvent(new VolunteerOpportunityUnpublishedDomainEvent(Id, OrganizationId));
		return Result.Success();
	}

	public Result Cancel(string? reason = null)
	{
		if (Status == OpportunityStatus.Cancelled)
			return Result.Failure(Error.Conflict("VolunteerOpportunity.AlreadyCancelled", "Opportunity is already cancelled."));

		if (Status == OpportunityStatus.Draft)
			return Result.Failure(Error.Conflict("VolunteerOpportunity.CannotCancelDraft", "A draft opportunity cannot be cancelled - delete it instead."));

		CancellationReason = reason;
		Status = OpportunityStatus.Cancelled;
		AddEvent(new VolunteerOpportunityCancelledDomainEvent(Id, OrganizationId, reason));
		return Result.Success();
	}

	public Result SetBannerImageUrl(string url)
	{
		if (string.IsNullOrWhiteSpace(url))
			return Result.Failure(Error.Validation("VolunteerOpportunity.BannerImageUrlRequired", "Banner image URL must not be empty."));

		BannerImageUrl = url;
		return Result.Success();
	}

	public void ClearBannerImageUrl()
	{
		BannerImageUrl = null;
	}

	public Result SetColor(string? color)
	{
		if (color is null)
		{
			Color = null;
			return Result.Success();
		}

		if (!EventColorContrast.IsValidHex(color))
			return Result.Failure(Error.Validation("VolunteerOpportunity.InvalidColor", "Color must be a #rrggbb hex value."));

		if (EventColorContrast.ContrastAgainstWhite(color) < EventColorContrast.MinimumContrastRatio)
			return Result.Failure(Error.Validation(
				"VolunteerOpportunity.ColorContrastTooLow",
				$"Color does not have enough contrast (at least {EventColorContrast.MinimumContrastRatio}:1 against white) to be usable as a calendar event color."));

		if (EventColorContrast.BestTextContrastRatio(color) < EventColorContrast.MinimumTextContrastRatio)
			return Result.Failure(Error.Validation(
				"VolunteerOpportunity.ColorTextContrastTooLow",
				$"Color does not leave enough contrast (at least {EventColorContrast.MinimumTextContrastRatio}:1) for its chip text to stay readable."));

		Color = color;
		return Result.Success();
	}

	public Result Rename(string titleDe, string? titleEn)
	{
		var validTitleDeLength = EnsureValidTitleLength(titleDe);
		if (validTitleDeLength.IsFailure)
			return validTitleDeLength;

		var validTitleEnLength = EnsureValidTitleLength(titleEn);
		if (validTitleEnLength.IsFailure)
			return validTitleEnLength;

		if (Status == OpportunityStatus.Published)
		{
			var publishable = EnsurePublishable(titleDe, DescriptionDe, IsRemote, Address);
			if (publishable.IsFailure)
				return publishable;
		}

		TitleDe = titleDe;
		TitleEn = titleEn;
		return Result.Success();
	}

	public Result ChangeDescription(string descriptionDe, string? descriptionEn)
	{
		var validDescriptionDeLength = EnsureValidDescriptionLength(descriptionDe);
		if (validDescriptionDeLength.IsFailure)
			return validDescriptionDeLength;

		var validDescriptionEnLength = EnsureValidDescriptionLength(descriptionEn);
		if (validDescriptionEnLength.IsFailure)
			return validDescriptionEnLength;

		if (Status == OpportunityStatus.Published)
		{
			var publishable = EnsurePublishable(TitleDe, descriptionDe, IsRemote, Address);
			if (publishable.IsFailure)
				return publishable;
		}

		DescriptionDe = descriptionDe;
		DescriptionEn = descriptionEn;
		return Result.Success();
	}

	public Result Relocate(bool isRemote, Address? address)
	{
		if (Status == OpportunityStatus.Published)
		{
			var publishable = EnsurePublishable(TitleDe, DescriptionDe, isRemote, address);
			if (publishable.IsFailure)
				return publishable;
		}

		var addressTextChanged = AddressTextChanged(Address, address);
		var needsGeocoding = !isRemote && address is not null && addressTextChanged;

		IsRemote = isRemote;

		if (isRemote || address is null || addressTextChanged)
		{
			Address = address;
			AddressGeocodingFailed = false;
		}

		if (needsGeocoding)
			AddEvent(new VolunteerOpportunityGeocodingRequestedDomainEvent(Id));

		return Result.Success();
	}

	public void ApplyGeocodingResult(Address resolvedAddress)
	{
		Address = resolvedAddress;
		AddressGeocodingFailed = false;
	}

	public void MarkAddressGeocodingFailed()
	{
		AddressGeocodingFailed = true;
	}

	private static bool AddressTextChanged(Address? prev, Address? next) =>
		prev?.Street != next?.Street ||
		prev?.HouseNumber != next?.HouseNumber ||
		prev?.ZipCode != next?.ZipCode ||
		prev?.City != next?.City;

	public void Reschedule(Occurrence occurrence)
	{
		Occurrence = occurrence;
	}

	public Result Recategorize(Category? category, IReadOnlyCollection<string> tags)
	{
		var validTags = EnsureValidTags(tags);
		if (validTags.IsFailure)
			return validTags;

		Category = category;
		_tags = new List<string>(tags);
		return Result.Success();
	}

	public Result ChangeCheckInMethod(CheckInMethod checkInMethod, IPinGenerator pinGenerator, DateTimeOffset now, string? checkInPin = null)
	{
		if (checkInMethod == CheckInMethod.PINCode && checkInPin is not null)
		{
			var validPin = EnsureValidPin(checkInPin);
			if (validPin.IsFailure)
				return validPin;
		}

		CheckInMethod = checkInMethod;
		if (checkInMethod == CheckInMethod.PINCode)
		{
			if (checkInPin is not null)
			{
				// Binds the organizer's own choice to the occurrence it's current for -
				// without this, EnsureCurrentCheckInPin would see this pin as belonging to
				// no occurrence (or a stale one) and silently replace it with a random one
				// the moment anyone next opens the check-in screen or tries a check-in.
				CheckInPin = checkInPin;
				CheckInPinTimeSlotId = CurrentCheckInTimeSlotId(now);
			}
			else if (CheckInPin is null)
			{
				CheckInPin = pinGenerator.GeneratePin();
				CheckInPinTimeSlotId = CurrentCheckInTimeSlotId(now);
			}
		}
		else
		{
			CheckInPin = null;
			CheckInPinTimeSlotId = null;
		}

		return Result.Success();
	}

	// Called from both the organizer's check-in-screen read (GetOpportunityCheckInPinQuery)
	// and the volunteer's PIN submission (CheckInWithPinCommandHandler) - whichever happens
	// first for a newly-current occurrence rotates the PIN, so a volunteer replaying a PIN
	// they learned at an earlier occurrence always lands on a value that has already moved
	// on. Returns whether it actually rotated, so a read-only query knows whether it has
	// something new to persist.
	public bool EnsureCurrentCheckInPin(DateTimeOffset now, IPinGenerator pinGenerator)
	{
		if (CheckInMethod != CheckInMethod.PINCode)
			return false;

		var currentTimeSlotId = CurrentCheckInTimeSlotId(now);
		if (CheckInPin is not null && currentTimeSlotId == CheckInPinTimeSlotId)
			return false;

		CheckInPin = pinGenerator.GeneratePin();
		CheckInPinTimeSlotId = currentTimeSlotId;
		return true;
	}

	// The earliest slot whose check-in window (TimeSlot.CheckInWindowAfter past its end)
	// has not yet closed - the occurrence the PIN should currently be protecting. Null for
	// IndividualContact (no slots to key off) and once every slot's window has closed.
	private TimeSlotId? CurrentCheckInTimeSlotId(DateTimeOffset now)
	{
		if (ParticipationType != ParticipationType.ScheduledSlots)
			return null;

		return _timeSlots
			.Where(ts => now <= ts.EndDateTime + TimeSlot.CheckInWindowAfter)
			.OrderBy(ts => ts.StartDateTime)
			.FirstOrDefault()
			?.Id;
	}

	public void SwitchParticipationType(ParticipationType participationType)
	{
		if (ParticipationType == ParticipationType.ScheduledSlots && participationType != ParticipationType.ScheduledSlots)
			_timeSlots.Clear();

		if (ParticipationType == ParticipationType.IndividualContact && participationType != ParticipationType.IndividualContact)
			ValidUntil = null;

		ParticipationType = participationType;
	}

	public Result SetValidUntil(DateTimeOffset? validUntil, DateTimeOffset now)
	{
		var validation = EnsureValidValidUntil(ParticipationType, validUntil, now);
		if (validation.IsFailure)
			return validation;

		ValidUntil = validUntil;
		return Result.Success();
	}

	// Callers decide "material change" from a before/after snapshot the aggregate
	// itself doesn't retain (see UpdateVolunteerOpportunityCommandHandler), so this is
	// an explicit trigger rather than something raised from within a single mutator.
	public void NotifyVolunteersOfUpdate(TimeSlotId? timeSlotId = null) =>
		AddEvent(new VolunteerOpportunityUpdatedDomainEvent(Id, timeSlotId));

	public Result<TimeSlot> AddTimeSlot(
		DateTimeOffset startDateTime,
		DateTimeOffset endDateTime,
		int? maxParticipants,
		DateTimeOffset now,
		Guid? seriesId = null,
		string? recurrenceFrequency = null,
		int? recurrenceCount = null)
	{
		if (ParticipationType != ParticipationType.ScheduledSlots)
			return Result.Failure<TimeSlot>(Error.Validation(
				"VolunteerOpportunity.TimeSlotNotAllowed",
				"Time slots can only be added to opportunities with Scheduled slots participation type."));

		var timeSlotResult = TimeSlot.Create(startDateTime, endDateTime, maxParticipants, now, seriesId, recurrenceFrequency, recurrenceCount);
		if (timeSlotResult.IsFailure)
			return timeSlotResult;

		_timeSlots.Add(timeSlotResult.Value);
		return timeSlotResult;
	}

	public Result UpdateTimeSlot(TimeSlotId timeSlotId, DateTimeOffset startDateTime, DateTimeOffset endDateTime, int? maxParticipants, DateTimeOffset now)
	{
		var timeSlot = _timeSlots.Find(ts => ts.Id == timeSlotId);
		if (timeSlot is null)
			return Result.Failure(Error.NotFound("VolunteerOpportunity.TimeSlotNotFound", $"Time slot with id '{timeSlotId.Value}' not found."));

		return timeSlot.Update(startDateTime, endDateTime, maxParticipants, now);
	}

	public Result UpdateTimeSlotCapacity(TimeSlotId timeSlotId, int? maxParticipants)
	{
		var timeSlot = _timeSlots.Find(ts => ts.Id == timeSlotId);
		if (timeSlot is null)
			return Result.Failure(Error.NotFound("VolunteerOpportunity.TimeSlotNotFound", $"Time slot with id '{timeSlotId.Value}' not found."));

		return timeSlot.UpdateCapacity(maxParticipants);
	}

	public Result RemoveTimeSlot(TimeSlotId timeSlotId)
	{
		var timeSlot = _timeSlots.Find(ts => ts.Id == timeSlotId);
		if (timeSlot is null)
			return Result.Failure(Error.NotFound("VolunteerOpportunity.TimeSlotNotFound", $"Time slot with id '{timeSlotId.Value}' not found."));

		_timeSlots.Remove(timeSlot);
		return Result.Success();
	}

	public Result MarkDeleted(DateTimeOffset deletedOn)
	{
		if (IsDeleted)
			return Result.Failure(Error.Conflict("VolunteerOpportunity.AlreadyDeleted", "Opportunity is already shadow-deleted."));

		IsDeleted = true;
		DeletedOn = deletedOn;
		return Result.Success();
	}

	public Result Restore()
	{
		if (!IsDeleted)
			return Result.Failure(Error.Conflict("VolunteerOpportunity.NotDeleted", "Opportunity is not shadow-deleted."));

		IsDeleted = false;
		DeletedOn = null;
		return Result.Success();
	}
}
