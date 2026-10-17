import { describe, it } from "vitest";
import MeetingDetails from "./MeetingDetails";
import Button from "./Button";
import { renderWithProviders } from "../test/render";
import { expectNoA11yViolations } from "../test/a11y";

const FUTURE = new Date().getUTCFullYear() + 1;

describe("MeetingDetails a11y", () => {
	it("has no violations for a linked group meeting with actions", async () => {
		renderWithProviders(
			<MeetingDetails
				meeting={{
					id: "11111111-1111-1111-1111-111111111111",
					startsAt: new Date(`${FUTURE}-03-10T17:00:00Z`),
					endsAt: new Date(`${FUTURE}-03-10T18:00:00Z`),
					format: "Online",
					location: "https://meet.example.org/abc",
					note: "Bring your ID",
					proposedBy: "Organization",
					response: "Pending",
					participantCount: 3,
				}}
				viewer="Volunteer"
			>
				<Button type="button" size="sm">
					Accept
				</Button>
			</MeetingDetails>,
			{ lng: "en" },
		);
		await expectNoA11yViolations();
	});
});
