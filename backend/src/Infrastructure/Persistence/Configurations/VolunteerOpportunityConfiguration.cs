using Application.Common.Exceptions;
using Domain.VolunteerOpportunities;
using Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

internal sealed class VolunteerOpportunityConfiguration
	: IEntityTypeConfiguration<VolunteerOpportunity>
{
	public void Configure(
		EntityTypeBuilder<VolunteerOpportunity> builder)
	{
		builder.HasKey(vo => vo.Id);

		builder.ToTable(t =>
		{
			t.HasCheckConstraint(
				"ck_volunteer_opportunity_occurrence_valid",
				"occurrence IN ('OneTime', 'Recurring')");
			t.HasCheckConstraint(
				"ck_volunteer_opportunity_participation_type_valid",
				"participation_type IN ('ScheduledSlots', 'IndividualContact')");
			t.HasCheckConstraint(
				"ck_volunteer_opportunity_check_in_method_valid",
				"check_in_method IN ('None', 'QRCode', 'PINCode', 'Manual')");
			t.HasCheckConstraint(
				"ck_volunteer_opportunity_category_valid",
				"category IN ('Social', 'Environment', 'Sport', 'Education', 'DisasterRelief', 'Health', 'Animals', 'Culture', 'Technology', 'Other')");
			t.HasCheckConstraint(
				"ck_volunteer_opportunity_status_valid",
				"status IN ('Draft', 'Published', 'Unpublished', 'Cancelled')");
			t.HasCheckConstraint(
				"ck_volunteer_opportunity_tags_count",
				$"cardinality(tags) <= {VolunteerOpportunity.MaxTagsCount}");
		});

		builder.Property(vo => vo.Id)
			.HasConversion(
				id => id.Value,
				guid => VolunteerOpportunityId.Create(guid).GetValueOrThrow())
			.ValueGeneratedNever();

		builder.Property(vo => vo.OrganizationId)
			.HasConversion(
				id => id.Value,
				guid => OrganizationId.Create(guid).GetValueOrThrow())
			.IsRequired();

		builder.Property(vo => vo.TitleDe)
			.HasMaxLength(VolunteerOpportunity.MaxTitleLength)
			.IsRequired();

		builder.Property(vo => vo.TitleEn)
			.HasMaxLength(VolunteerOpportunity.MaxTitleLength)
			.IsRequired(false);

		builder.Property(vo => vo.DescriptionDe)
			.HasMaxLength(VolunteerOpportunity.MaxDescriptionLength)
			.IsRequired();

		builder.Property(vo => vo.DescriptionEn)
			.HasMaxLength(VolunteerOpportunity.MaxDescriptionLength)
			.IsRequired(false);

		builder.Property(vo => vo.IsRemote)
			.IsRequired();

		builder.OwnsOne(vo => vo.Address, address =>
		{
			address.Property(a => a.Street).HasMaxLength(200).IsRequired();
			address.Property(a => a.HouseNumber).HasMaxLength(20).IsRequired();
			address.Property(a => a.ZipCode).HasMaxLength(5).IsRequired();
			address.Property(a => a.City).HasMaxLength(100).IsRequired();
			address.Property(a => a.Latitude);
			address.Property(a => a.Longitude);

			address.HasIndex(a => new { a.Latitude, a.Longitude });
		});

		builder.Property(vo => vo.AddressGeocodingFailed)
			.HasDefaultValue(false);

		builder.Property(vo => vo.Occurrence)
			.HasConversion<string>()
			.IsRequired();

		builder.Property(vo => vo.ParticipationType)
			.HasConversion<string>()
			.IsRequired();

		builder.Property(vo => vo.CheckInMethod)
			.HasConversion<string>()
			.IsRequired();

		builder.Property(vo => vo.Category)
			.HasConversion<string>()
			.IsRequired(false);

		builder.Property(vo => vo.Status)
			.HasConversion<string>()
			.IsRequired();

		builder.Property(vo => vo.CancellationReason);

		builder.Property(vo => vo.BannerImageUrl);

		builder.Property(vo => vo.Color);

		builder.PrimitiveCollection(vo => vo.Tags)
			.ElementType(e => e.HasMaxLength(VolunteerOpportunity.MaxTagLength));

		builder.Property(vo => vo.CheckInPin);

		builder.Property(vo => vo.CheckInPinTimeSlotId)
			.HasConversion(
				id => id.HasValue ? id.Value.Value : (Guid?)null,
				guid => guid.HasValue ? TimeSlotId.Create(guid.Value).GetValueOrThrow() : null);

		builder.Property(vo => vo.ValidUntil);

		builder.Property(vo => vo.MembersOnly)
			.HasDefaultValue(false);

		builder.Property(vo => vo.CreatedOn);

		builder.Property(vo => vo.ModifiedOn);

		builder.Property(vo => vo.IsDeleted)
			.HasDefaultValue(false);

		builder.Property(vo => vo.DeletedOn);

		builder.HasQueryFilter(vo => !vo.IsDeleted);

		builder.HasMany(vo => vo.TimeSlots)
			.WithOne()
			.HasForeignKey("volunteer_opportunity_id")
			.IsRequired();

		builder.HasIndex(vo => vo.OrganizationId);

		builder.HasOne<Organization>()
			.WithMany()
			.HasForeignKey(vo => vo.OrganizationId)
			.OnDelete(DeleteBehavior.Cascade);

		builder.HasIndex(vo => vo.CheckInPinTimeSlotId);

		// Mirrors EngagementConfiguration's identical TimeSlotId relationship. Explicit
		// SetNull (not the ClientSetNull default, which renders as ON DELETE NO ACTION in
		// Postgres) matters here specifically: RemoveTimeSlot/SwitchParticipationType can
		// delete the very TimeSlot this points to without clearing it first, and rely on
		// this to keep that from ever producing a dangling reference or a save-time FK
		// violation - EnsureCurrentCheckInPin already treats a stale value as "rotate",
		// which is the same outcome this constraint additionally guarantees at the DB level.
		builder.HasOne<TimeSlot>()
			.WithMany()
			.HasForeignKey(vo => vo.CheckInPinTimeSlotId)
			.IsRequired(false)
			.OnDelete(DeleteBehavior.SetNull);

		builder.HasIndex(vo => new { vo.Status, vo.CreatedOn });

		builder.HasIndex(vo => vo.Tags)
			.HasMethod("gin");

		builder.Property<uint>("Version").IsRowVersion();

		builder.Ignore(vo => vo.Events);
	}
}
