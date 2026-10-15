using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
	/// <inheritdoc />
	public partial class AddEngagementMeetings : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropCheckConstraint(
				name: "ck_notification_kind_valid",
				table: "notification");

			migrationBuilder.AddColumn<DateTimeOffset>(
				name: "meeting_ends_at",
				table: "engagement",
				type: "timestamp with time zone",
				nullable: true);

			migrationBuilder.AddColumn<Guid>(
				name: "meeting_id",
				table: "engagement",
				type: "uuid",
				nullable: true);

			migrationBuilder.AddColumn<string>(
				name: "meeting_location",
				table: "engagement",
				type: "character varying(300)",
				maxLength: 300,
				nullable: true);

			migrationBuilder.AddColumn<string>(
				name: "meeting_note",
				table: "engagement",
				type: "character varying(500)",
				maxLength: 500,
				nullable: true);

			migrationBuilder.AddColumn<int>(
				name: "meeting_proposal_count",
				table: "engagement",
				type: "integer",
				nullable: false,
				defaultValue: 0);

			migrationBuilder.AddColumn<string>(
				name: "meeting_proposed_by",
				table: "engagement",
				type: "text",
				nullable: true);

			migrationBuilder.AddColumn<string>(
				name: "meeting_response",
				table: "engagement",
				type: "text",
				nullable: true);

			migrationBuilder.AddColumn<DateTimeOffset>(
				name: "meeting_starts_at",
				table: "engagement",
				type: "timestamp with time zone",
				nullable: true);

			migrationBuilder.AddCheckConstraint(
				name: "ck_notification_kind_valid",
				table: "notification",
				sql: "kind IN ('EngagementCreated', 'EngagementConfirmed', 'EngagementCancelled', 'EngagementWithdrawn', 'OpportunityUpdated', 'OpportunityDeleted', 'OpportunityUnpublished', 'OpportunityCancelled', 'InvitationReceived', 'InvitationAccepted', 'InvitationDeclined', 'FeedbackSubmitted', 'MeetingProposed', 'MeetingAccepted', 'MeetingDeclined', 'MeetingCancelled')");

			migrationBuilder.CreateIndex(
				name: "ix_engagement_meeting_id",
				table: "engagement",
				column: "meeting_id");

			migrationBuilder.AddCheckConstraint(
				name: "ck_engagement_meeting_proposed_by_valid",
				table: "engagement",
				sql: "meeting_proposed_by IS NULL OR meeting_proposed_by IN ('Organization', 'Volunteer')");

			migrationBuilder.AddCheckConstraint(
				name: "ck_engagement_meeting_response_valid",
				table: "engagement",
				sql: "meeting_response IS NULL OR meeting_response IN ('Pending', 'Accepted', 'Declined')");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropCheckConstraint(
				name: "ck_notification_kind_valid",
				table: "notification");

			migrationBuilder.DropIndex(
				name: "ix_engagement_meeting_id",
				table: "engagement");

			migrationBuilder.DropCheckConstraint(
				name: "ck_engagement_meeting_proposed_by_valid",
				table: "engagement");

			migrationBuilder.DropCheckConstraint(
				name: "ck_engagement_meeting_response_valid",
				table: "engagement");

			migrationBuilder.DropColumn(
				name: "meeting_ends_at",
				table: "engagement");

			migrationBuilder.DropColumn(
				name: "meeting_id",
				table: "engagement");

			migrationBuilder.DropColumn(
				name: "meeting_location",
				table: "engagement");

			migrationBuilder.DropColumn(
				name: "meeting_note",
				table: "engagement");

			migrationBuilder.DropColumn(
				name: "meeting_proposal_count",
				table: "engagement");

			migrationBuilder.DropColumn(
				name: "meeting_proposed_by",
				table: "engagement");

			migrationBuilder.DropColumn(
				name: "meeting_response",
				table: "engagement");

			migrationBuilder.DropColumn(
				name: "meeting_starts_at",
				table: "engagement");

			migrationBuilder.AddCheckConstraint(
				name: "ck_notification_kind_valid",
				table: "notification",
				sql: "kind IN ('EngagementCreated', 'EngagementConfirmed', 'EngagementCancelled', 'EngagementWithdrawn', 'OpportunityUpdated', 'OpportunityDeleted', 'OpportunityUnpublished', 'OpportunityCancelled', 'InvitationReceived', 'InvitationAccepted', 'InvitationDeclined', 'FeedbackSubmitted')");
		}
	}
}
