import { describe, it } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import MeetingFormModal from "./MeetingFormModal";
import { renderWithProviders } from "../test/render";
import { expectNoA11yViolations } from "../test/a11y";

function renderForm() {
	renderWithProviders(
		<MeetingFormModal
			title="Propose a meeting"
			intro="Vera gets a notification."
			initialLocation="Clubhouse"
			onSubmit={() => Promise.resolve()}
			onClose={() => {}}
		/>,
		{ lng: "en" },
	);
}

describe("MeetingFormModal a11y", () => {
	it("has no violations when empty", async () => {
		renderForm();
		await expectNoA11yViolations();
	});

	it("has no violations while showing a times error", async () => {
		renderForm();
		await userEvent
			.setup()
			.click(screen.getByRole("button", { name: "Send proposal" }));
		await screen.findByRole("alert");
		await expectNoA11yViolations();
	});
});
