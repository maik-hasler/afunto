import { describe, it, expect } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import HelpPage from "./HelpPage";
import { renderWithProviders } from "../test/render";

describe("HelpPage", () => {
	it("points to the Contact page for anything not covered by the FAQ, with no reply SLA promised", () => {
		renderWithProviders(<HelpPage />);

		expect(screen.getByRole("link", { name: "Contact page" })).toHaveAttribute(
			"href",
			"/contact",
		);
		expect(screen.queryByText(/24 hours/)).toBeNull();
		expect(screen.queryByText(/maikhslr/)).toBeNull();
	});

	it("keeps a split German compound word inside one link label", () => {
		renderWithProviders(<HelpPage />, { lng: "de" });

		expect(
			screen.getByRole("link", { name: "Einsatzseite" }),
		).toBeInTheDocument();
		expect(screen.queryByRole("link", { name: "Einsatz-" })).toBeNull();
	});

	it("answers the four General questions the landing page's FAQ shows", async () => {
		const { container } = renderWithProviders(<HelpPage />);

		expect(
			screen.getByRole("heading", { name: "General" }),
		).toBeInTheDocument();
		expect(
			screen.getByText("Does using Afunto cost anything?"),
		).toBeInTheDocument();

		const details = container.querySelectorAll("details");
		expect(details.length).toBeGreaterThanOrEqual(4);
		await userEvent.click(screen.getByText("Does using Afunto cost anything?"));
		expect(details[0].textContent?.length ?? 0).toBeGreaterThan(
			"Does using Afunto cost anything?".length,
		);
	});
});
