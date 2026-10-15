import { describe, it, expect } from "vitest";
import { screen } from "@testing-library/react";
import OpportunityCard, { type OpportunityCardItem } from "./OpportunityCard";
import { renderWithProviders } from "../test/render";

const base: OpportunityCardItem = {
	id: "11111111-1111-1111-1111-111111111111",
	titleDe: "Deutscher Titel",
	titleEn: "English Title",
	descriptionDe: "Beschreibung.",
	descriptionEn: "Description.",
	street: "Teststrasse",
	houseNumber: "1",
	zipCode: "24103",
	city: "Kiel",
	isRemote: false,
	occurrence: "OneTime",
	participationType: "ScheduledSlots",
	category: "Environment",
	totalMaxParticipants: 10,
	currentParticipantCount: 2,
	validUntil: undefined,
	nextTimeSlotStart: new Date(Date.UTC(2027, 0, 14, 9, 0)),
	organizationId: "22222222-2222-2222-2222-222222222222",
	organizationName: "Freiwillige Feuerwehr Kiel",
};

const renderCard = (item: OpportunityCardItem, keyword?: string) =>
	renderWithProviders(
		<OpportunityCard item={item} headingLevel={3} keyword={keyword} />,
	);

describe("OpportunityCard date and capacity contract", () => {
	it.each([
		[
			"a scheduled opportunity",
			{ ...base, nextTimeSlotStart: new Date(Date.UTC(2027, 0, 14, 9, 0)) },
			"start",
		],
		[
			"an interest-based one with a deadline",
			{
				...base,
				participationType: "IndividualContact",
				nextTimeSlotStart: undefined,
				validUntil: new Date(Date.UTC(2027, 0, 31)),
			},
			"deadline",
		],
		[
			"one with neither",
			{
				...base,
				participationType: "IndividualContact",
				nextTimeSlotStart: undefined,
				validUntil: undefined,
			},
			"flexible",
		],
	])("states a date kind and a capacity for %s", (_label, item, kind) => {
		renderCard(item as OpportunityCardItem);

		expect(screen.getByTestId("opportunity-date-line")).toHaveAttribute(
			"data-date-kind",
			kind,
		);
		expect(
			screen.getByTestId("opportunity-capacity").textContent?.trim(),
		).not.toBe("");
	});

	it("omits the capacity chip entirely for an interest-based opportunity, which has no capacity to count (#2228)", () => {
		renderCard({
			...base,
			participationType: "IndividualContact",
			nextTimeSlotStart: undefined,
			totalMaxParticipants: 0,
			currentParticipantCount: 0,
		});

		expect(screen.queryByTestId("opportunity-capacity")).toBeNull();
	});
});

describe("OpportunityCard sign-up mechanism chip (#2228)", () => {
	it("states how to sign up for a scheduled opportunity", () => {
		renderCard(base);

		expect(
			screen.getByTestId("opportunity-signup-mechanism"),
		).toHaveTextContent("Scheduled slots");
	});

	it("states the participation type for an interest-based one too, so the position always means the same thing", () => {
		renderCard({
			...base,

			participationType: "IndividualContact",
			nextTimeSlotStart: undefined,
			validUntil: new Date(Date.UTC(2027, 0, 31)),
			totalMaxParticipants: 0,
			currentParticipantCount: 0,
		});

		expect(
			screen.getByTestId("opportunity-signup-mechanism"),
		).toHaveTextContent("By expression of interest");
		expect(screen.queryByTestId("opportunity-capacity")).toBeNull();
	});
});

describe("OpportunityCard organization badge", () => {
	it("shows the uploaded logo instead of initials", () => {
		renderCard({
			...base,
			organizationLogoUrl: "https://storage.example.test/logos/kiel.png",
		});

		const link = screen.getByTestId("opportunity-org-link");
		const img = link.querySelector("img");
		expect(img).not.toBeNull();
		expect(img).toHaveAttribute(
			"src",
			"https://storage.example.test/logos/kiel.png",
		);
		expect(link.querySelector("[aria-hidden='true']")).toBeNull();
	});

	it("falls back to initials when there is no logo", () => {
		renderCard(base);

		const link = screen.getByTestId("opportunity-org-link");
		expect(link.querySelector("img")).toBeNull();
		expect(link.querySelector("[aria-hidden='true']")).toHaveTextContent("FK");
	});
});

