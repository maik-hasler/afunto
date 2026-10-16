namespace Application.UnitTests.Common.Time;

/// <summary>A clock frozen at one instant, for handlers that stamp the current time.</summary>
internal sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
	public override DateTimeOffset GetUtcNow() => utcNow;
}
