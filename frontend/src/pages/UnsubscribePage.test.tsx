import { describe, it, expect } from "vitest";
import { screen } from "@testing-library/react";
import UnsubscribePage from "./UnsubscribePage";
import { renderWithProviders } from "../test/render";

describe("UnsubscribePage", () => {
	it("tells the visitor of an old email link that nothing is sent any more", () => {
		renderWithProviders(<UnsubscribePage />, {
			route: "/unsubscribe?type=EngagementConfirmed&token=abc",
		});

		expect(
			screen.getByRole("heading", {
				level: 1,
				name: "Afunto no longer sends these emails",
			}),
		).toBeInTheDocument();
		expect(screen.getByRole("link", { name: "Back to home" })).toHaveAttribute(
			"href",
			"/",
		);
	});

	it("speaks German when the interface is German", () => {
		renderWithProviders(<UnsubscribePage />, { lng: "de" });

		expect(
			screen.getByRole("heading", {
				level: 1,
				name: "Afunto verschickt diese E-Mails nicht mehr",
			}),
		).toBeInTheDocument();
	});
});