describe("OpportunityCard cross-locale search match notice (#2242)", () => {
	it("explains a match that only exists in the hidden German title", () => {
		renderCard(base, "Deutscher");

		expect(
			screen.getByTestId("opportunity-cross-locale-match"),
		).toHaveTextContent("Deutscher Titel");
	});

	it("stays silent when the keyword already appears in the displayed English title", () => {
		renderCard(base, "English");

		expect(screen.queryByTestId("opportunity-cross-locale-match")).toBeNull();
	});

	it("stays silent when no keyword is active", () => {
		renderCard(base);

		expect(screen.queryByTestId("opportunity-cross-locale-match")).toBeNull();
	});

	it("stays silent when the keyword only matches the already-visible organization name", () => {
		renderCard(base, "Feuerwehr");

		expect(screen.queryByTestId("opportunity-cross-locale-match")).toBeNull();
	});
});

// One grid row, one card anatomy. A media band rendered per item - only where
// `bannerImageUrl` happened to be set - gave a row of cards two different
// shapes and stretched the banner-less ones to the tallest card's height,
// ~156px of it empty (#2329 F10). The band is now a property of the surface.
describe("OpportunityCard media band", () => {
	it("renders no band at all on a surface that does not ask for one", () => {
		const { container } = renderWithProviders(
			<OpportunityCard
				item={{ ...base, bannerImageUrl: "https://example.test/banner.jpg" }}
				headingLevel={3}
			/>,
		);

		// alt="" images have no img role, so this is queried by tag (see
		// frontend/AGENTS.md on that porting hazard).
		expect(container.querySelector("img[src*='banner']")).toBeNull();
	});

	it("renders the banner where the surface asks for one", () => {
		const { container } = renderWithProviders(
			<OpportunityCard
				item={{ ...base, bannerImageUrl: "https://example.test/banner.jpg" }}
				headingLevel={3}
				withMedia
			/>,
		);

		expect(container.querySelector("img[src*='banner']")).not.toBeNull();
	});

	it("keeps the band, and the card's height, when an item has no banner", () => {
		const { container } = renderWithProviders(
			<OpportunityCard item={base} headingLevel={3} withMedia />,
		);

		const band = container.querySelector(".h-32");
		expect(band).not.toBeNull();
		expect(container.querySelector("img[src*='banner']")).toBeNull();
		// The fallback is the category glyph on the brand gradient, not a
		// broken image or an empty grey box.
		expect(band?.querySelector("svg")).not.toBeNull();
	});
});

// Whether the footer wrapped used to depend purely on the org name's length,
// so two cards side by side in one grid rendered two different footers
// (#2329 F7). jsdom cannot measure the wrap, so the guard is the recipe: no
// wrapping, and the name is the element that gives up width.
describe("OpportunityCard footer row", () => {
	it("lays the organization and the location out on one unwrapping row", () => {
		renderCard(base);

		const orgLink = screen.getByTestId("opportunity-org-link");
		const footer = orgLink.parentElement;

		expect(footer?.className).not.toContain("flex-wrap");
		expect(orgLink.className).toContain("min-w-0");
		expect(screen.getByText("Freiwillige Feuerwehr Kiel").className).toContain(
			"truncate",
		);
		expect(screen.getByText("Kiel").parentElement?.className).toContain(
			"shrink-0",
		);
	});
});

describe("OpportunityCard viewer engagement chip (#2079)", () => {
	it("shows no chip when the viewer holds no engagement", () => {
		renderCard(base);

		expect(
			screen.queryByTestId("opportunity-viewer-engagement"),
		).not.toBeInTheDocument();
	});

	it.each([
		["ScheduledSlots", "Confirmed", "Signed up"],
		["ScheduledSlots", "Pending", "Sign-up pending"],
		["IndividualContact", "Confirmed", "Interest confirmed"],
		["IndividualContact", "Pending", "Interest sent"],
	] as const)(
		"words a %s opportunity's %s engagement as %s",
		(participationType, status, label) => {
			renderWithProviders(
				<OpportunityCard
					item={{ ...base, participationType }}
					viewerEngagementStatus={status}
				/>,
			);

			expect(
				screen.getByTestId("opportunity-viewer-engagement"),
			).toHaveTextContent(label);
		},
	);

	it("uses the 'eintragen' wording in German, not 'anmelden'", () => {
		renderWithProviders(
			<OpportunityCard item={base} viewerEngagementStatus="Pending" />,
			{ lng: "de" },
		);

		expect(
			screen.getByTestId("opportunity-viewer-engagement"),
		).toHaveTextContent("Eintragung ausstehend");
	});
});
