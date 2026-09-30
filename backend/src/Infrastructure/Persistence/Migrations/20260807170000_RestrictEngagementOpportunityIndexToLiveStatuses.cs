using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
	/// <inheritdoc />
	// afunto#1724: ix_engagement_volunteer_id_opportunity_id and
	// ix_engagement_volunteer_id_time_slot_id previously disagreed - a volunteer
	// signed up for two time slots of the same recurring opportunity legitimately
	// holds two rows sharing (volunteer_id, opportunity_id), differing only by
	// time_slot_id. Deleting both slots at once cancels their engagements first,
	// then hard-deletes the TimeSlot rows, which nulls time_slot_id on both
	// engagements via its ON DELETE SET NULL FK - the two now-Cancelled rows then
	// collided on this partial index (both matching volunteer_id + opportunity_id
	// with time_slot_id IS NULL) and 500'd the deletion. Narrowing the filter to
	// only live (non-terminal) engagements excludes cancelled/withdrawn rows from
	// the index entirely, so two of them landing on the same (volunteer_id,
	// opportunity_id, time_slot_id IS NULL) combination no longer collides.
	public partial class RestrictEngagementOpportunityIndexToLiveStatuses : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropIndex(
				name: "ix_engagement_volunteer_id_opportunity_id",
				table: "engagement");

			migrationBuilder.CreateIndex(
				name: "ix_engagement_volunteer_id_opportunity_id",
				table: "engagement",
				columns: new[] { "volunteer_id", "opportunity_id" },
				unique: true,
				filter: "time_slot_id IS NULL AND status NOT IN ('Cancelled', 'Withdrawn')");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropIndex(
				name: "ix_engagement_volunteer_id_opportunity_id",
				table: "engagement");

			migrationBuilder.CreateIndex(
				name: "ix_engagement_volunteer_id_opportunity_id",
				table: "engagement",
				columns: new[] { "volunteer_id", "opportunity_id" },
				unique: true,
				filter: "time_slot_id IS NULL");
		}
	}
}
