using System.Security.Cryptography;
using System.Text;
using Application.Common.Exceptions;
using Application.Common.Messaging;
using Application.Common.Persistence;
using Application.Common.RateLimiting;
using Domain.Engagements;
using Domain.Primitives;
using Domain.VolunteerOpportunities;

namespace Application.Engagements.CheckInWithPin.v1;

internal sealed class CheckInWithPinCommandHandler(
	IApplicationDbContext dbContext,
	ICheckInAttemptLimiter attemptLimiter,
	IPinGenerator pinGenerator)
	: ICommandHandler<CheckInWithPinCommand, Engagement>
{
	public async ValueTask<Engagement> Handle(
		CheckInWithPinCommand request,
		CancellationToken cancellationToken = default)
	{
		var engagement = await dbContext.Engagements.FindAsync(request.EngagementId, cancellationToken)
			?? throw new ResultFailureException(Error.NotFound("Engagement.NotFound", $"Engagement '{request.EngagementId.Value}' not found."));

		if (engagement.IsAnonymized)
			throw new ResultFailureException(Error.Conflict("Engagement.Anonymized", "This engagement's volunteer account has been deleted."));

		if (engagement.VolunteerId!.Value.Value != request.RequestingUserId.Value)
			throw new ResultFailureException(Error.Validation("Engagement.NotOwner", "You can only check in your own engagement."));

		if (await attemptLimiter.IsLockedOutAsync(engagement.VolunteerId.Value, engagement.OpportunityId, cancellationToken))
			throw new ResultFailureException(Error.Forbidden(
				"Engagement.CheckInLocked",
				"Too many failed PIN attempts. Try again later."));

		var opportunity = await dbContext.VolunteerOpportunities.FindAsync(engagement.OpportunityId, cancellationToken)
			?? throw new ResultFailureException(Error.NotFound("VolunteerOpportunity.NotFound", "Opportunity not found."));

		if (opportunity.CheckInMethod != CheckInMethod.PINCode)
			throw new ResultFailureException(Error.Conflict("Engagement.CheckInMethodNotPin", "This opportunity does not use PIN check-in."));

		if (string.IsNullOrWhiteSpace(request.Pin))
			throw new ResultFailureException(Error.Validation("Engagement.PinRequired", "PIN is required."));

		var now = DateTimeOffset.UtcNow;

		// Rotates the opportunity's PIN onto whichever occurrence is current before
		// comparing, so a PIN learned from a past occurrence never matches here even if
		// nobody has looked at the organizer's check-in screen since (afunto#2202).
		opportunity.EnsureCurrentCheckInPin(now, pinGenerator);

		if (!PinsMatch(opportunity.CheckInPin, request.Pin))
		{
			await attemptLimiter.RegisterFailedAttemptAsync(engagement.VolunteerId.Value, engagement.OpportunityId, cancellationToken);
			throw new ResultFailureException(Error.Validation("Engagement.InvalidPin", "Invalid PIN."));
		}

		await attemptLimiter.ResetAsync(engagement.VolunteerId.Value, engagement.OpportunityId, cancellationToken);

		engagement.CheckIn(now).ThrowIfFailure();

		return engagement;
	}

	private static bool PinsMatch(string? storedPin, string suppliedPin)
	{
		if (storedPin is null)
			return false;

		var storedBytes = Encoding.UTF8.GetBytes(storedPin);
		var suppliedBytes = Encoding.UTF8.GetBytes(suppliedPin);

		return storedBytes.Length == suppliedBytes.Length
			&& CryptographicOperations.FixedTimeEquals(storedBytes, suppliedBytes);
	}
}
