using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
	/// <inheritdoc />
	public partial class RemoveBackendEmail : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			// The two events lost their only handlers with the emails. OutboxMessage.ToDomainEvent
			// throws on an unknown type, so pending rows would loop in the processor until retention.
			migrationBuilder.Sql(
				"DELETE FROM outbox_message WHERE type IN " +
				"('Domain.Engagements.EngagementReminderDueDomainEvent', " +
				"'Domain.VolunteerOpportunities.VolunteerOpportunityUpdatedDomainEvent');");

			migrationBuilder.DropTable(
				name: "pending_organizer_digest_item");

			migrationBuilder.DropColumn(
				name: "notify_on_engagement_confirmed",
				table: "user");

			migrationBuilder.DropColumn(
				name: "notify_on_engagement_reminder",
				table: "user");

			migrationBuilder.DropColumn(
				name: "notify_on_new_sign_up",
				table: "user");

			migrationBuilder.DropColumn(
				name: "notify_on_withdrawal",
				table: "user");

			migrationBuilder.DropColumn(
				name: "preferred_language",
				table: "user");

			migrationBuilder.DropColumn(
				name: "unsubscribe_token",
				table: "user");

			migrationBuilder.DropColumn(
				name: "reminder_sent_at",
				table: "engagement");

			migrationBuilder.DropColumn(
				name: "status_notified_at",
				table: "engagement");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.AddColumn<bool>(
				name: "notify_on_engagement_confirmed",
				table: "user",
				type: "boolean",
				nullable: false,
				defaultValue: true);

			migrationBuilder.AddColumn<bool>(
				name: "notify_on_engagement_reminder",
				table: "user",
				type: "boolean",
				nullable: false,
				defaultValue: true);

			migrationBuilder.AddColumn<bool>(
				name: "notify_on_new_sign_up",
				table: "user",
				type: "boolean",
				nullable: false,
				defaultValue: true);

			migrationBuilder.AddColumn<bool>(
				name: "notify_on_withdrawal",
				table: "user",
				type: "boolean",
				nullable: false,
				defaultValue: true);

			migrationBuilder.AddColumn<string>(
				name: "preferred_language",
				table: "user",
				type: "character varying(5)",
				maxLength: 5,
				nullable: true);

			migrationBuilder.AddColumn<Guid>(
				name: "unsubscribe_token",
				table: "user",
				type: "uuid",
				nullable: false,
				defaultValueSql: "gen_random_uuid()");

			migrationBuilder.AddColumn<DateTimeOffset>(
				name: "reminder_sent_at",
				table: "engagement",
				type: "timestamp with time zone",
				nullable: true);

			migrationBuilder.AddColumn<DateTimeOffset>(
				name: "status_notified_at",
				table: "engagement",
				type: "timestamp with time zone",
				nullable: true);

			migrationBuilder.CreateTable(
				name: "pending_organizer_digest_item",
				columns: table => new
				{
					id = table.Column<Guid>(type: "uuid", nullable: false),
					claimed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
					digest_sent_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
					kind = table.Column<string>(type: "text", nullable: false),
					occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
					opportunity_title = table.Column<string>(type: "text", nullable: false),
					organization_id = table.Column<Guid>(type: "uuid", nullable: true),
					organizer_id = table.Column<Guid>(type: "uuid", nullable: false),
					volunteer_name = table.Column<string>(type: "text", nullable: false)
				},
				constraints: table =>
				{
					table.PrimaryKey("pk_pending_organizer_digest_item", x => x.id);
				});

			migrationBuilder.CreateIndex(
				name: "ix_pending_organizer_digest_item_digest_sent_on_utc",
				table: "pending_organizer_digest_item",
				column: "digest_sent_on_utc");

			migrationBuilder.CreateIndex(
				name: "ix_pending_organizer_digest_item_organizer_id",
				table: "pending_organizer_digest_item",
				column: "organizer_id",
				filter: "digest_sent_on_utc IS NULL");
		}
	}
}
