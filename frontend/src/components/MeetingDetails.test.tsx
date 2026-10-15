import { describe, it, expect } from "vitest";
import { screen } from "@testing-library/react";
import MeetingDetails from "./MeetingDetails";
import { renderWithProviders } from "../test/render";
import type { EngagementMeetingSummary } from "../client";

const FUTURE = new Date().getUTCFullYear() + 1;

function meeting(
	overrides: Partial<EngagementMeetingSummary> = {},
): EngagementMeetingSummary {
	return {
		id: "11111111-1111-1111-1111-111111111111",
		startsAt: new Date(`${FUTURE}-03-10T17:00:00Z`),
		endsAt: new Date(`${FUTURE}-03-10T18:00:00Z`),
		format: "InPerson",
		location: "Vereinsheim, Hauptstraße 1",
		note: null,
		proposedBy: "Organization",
		response: "Pending",
		participantCount: 1,
		...overrides,
	};
}

describe("MeetingDetails", () => {
	it("tells the volunteer it is their turn on the organization's proposal", () => {
		renderWithProviders(
			<MeetingDetails meeting={meeting()} viewer="Volunteer" />,
			{ lng: "en" },
		);

		expect(screen.getByText("Waiting for your answer")).toBeInTheDocument();
		expect(screen.getByText("Vereinsheim, Hauptstraße 1")).toBeInTheDocument();
		expect(screen.queryByRole("link")).not.toBeInTheDocument();
	});

	it("tells the volunteer who it waits for on their own proposal", () => {
		renderWithProviders(
			<MeetingDetails
				meeting={meeting({ proposedBy: "Volunteer" })}
				viewer="Volunteer"
			/>,
			{ lng: "en" },
		);

		expect(
			screen.getByText("Waiting for the organization"),
		).toBeInTheDocument();
	});

	it("links an online meeting's https link, opening it in a new tab without an opener", () => {
		renderWithProviders(
			<MeetingDetails
				meeting={meeting({
					format: "Online",
					location: "https://meet.example.org/abc",
				})}
				viewer="Organization"
			/>,
			{ lng: "en" },
		);

		const link = screen.getByRole("link", {
			name: "https://meet.example.org/abc",
		});
		expect(link).toHaveAttribute("href", "https://meet.example.org/abc");
		expect(link).toHaveAttribute("rel", "noopener noreferrer");
	});

	it("never links a meeting in person, even when its place looks like a URL", () => {
		renderWithProviders(
			<MeetingDetails
				meeting={meeting({ location: "https://meet.example.org/abc" })}
				viewer="Organization"
			/>,
			{ lng: "en" },
		);

		expect(screen.queryByRole("link")).not.toBeInTheDocument();
		expect(
			screen.getByText("https://meet.example.org/abc"),
		).toBeInTheDocument();
	});

	it("says how many were invited to a group meeting, never who", () => {
		renderWithProviders(
			<MeetingDetails
				meeting={meeting({ participantCount: 5 })}
				viewer="Volunteer"
			/>,
			{ lng: "en" },
		);

		expect(
			screen.getByText("Group meeting, 5 people invited"),
		).toBeInTheDocument();
	});
});
