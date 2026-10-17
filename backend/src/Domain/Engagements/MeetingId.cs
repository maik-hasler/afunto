using Domain.Primitives;

namespace Domain.Engagements;

public readonly record struct MeetingId : IValueObject
{
	public Guid Value { get; }

	private MeetingId(Guid value) => Value = value;

	public static Result<MeetingId> Create(Guid value) =>
		value == Guid.Empty
			? Result.Failure<MeetingId>(Error.Validation("MeetingId.Empty", "MeetingId must not be empty."))
			: Result.Success(new MeetingId(value));

	public static MeetingId New() => new(Guid.CreateVersion7());
}
