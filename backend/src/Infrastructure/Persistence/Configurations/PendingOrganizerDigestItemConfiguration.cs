using Infrastructure.Persistence.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

internal sealed class PendingOrganizerDigestItemConfiguration
	: IEntityTypeConfiguration<PendingOrganizerDigestItem>
{
	public void Configure(
		EntityTypeBuilder<PendingOrganizerDigestItem> builder)
	{
		builder.HasKey(i => i.Id);

		builder.Property(i => i.Id).ValueGeneratedNever();

		builder.Property(i => i.OrganizerId).IsRequired();

		builder.Property(i => i.OrganizationId);

		builder.Property(i => i.OpportunityTitle).IsRequired();

		builder.Property(i => i.VolunteerName).IsRequired();

		builder.Property(i => i.Kind).IsRequired().HasConversion<string>();

		builder.Property(i => i.OccurredOnUtc).IsRequired();

		builder.HasIndex(i => i.OrganizerId)
			.HasFilter("digest_sent_on_utc IS NULL");

		builder.HasIndex(i => i.DigestSentOnUtc);
	}
}
