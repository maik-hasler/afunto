import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, within } from "@testing-library/react";
import NotificationPreferencesSection from "./NotificationPreferencesSection";
import { renderWithProviders } from "../../test/render";

const { useMyOrganizations } = vi.hoisted(() => ({
	useMyOrganizations: vi.fn(),
}));

vi.mock("../../hooks/useMyOrganizations", () => ({ useMyOrganizations }));

const ORGANIZER_ROWS = [
	"New sign-ups for opportunities you organize",
	"Volunteer withdrawals from opportunities you organize",
];
const VOLUNTEER_ROWS = [
	"Your sign-up is confirmed",
	"Reminder before your opportunity starts",
];

const PREFERENCES = {
	notifyOnNewSignUp: true,
	notifyOnWithdrawal: true,
	notifyOnEngagementConfirmed: true,
	notifyOnEngagementReminder: true,
};

beforeEach(() => {
	useMyOrganizations.mockReturnValue({
		orgs: [],
		loading: false,
		failed: false,
	});
});

function renderSection(
	overrides: Partial<
		React.ComponentProps<typeof NotificationPreferencesSection>
	> = {},
) {
	return renderWithProviders(
		<NotificationPreferencesSection
			editing={false}
			preferences={PREFERENCES}
			loading={false}
			loadError={false}
			onToggle={vi.fn()}
			{...overrides}
		/>,
		{ auth: { isAuthenticated: true } },
	);
}

describe("notification preferences for an organization member", () => {
	beforeEach(() => {
		useMyOrganizations.mockReturnValue({
			orgs: [{ id: "11111111-1111-1111-1111-111111111111", name: "Org" }],
			loading: false,
			failed: false,
		});
	});

	it("still shows all four preferences", () => {
		renderSection();

		for (const label of [...ORGANIZER_ROWS, ...VOLUNTEER_ROWS]) {
			expect(screen.getByLabelText(label)).toBeInTheDocument();
		}
	});

	it("groups them by audience", () => {
		renderSection();

		const groupOf = (heading: HTMLElement) => {
			const block = heading.parentElement;
			expect(block).not.toBeNull();
			return block as HTMLElement;
		};
		const organizerGroup = groupOf(
			screen.getByRole("heading", { name: "As an organizer" }),
		);
		const volunteerGroup = groupOf(
			screen.getByRole("heading", { name: "As a volunteer" }),
		);

		for (const label of ORGANIZER_ROWS) {
			expect(within(organizerGroup).getByLabelText(label)).toBeInTheDocument();
		}
		for (const label of VOLUNTEER_ROWS) {
			expect(within(volunteerGroup).getByLabelText(label)).toBeInTheDocument();
		}
	});
});

describe("notification preferences for a volunteer without an organization", () => {
	it("hides the two organizer-only preferences", () => {
		renderSection();

		for (const label of VOLUNTEER_ROWS) {
			expect(screen.getByLabelText(label)).toBeInTheDocument();
		}
		for (const label of ORGANIZER_ROWS) {
			expect(screen.queryByLabelText(label)).toBeNull();
		}
	});

	it("drops the group headings, since only one audience is left", () => {
		renderSection();

		expect(screen.getByLabelText(VOLUNTEER_ROWS[0])).toBeInTheDocument();
		expect(
			screen.queryByRole("heading", { name: "As an organizer" }),
		).toBeNull();
		expect(
			screen.queryByRole("heading", { name: "As a volunteer" }),
		).toBeNull();
	});

	it("keeps the organizer rows when the organization lookup failed", () => {
		useMyOrganizations.mockReturnValue({
			orgs: [],
			loading: false,
			failed: true,
		});

		renderSection();

		for (const label of ORGANIZER_ROWS) {
			expect(screen.getByLabelText(label)).toBeInTheDocument();
		}
	});
});

describe("editing gate", () => {
	it("renders the checkboxes disabled outside of edit mode", () => {
		renderSection({ editing: false });

		expect(screen.getByLabelText(VOLUNTEER_ROWS[0])).toBeDisabled();
	});

	it("enables the checkboxes once the page enters edit mode", () => {
		renderSection({ editing: true });

		expect(screen.getByLabelText(VOLUNTEER_ROWS[0])).toBeEnabled();
	});

	it("shows a loading state instead of stale checkboxes while preferences load", () => {
		renderSection({ preferences: null, loading: true });

		expect(screen.getByRole("status")).toBeInTheDocument();
		expect(screen.queryByLabelText(VOLUNTEER_ROWS[0])).toBeNull();
	});

	it("shows an error banner when preferences failed to load", () => {
		renderSection({ preferences: null, loading: false, loadError: true });

		expect(
			screen.getByText("Could not load notification preferences."),
		).toBeInTheDocument();
	});
});
