import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import VolunteerMeetingPanel from "./VolunteerMeetingPanel";
import type { EngagementSummary } from "../../client";
import { renderWithProviders } from "../../test/render";

const { api } = await vi.hoisted(async () => {
	const { createApiMock } = await import("../../test/apiMock");
	return { api: createApiMock() };
});

vi.mock("../../hooks/useApiClient", () => ({ useApiClient: () => api }));

const ENGAGEMENT_ID = "aaaaaaaa-0000-0000-0000-000000000001";
const MEETING_ID = "44444444-4444-4444-4444-444444444444";
const NEXT_YEAR = new Date().getUTCFullYear() + 1;

function interest(
	meeting: Partial<NonNullable<EngagementSummary["meeting"]>> | null,
	status = "Pending",
): EngagementSummary {
	return {
		id: ENGAGEMENT_ID,
		opportunityId: "22222222-2222-2222-2222-222222222222",
		opportunityTitle: "Kennenlernen",
		organizationId: null,
		organizationName: null,
		volunteerId: "33333333-3333-3333-3333-333333333333",
		timeSlotId: null,
		message: "Ich helfe gern.",
		status,
		isCheckedIn: false,
		hasFeedback: false,
		createdOn: new Date(Date.UTC(2026, 7, 10)),
		meeting: meeting && {
			id: MEETING_ID,
			startsAt: new Date(`${NEXT_YEAR}-03-10T17:00:00Z`),
			endsAt: new Date(`${NEXT_YEAR}-03-10T18:00:00Z`),
			format: "InPerson",
			location: "Clubhouse",
			note: null,
			proposedBy: "Organization",
			response: "Pending",
			participantCount: 1,
			...meeting,
		},
	};
}

function renderPanel(engagement: EngagementSummary) {
	const onChanged = vi.fn();
	renderWithProviders(
		<VolunteerMeetingPanel engagement={engagement} onChanged={onChanged} />,
		{ lng: "en", auth: { isAuthenticated: true } },
	);
	return { onChanged };
}

beforeEach(() => {
	api.__reset();
});

describe("VolunteerMeetingPanel", () => {
	it("accepts the organization's proposal and reloads", async () => {
		const { onChanged } = renderPanel(interest({}));

		await userEvent.click(screen.getByRole("button", { name: "Accept" }));

		await waitFor(() => expect(onChanged).toHaveBeenCalled());
		expect(api.respondToMeetingAsVolunteer).toHaveBeenCalledWith({
			path: { engagementId: ENGAGEMENT_ID, meetingId: MEETING_ID },
			body: { accept: true },
		});
	});

	it("lets the volunteer back out of a meeting they accepted", async () => {
		renderPanel(interest({ response: "Accepted" }));

		await userEvent.click(
			screen.getByRole("button", { name: "Can't make it" }),
		);

		await waitFor(() =>
			expect(api.respondToMeetingAsVolunteer).toHaveBeenCalledWith({
				path: { engagementId: ENGAGEMENT_ID, meetingId: MEETING_ID },
				body: { accept: false },
			}),
		);
	});

	it("warns that suggesting another time leaves a group meeting", async () => {
		renderPanel(interest({ participantCount: 4 }));

		expect(
			screen.getByText(
				"Other interested people are invited to this meeting too and will meet you there.",
			),
		).toBeVisible();
		await userEvent.click(
			screen.getByRole("button", { name: "Suggest another time" }),
		);

		const dialog = await screen.findByRole("dialog");
		expect(
			within(dialog).getByText(
				"This takes you out of the group meeting and sends the organization your own proposal.",
			),
		).toBeVisible();
	});

	it("withdraws the volunteer's own proposal after asking", async () => {
		const { onChanged } = renderPanel(interest({ proposedBy: "Volunteer" }));

		expect(
			screen.queryByRole("button", { name: "Accept" }),
		).not.toBeInTheDocument();
		await userEvent.click(
			screen.getByRole("button", { name: "Withdraw proposal" }),
		);
		const dialog = await screen.findByRole("dialog");
		await userEvent.click(
			within(dialog).getByRole("button", { name: "Withdraw proposal" }),
		);

		await waitFor(() => expect(onChanged).toHaveBeenCalled());
		expect(api.cancelMeetingAsVolunteer).toHaveBeenCalledWith({
			path: { engagementId: ENGAGEMENT_ID, meetingId: MEETING_ID },
		});
	});

	it("offers a first proposal when there is no meeting yet", () => {
		renderPanel(interest(null));

		expect(
			screen.getByRole("button", { name: "Propose a meeting" }),
		).toBeVisible();
	});

	it("shows nothing for a withdrawn expression of interest", () => {
		renderPanel(interest(null, "Withdrawn"));

		expect(screen.queryByRole("button")).not.toBeInTheDocument();
	});
});
