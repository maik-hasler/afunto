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
			initial={{ format: "InPerson", location: "Clubhouse" }}
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

	it("has no violations with the online link field showing its error", async () => {
		renderForm();
		const user = userEvent.setup();
		await user.click(screen.getByRole("radio", { name: "Online" }));
		await user.type(screen.getByLabelText(/Video call link/), "http://x.org");
		// Make the times valid so the link is what gets refused.
		await user.type(screen.getByLabelText(/^From/), "1000");
		await user.click(screen.getByRole("button", { name: "Send proposal" }));
		await screen.findByRole("alert");
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
