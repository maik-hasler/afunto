import { describe, it, expect } from "vitest";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import LanguageSelector from "./LanguageSelector";
import { renderWithProviders } from "../../test/render";

const renderSelector = (lng: "de" | "en" = "en") =>
	renderWithProviders(<LanguageSelector />, { lng });

describe("LanguageSelector disclosure semantics", () => {
	it("announces itself as a disclosure, never as a listbox or menu", async () => {
		renderSelector();

		const trigger = screen.getByTestId("language-selector-trigger");
		expect(trigger).toHaveAttribute("aria-expanded", "false");
		expect(trigger).not.toHaveAttribute("aria-haspopup");

		await userEvent.click(trigger);

		expect(trigger).toHaveAttribute("aria-expanded", "true");

		const menu = screen.getByTestId("language-selector-menu");
		expect(menu.tagName).toBe("UL");
		expect(menu).toHaveAccessibleName("Switch language");
		expect(menu).not.toHaveAttribute("role");
		expect(menu.querySelectorAll('[role="option"]')).toHaveLength(0);
		expect(menu.querySelectorAll("[aria-selected]")).toHaveLength(0);
	});

	it("marks the active language with aria-current, not aria-selected", async () => {
		renderSelector("de");

		await userEvent.click(screen.getByTestId("language-selector-trigger"));

		const menu = screen.getByTestId("language-selector-menu");
		const current = within(menu).getByRole("button", { name: "Deutsch" });
		expect(current).toHaveAttribute("aria-current", "true");
		expect(
			within(menu).getByRole("button", { name: "English" }),
		).not.toHaveAttribute("aria-current");
	});

	it("leads its accessible name with the code it visibly shows", () => {
		renderSelector("de");

		const trigger = screen.getByTestId("language-selector-trigger");
		expect(trigger).toHaveTextContent("DE");
		const name = trigger.getAttribute("aria-label") ?? "";
		expect(name.startsWith("DE")).toBe(true);
		expect(name).toContain("Deutsch");
	});
});
