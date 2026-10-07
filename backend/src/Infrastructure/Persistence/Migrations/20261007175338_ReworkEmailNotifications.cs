using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
	/// <inheritdoc />
	public partial class ReworkEmailNotifications : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropColumn(
				name: "notify_on_engagement_cancelled",
				table: "user");

			migrationBuilder.AddColumn<Guid>(
				name: "organization_id",
				table: "pending_organizer_digest_item",
				type: "uuid",
				nullable: true);

			migrationBuilder.AddColumn<DateTimeOffset>(
				name: "status_notified_at",
				table: "engagement",
				type: "timestamp with time zone",
				nullable: true);

			// Engagements whose status email already went out under the old one-email-per-
			// engagement scheme must not be swept into the volunteer's next bundled email.
			// The last hour stays unmarked so an outbox event still in flight at deploy
			// time keeps its email - the worst case is one already-announced date
			// appearing again in a later bundle, never a lost email.
			migrationBuilder.Sql("""
				UPDATE engagement
				SET status_notified_at = COALESCE(modified_on, created_on)
				WHERE status IN ('Confirmed', 'Cancelled')
					AND COALESCE(modified_on, created_on) < now() - interval '1 hour';
				""");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropColumn(
				name: "organization_id",
				table: "pending_organizer_digest_item");

			migrationBuilder.DropColumn(
				name: "status_notified_at",
				table: "engagement");

			migrationBuilder.AddColumn<bool>(
				name: "notify_on_engagement_cancelled",
				table: "user",
				type: "boolean",
				nullable: false,
				defaultValue: true);
		}
	}
}
