using Domain.Primitives;

namespace Domain.VolunteerOpportunities;

public sealed class TimeSlot
	: Entity<TimeSlotId>,
		IAuditableEntity
{
	// How far around a slot's own window a check-in against it is honoured
	// (Engagement.CheckIn) - and, for a PINCode opportunity, how long its
	// current PIN keeps covering this slot before VolunteerOpportunity treats
	// the next slot as due and rotates (afunto#2202). Before the start,
	// so an organizer can check volunteers in as they arrive early; well past
	// the end, since Manual/QRCode check-in commonly happens during a
	// post-event wrap-up rather than the instant the slot ends.
	public static readonly TimeSpan CheckInWindowBefore = TimeSpan.FromHours(1);

	public static readonly TimeSpan CheckInWindowAfter = TimeSpan.FromHours(2);

	// An organizer typing a capacity has no upper bound to bump into otherwise,
	// so a slip of the keyboard persists as a nine-digit figure that reads as a
	// real number of seats everywhere it is shown (afunto#2325). Well
	// above any plausible shift, low enough to catch a typo.
	public const int MaxParticipantsLimit = 10_000;

	public DateTimeOffset StartDateTime { get; private set; }

	public DateTimeOffset EndDateTime { get; private set; }

	public int? MaxParticipants { get; private set; }

	public Guid? SeriesId { get; private set; }

	public string? RecurrenceFrequency { get; private set; }

	public int? RecurrenceCount { get; private set; }

	public DateTimeOffset CreatedOn { get; private set; }

	public DateTimeOffset? ModifiedOn { get; private set; }

#pragma warning disable CS8618
	private TimeSlot() : base(default) { }
#pragma warning restore CS8618

	private TimeSlot(
		TimeSlotId id,
		DateTimeOffset startDateTime,
		DateTimeOffset endDateTime,
		int? maxParticipants,
		Guid? seriesId,
		string? recurrenceFrequency,
		int? recurrenceCount)
		: base(id)
	{
		StartDateTime = startDateTime;
		EndDateTime = endDateTime;
		MaxParticipants = maxParticipants;
		SeriesId = seriesId;
		RecurrenceFrequency = recurrenceFrequency;
		RecurrenceCount = recurrenceCount;
	}

	public static Result<TimeSlot> Create(
		DateTimeOffset startDateTime,
		DateTimeOffset endDateTime,
		int? maxParticipants,
		DateTimeOffset now,
		Guid? seriesId = null,
		string? recurrenceFrequency = null,
		int? recurrenceCount = null)
	{
		var validation = Validate(startDateTime, endDateTime, maxParticipants, now);
		if (validation.IsFailure)
			return Result.Failure<TimeSlot>(validation.Error);

		return new TimeSlot(
			TimeSlotId.New(),
			startDateTime,
			endDateTime,
			maxParticipants,
			seriesId,
			recurrenceFrequency,
			recurrenceCount);
	}

	public Result Update(DateTimeOffset startDateTime, DateTimeOffset endDateTime, int? maxParticipants, DateTimeOffset now)
	{
		var validation = Validate(startDateTime, endDateTime, maxParticipants, now);
		if (validation.IsFailure)
			return validation;

		StartDateTime = startDateTime;
		EndDateTime = endDateTime;
		MaxParticipants = maxParticipants;
		return Result.Success();
	}

	public Result UpdateCapacity(int? maxParticipants)
	{
		var validation = ValidateCapacity(maxParticipants);
		if (validation.IsFailure)
			return validation;

		MaxParticipants = maxParticipants;
		return Result.Success();
	}

	private static Result Validate(DateTimeOffset startDateTime, DateTimeOffset endDateTime, int? maxParticipants, DateTimeOffset now)
	{
		if (startDateTime <= now)
			return Result.Failure(Error.Validation("TimeSlot.StartMustBeFuture", "Start date must be in the future."));

		if (endDateTime <= startDateTime)
			return Result.Failure(Error.Validation("TimeSlot.EndMustBeAfterStart", "End date must be after start date."));

		return ValidateCapacity(maxParticipants);
	}

	private static Result ValidateCapacity(int? maxParticipants)
	{
		if (maxParticipants is <= 0)
			return Result.Failure(Error.Validation("TimeSlot.MaxParticipantsMustBePositive", "Max participants must be greater than zero."));

		if (maxParticipants > MaxParticipantsLimit)
			return Result.Failure(Error.Validation("TimeSlot.MaxParticipantsTooLarge", $"Max participants must not exceed {MaxParticipantsLimit}."));

		return Result.Success();
	}
}
