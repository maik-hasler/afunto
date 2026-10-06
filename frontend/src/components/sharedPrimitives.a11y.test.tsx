import { describe, it, expect } from "vitest";
import { screen } from "@testing-library/react";
import Button from "./Button";
import Chip from "./Chip";
import DangerZonePanel from "./DangerZonePanel";
import EmptyState from "./EmptyState";
import ErrorBanner from "./ErrorBanner";
import FaqAccordion from "./FaqAccordion";
import LoadMoreButton from "./LoadMoreButton";
import LoadMoreError from "./LoadMoreError";
import ModalLoadingFallback from "./ModalLoadingFallback";
import Skeleton from "./Skeleton";
import SkipLink from "./SkipLink";
import Spinner from "./Spinner";
import SuccessBanner from "./SuccessBanner";
import WarningBanner from "./WarningBanner";
import { renderWithProviders } from "../test/render";
import { expectNoA11yViolations } from "../test/a11y";

describe("shared primitives a11y", () => {
	it("Button has no violations across every variant, as a button and as a link", async () => {
		const variants = [
			"primary",
			"secondary",
			"tertiary",
			"danger",
			"dangerOutline",
			"success",
			"outline",
			"onDark",
			"outlineOnDark",
		] as const;
		renderWithProviders(
			<div>
				{variants.map((variant) => (
					<Button key={variant} variant={variant} onClick={() => {}}>
						{variant}
					</Button>
				))}
				<Button to="/opportunities">Browse opportunities</Button>
				<Button disabled onClick={() => {}}>
					Unavailable
				</Button>
			</div>,
		);
		await expectNoA11yViolations();
	});

	it("Chip has no violations as static text, as a link, and with a remove control", async () => {
		renderWithProviders(
			<div>
				<Chip tone="brand">Environment</Chip>
				<Chip tone="danger" size="sm">
					Cancelled
				</Chip>
				<Chip to="/opportunities?tag=cleanup">cleanup</Chip>
				<Chip onRemove={() => {}} removeLabel="Remove the tag cleanup">
					cleanup
				</Chip>
			</div>,
		);
		await expectNoA11yViolations();
	});

	it("Chip's remove control carries an accessible name, not a bare icon", async () => {
		renderWithProviders(
			<Chip onRemove={() => {}} removeLabel="Remove the tag cleanup">
				cleanup
			</Chip>,
		);
		expect(
			screen.getByRole("button", { name: "Remove the tag cleanup" }),
		).toBeInTheDocument();
	});

	it("the three inline banners have no violations and keep their live-region roles", async () => {
		renderWithProviders(
			<div>
				<ErrorBanner message="The organization could not be saved." />
				<SuccessBanner message="Your changes were saved." />
				<WarningBanner message="You are close to this month's sign-up limit." />
			</div>,
		);
		await expectNoA11yViolations();

		expect(screen.getByRole("alert")).toHaveTextContent(
			"The organization could not be saved.",
		);
		expect(screen.getAllByRole("status")).toHaveLength(2);
	});

	it("EmptyState has no violations, plain and with either kind of call to action", async () => {
		renderWithProviders(
			<div>
				<EmptyState title="No sign-ups yet" message="Nothing here so far." />
				<EmptyState
					title="No opportunities yet"
					action={{ label: "Create an opportunity", onClick: () => {} }}
				/>
				<EmptyState
					compact
					title="No notifications"
					action={{ label: "Browse opportunities", to: "/opportunities" }}
				/>
			</div>,
		);
		await expectNoA11yViolations();
	});

	it("the load-more pair has no violations while idle, loading, and failed", async () => {
		renderWithProviders(
			<div>
				<LoadMoreButton
					loading={false}
					label="Load more"
					loadingLabel="Loading…"
					onClick={() => {}}
				/>
				<LoadMoreButton
					loading
					label="Load more"
					loadingLabel="Loading…"
					onClick={() => {}}
				/>
				<LoadMoreError
					message="More opportunities could not be loaded."
					retrying={false}
					onRetry={() => {}}
				/>
			</div>,
		);
		await expectNoA11yViolations();
	});

	it("the loading placeholders have no violations", async () => {
		renderWithProviders(
			<div>
				<Skeleton className="h-4 w-32" />
				<Spinner label="Loading page…" />
				<Spinner label="Loading page…" size="sm" />
			</div>,
		);
		await expectNoA11yViolations();
	});

	it("ModalLoadingFallback has no violations - the dialog chrome shown while a lazy modal loads", async () => {
		renderWithProviders(<ModalLoadingFallback onClose={() => {}} />);
		await expectNoA11yViolations();
		expect(screen.getByRole("dialog")).toHaveAccessibleName("Loading…");
	});

	it("DangerZonePanel has no violations, enabled and disabled", async () => {
		renderWithProviders(
			<div>
				<DangerZonePanel
					title="Delete this organization"
					description="This cannot be undone."
					actionLabel="Delete organization"
					onAction={() => {}}
				/>
				<DangerZonePanel
					title="Delete your account"
					description="This cannot be undone."
					actionLabel="Delete account"
					disabled
					onAction={() => {}}
				/>
			</div>,
		);
		await expectNoA11yViolations();
	});

	it("FaqAccordion has no violations collapsed or expanded", async () => {
		const items = [
			{
				q: "Is Afunto free?",
				a: "Yes, for volunteers and organizations.",
			},
			{ q: "Do I need an account?", a: "Only to sign up for an opportunity." },
		];
		const { container } = renderWithProviders(<FaqAccordion items={items} />);
		await expectNoA11yViolations();

		for (const details of container.querySelectorAll("details")) {
			details.open = true;
		}
		await expectNoA11yViolations();
	});

	it("SkipLink is the bypass mechanism both layouts rely on", async () => {
		renderWithProviders(<SkipLink />);
		const link = screen.getByRole("link", { name: "Skip to content" });
		expect(link).toHaveAttribute("href", "#main-content");
		await expectNoA11yViolations();
	});
});
