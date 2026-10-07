using Domain.Common;
using Domain.Engagements;
using Domain.VolunteerOpportunities;

namespace Application.Common.Email;

// Builds the "Termin" and "Ort" blocks and the title every volunteer email shows, so an
// email names the date and place instead of sending the reader to the app for them.
public static class EmailFacts
{
	// The live slot wins over the engagement's sign-up-time snapshot because a slot can be
	// rescheduled after sign-up. The snapshot only fills in when the slot itself is gone -
	// cancelling a series removes its slots in the same transaction.
	public static EmailFact.Schedule? Schedule(IEnumerable<Engagement> engagements, VolunteerOpportunity? opportunity)
	{
		var slots = engagements
			.Select(engagement => TimeRange(engagement, opportunity))
			.OfType<EmailTimeRange>()
			.ToList();

		return slots.Count == 0 ? null : new EmailFact.Schedule(slots);
	}

	public static EmailFact.Schedule Schedule(TimeSlot timeSlot) =>
		new([new EmailTimeRange(timeSlot.StartDateTime, timeSlot.EndDateTime)]);

	public static EmailFact.Location Location(VolunteerOpportunity opportunity) =>
		new(opportunity.IsRemote || opportunity.Address is null ? null : FormatAddress(opportunity.Address));

	public static string OpportunityTitle(VolunteerOpportunity opportunity, string language) =>
		language == "en" && !string.IsNullOrWhiteSpace(opportunity.TitleEn)
			? opportunity.TitleEn
			: opportunity.TitleDe;

	private static EmailTimeRange? TimeRange(Engagement engagement, VolunteerOpportunity? opportunity)
	{
		if (engagement.TimeSlotId is null)
			return null;

		var slot = opportunity?.TimeSlots.FirstOrDefault(ts => ts.Id == engagement.TimeSlotId);
		if (slot is not null)
			return new EmailTimeRange(slot.StartDateTime, slot.EndDateTime);

		return engagement is { TimeSlotStartDateTime: { } start, TimeSlotEndDateTime: { } end }
			? new EmailTimeRange(start, end)
			: null;
	}

	private static string FormatAddress(Address address) =>
		$"{address.Street} {address.HouseNumber}, {address.ZipCode} {address.City}";
}
