import { lazy, Suspense, useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import { useParams, Link, useLocation, useSearchParams } from "react-router";
import { useAuth } from "react-oidc-context";
import { useTranslation } from "react-i18next";
import type { TFunction } from "i18next";
import type {
	CurrentUserEngagementInfo,
	PublicOpportunitySummaryDto,
	PublicOrganizationProfileResponse,
	TimeSlotDetail,
	VolunteerOpportunityDetails,
} from "../client";
import { useApiClient } from "../hooks/useApiClient";
import {
	useInvalidateMyEngagementStates,
	useMyEngagementStates,
} from "../hooks/useMyEngagementStates";
import { useOnlineStatus } from "../hooks/useOnlineStatus";
import {
	computeSpotsLeft,
	findNextTimeSlot,
	formatDate,
	formatDateTime,
	formatDateTimeRange,
	formatOccurrence,
	formatParticipationType,
	formatPostedAgo,
	isSlotFull,
	isTimeSlotEnded,
	pickLocalizedText,
} from "../lib/format";
import {
	FEW_SPOTS_THRESHOLD,
	getCapacityFromTimeSlots,
	type OpportunityCapacity,
	type TimeSlotCapacityInput,
} from "../lib/opportunityCapacity";
import { SIGN_UP_INTEREST, SIGN_UP_PARAM } from "../lib/signUpDeepLink";
import Chip from "../components/Chip";
import PageSectionHeading from "../components/PageSectionHeading";
import OpportunityActionPanel, {
	type PanelFact,
	type PanelStatusTone,
} from "../components/OpportunityActionPanel";
import SignUpModal from "../components/SignUpModal";
import ReportContentModal, {
	type ReportReason,
} from "../components/ReportContentModal";
import ConfirmDialog from "../components/ConfirmDialog";
import Button from "../components/Button";
import Skeleton from "../components/Skeleton";
import LoadMoreError from "../components/LoadMoreError";
import DetailLoadFailure from "../components/DetailLoadFailure";
import ModalLoadingFallback from "../components/ModalLoadingFallback";
import PageHeaderBand from "../components/PageHeaderBand";
import OpportunityCard, {
	type OpportunityCardItem,
	viewerEngagementChip,
} from "../components/OpportunityCard";
import RouteState from "../components/RouteState";
import WarningBanner from "../components/WarningBanner";
import { usePageDescription } from "../hooks/usePageDescription";
import { usePageTitle } from "../hooks/usePageTitle";
import { dispatchToast } from "../lib/toastBus";
import {
	classifyLoadFailure,
	getApiErrorMessage,
	type LoadFailureKind,
} from "../lib/apiError";
import { signinLocaleArgs } from "../lib/authLocale";
import { reportIntentSigninArgs } from "../lib/reportIntent";
import { usePendingReportIntent } from "../hooks/usePendingReportIntent";
import { cardClass, cardSubtleClass } from "../lib/surfaceClasses";
import { inlineLinkClass } from "../lib/linkClasses";
import {
	ArrowTopRightOnSquareIcon,
	BuildingOfficeIcon,
	CalendarIcon,
	CheckIconSolid,
	ChevronDownIcon,
	ChevronRightIcon,
	EnvelopeIcon,
	FlagIcon,
	GlobeIcon,
	MapPinIcon,
	PhoneIcon,
	UserGroupIcon,
} from "../components/icons";

const SingleMarkerMap = lazy(() => import("../components/SingleMarkerMap"));

/**
 * The page's rhythm. Every block used to carry the same 12px uppercase label
 * and the same mb-6, so "About this organization" arrived with exactly the
 * weight of the schedule a visitor came for, and a long page read as one
 * undifferentiated scroll (#2330). A short brand rule plus a display-face
 * heading is what separates the five things this page has to say.
 */
function DetailSection({
	title,
	children,
	"data-testid": testId,
}: {
	/** Omitted only where the content names itself, e.g. a lone disclosure. */
	title?: string;
	children: ReactNode;
	"data-testid"?: string;
}) {
	return (
		<section data-testid={testId} className="pt-2 first:pt-0">
			{title && (
				<>
					{/* A short brand rule, not a full-width one: a hairline across the
					column is the generic answer and reads as a horizontal rule between
					two paragraphs, where this marks where a section starts. It repeats
					down the page as one system with the display-face heading under
					it. */}
					<span
						aria-hidden="true"
						className="mb-4 block h-0.5 w-10 bg-brand-600"
					/>
					<PageSectionHeading>{title}</PageSectionHeading>
				</>
			)}
			{children}
		</section>
	);
}

const CreateVolunteerOpportunityModal = lazy(
	() => import("../components/CreateVolunteerOpportunityModal"),
);

const MAX_META_DESCRIPTION_LENGTH = 160;

/**
 * Baseline-aligned and wrapping. A long capacity label used to squeeze the
 * date onto two lines and then centre itself between them, so the rows of one
 * list came out at different heights with nothing lining up (#2330).
 */
const SLOT_ROW_CLASS =
	"flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1";

const HERO_LEAD_MAX_LENGTH = 220;

/**
 * The hero band is a lead, not the body copy. It renders on one line-height
 * with newlines collapsed, so a description written in paragraphs and bullets
 * arrived as a single run-on block that could fill the whole first screen and
 * push the sign-up CTA out of sight. Summarise it for the band; the stored
 * text keeps its line breaks in its own section further down (#2330).
 */
function toHeroLead(text: string): string {
	const collapsed = (text.trim().split(/\n\s*\n/)[0] ?? "")
		.replace(/\s+/g, " ")
		.trim();
	if (collapsed.length <= HERO_LEAD_MAX_LENGTH) return collapsed;

	const cut = collapsed.slice(0, HERO_LEAD_MAX_LENGTH);
	const lastSpace = cut.lastIndexOf(" ");
	return `${(lastSpace > HERO_LEAD_MAX_LENGTH / 2 ? cut.slice(0, lastSpace) : cut).trimEnd()}\u2026`;
}

function toMetaDescription(text: string): string {
	const trimmed = text.trim();
	if (trimmed.length <= MAX_META_DESCRIPTION_LENGTH) return trimmed;
	return `${trimmed.slice(0, MAX_META_DESCRIPTION_LENGTH - 1).trimEnd()}…`;
}

/**
 * The status strip of the action panel. `tone` says whether a visitor can still
 * take part - it drives the strip's colour, so it is a panel tone rather than a
 * text colour: the label used to sit as bare coloured text in a row of chips,
 * where "Only 2 spots left!" carried no more weight than a tag (#2330).
 */
function describeCapacity(
	capacity: OpportunityCapacity,
	t: TFunction,
): { label: string; tone: PanelStatusTone; note?: string } {
	switch (capacity.kind) {
		case "unlimited":
			return { label: t("opportunities.unlimitedSpots"), tone: "open" };
		case "notApplicable":
			return capacity.reason === "interest"
				? {
						label: t("opportunities.byInterest"),
						tone: "open",
						...(capacity.booked > 0
							? {
									note: t("opportunities.participantsJoined", {
										count: capacity.booked,
									}),
								}
							: {}),
					}
				: { label: t("opportunities.noOpenSpots"), tone: "closed" };
		case "capped":
			if (capacity.isFull) {
				return { label: t("opportunities.full"), tone: "closed" };
			}
			return capacity.spotsLeft <= FEW_SPOTS_THRESHOLD
				? {
						label: t("opportunities.fewSpotsLeft", {
							count: capacity.spotsLeft,
						}),
						tone: "urgent",
					}
				: {
						label: t("opportunities.spotsLeft", { count: capacity.spotsLeft }),
						tone: "open",
					};
	}
}

/**
 * The generated DTO widens every integer to `number | string` and spells an
 * absent one `null`; the capacity helpers take plain numbers.
 */
function toCapacityInput(slot: TimeSlotDetail): TimeSlotCapacityInput {
	return {
		maxParticipants:
			slot.maxParticipants == null ? null : Number(slot.maxParticipants),
		bookedCount: Number(slot.bookedCount),
	};
}

/** The same widening for the card's item shape, which reads `undefined`. */
function toCardItem(opp: PublicOpportunitySummaryDto): OpportunityCardItem {
	return {
		...opp,
		titleEn: opp.titleEn ?? undefined,
		descriptionDe: opp.descriptionDe ?? undefined,
		descriptionEn: opp.descriptionEn ?? undefined,
		street: opp.street ?? undefined,
		houseNumber: opp.houseNumber ?? undefined,
		zipCode: opp.zipCode ?? undefined,
		city: opp.city ?? undefined,
		category: opp.category ?? undefined,
		validUntil: opp.validUntil ?? undefined,
		nextTimeSlotStart: opp.nextTimeSlotStart ?? undefined,
		totalMaxParticipants:
			opp.totalMaxParticipants == null
				? undefined
				: Number(opp.totalMaxParticipants),
		currentParticipantCount: Number(opp.currentParticipantCount),
	};
}

function slotCapacityLabel(slot: TimeSlotCapacityInput, t: TFunction): string {
	const spotsLeft = computeSpotsLeft(slot.maxParticipants, slot.bookedCount);
	if (spotsLeft === null) return t("opportunities.unlimitedSpots");
	return isSlotFull(slot.maxParticipants, slot.bookedCount)
		? t("opportunities.full")
		: t("opportunities.spotsLeft", { count: spotsLeft });
}

function describeWhenFact(
	opportunity: VolunteerOpportunityDetails,
	t: TFunction,
	lng: string,
): string {
	if (opportunity.participationType === "IndividualContact") {
		return opportunity.validUntil
			? t("opportunities.applyBy", {
					date: formatDate(opportunity.validUntil as unknown as string, lng),
				})
			: t("opportunities.flexibleDate");
	}

	const nextSlot = findNextTimeSlot(opportunity.timeSlots);
	return nextSlot
		? formatDateTime(nextSlot.startDateTime as unknown as string, lng)
		: t("opportunities.flexibleDate");
}

function describeHowFact(
	opportunity: VolunteerOpportunityDetails,
	upcomingTimeSlotCount: number,
	t: TFunction,
): string {
	return opportunity.participationType === "ScheduledSlots"
		? t("opportunities.slotCount", { count: upcomingTimeSlotCount })
		: formatParticipationType(opportunity.participationType, t);
}

export default function VolunteerOpportunityDetailPage() {
	const { opportunityId } = useParams<{ opportunityId: string }>();
	const auth = useAuth();
	const location = useLocation();
	const api = useApiClient();
	const { t, i18n } = useTranslation();
	const engagementStates = useMyEngagementStates();
	const invalidateMyEngagementStates = useInvalidateMyEngagementStates();

	const [opportunity, setOpportunity] =
		useState<VolunteerOpportunityDetails | null>(null);
	usePageTitle(
		opportunity &&
			pickLocalizedText(opportunity.titleDe, opportunity.titleEn, i18n.language)
				.text,
	);
	const opportunityDescription = opportunity
		? pickLocalizedText(
				opportunity.descriptionDe,
				opportunity.descriptionEn,
				i18n.language,
			)?.text
		: undefined;
	usePageDescription(
		opportunityDescription ? toMetaDescription(opportunityDescription) : null,
	);
	const [loading, setLoading] = useState(true);
	const [error, setError] = useState<string | null>(null);

	const [failure, setFailure] = useState<LoadFailureKind | null>(null);
	const online = useOnlineStatus();
	// An error captured while offline stays an offline state until the
	// connection is back, whatever the browser reported at the time.
	const failureKind =
		failure && failure !== "notFound" && !online ? "offline" : failure;
	const [showSignUp, setShowSignUp] = useState(false);

	const [preselectedSlotId, setPreselectedSlotId] = useState<
		string | undefined
	>(undefined);

	// "Sign up again" on a withdrawn engagement links here carrying the slot it
	// was for, so the volunteer doesn't land on a bare page with no trace of
	// their withdrawn sign-up (#2323). Consume it once and drop it from the URL
	// so a reload or a Back doesn't reopen the dialog.
	const [searchParams, setSearchParams] = useSearchParams();
	const requestedSignUp = searchParams.get(SIGN_UP_PARAM);
	useEffect(() => {
		if (!requestedSignUp) return;
		setPreselectedSlotId(
			requestedSignUp === SIGN_UP_INTEREST ? undefined : requestedSignUp,
		);
		setShowSignUp(true);
		setSearchParams(
			(prev) => {
				const next = new URLSearchParams(prev);
				next.delete(SIGN_UP_PARAM);
				return next;
			},
			{ replace: true },
		);
	}, [requestedSignUp, setSearchParams]);
	const [showReport, setShowReport] = useState(false);
	const [withdrawTarget, setWithdrawTarget] =
		useState<CurrentUserEngagementInfo | null>(null);
	const [withdrawing, setWithdrawing] = useState(false);
	const [withdrawError, setWithdrawError] = useState<string | null>(null);
	const [showEditModal, setShowEditModal] = useState(false);
	const [publishing, setPublishing] = useState(false);

	const withdrawLimitWarningRef = useRef<HTMLParagraphElement>(null);
	const withdrawLimitWarningActive =
		withdrawTarget !== null && withdrawTarget.remainingReactivations === 1;
	useEffect(() => {
		if (withdrawLimitWarningActive) withdrawLimitWarningRef.current?.focus();
	}, [withdrawTarget, withdrawLimitWarningActive]);

	const isAuthenticated = auth.isAuthenticated;
	const roles = (
		Array.isArray(auth.user?.profile?.roles) ? auth.user?.profile?.roles : []
	) as string[];

	const isOrganisator = isAuthenticated && roles.includes("organisator");
	const [userOrgIds, setUserOrgIds] = useState<string[]>([]);

	useEffect(() => {
		if (!isOrganisator) return;
		api
			.getOrganizations({})
			.then((orgs) => setUserOrgIds(orgs.map((o) => o.id)))
			.catch(() => {});
		// eslint-disable-next-line react-hooks/exhaustive-deps
	}, [isOrganisator]);

	// Resumes a Report click made before the Keycloak round trip, which used to be dropped at
	// the redirect (#2326). Waits for the opportunity itself: the report control is not offered
	// on your own organization's opportunity, so neither is the resumed modal.
	const pendingReportTargetId = usePendingReportIntent();
	const viewerOwnsOpportunity =
		isOrganisator &&
		opportunity !== null &&
		userOrgIds.includes(opportunity.organizationId);
	useEffect(() => {
		if (
			isAuthenticated &&
			opportunity !== null &&
			!viewerOwnsOpportunity &&
			pendingReportTargetId === opportunity.id
		) {
			setShowReport(true);
		}
	}, [
		isAuthenticated,
		opportunity,
		viewerOwnsOpportunity,
		pendingReportTargetId,
	]);

	const [orgProfile, setOrgProfile] =
		useState<PublicOrganizationProfileResponse | null>(null);
	const [orgProfileError, setOrgProfileError] = useState<string | null>(null);
	const [retryingOrgProfile, setRetryingOrgProfile] = useState(false);

	function loadOrgProfile() {
		if (!opportunity?.organizationId) return Promise.resolve();
		setOrgProfileError(null);
		return api
			.getPublicOrganizationProfile({
				path: { organizationId: opportunity.organizationId },
			})
			.then(setOrgProfile)
			.catch((err) => {
				setOrgProfileError(
					getApiErrorMessage(
						err,
						t("opportunities.aboutOrganizationLoadError"),
					),
				);
			});
	}

	useEffect(() => {
		loadOrgProfile();
		// eslint-disable-next-line react-hooks/exhaustive-deps
	}, [opportunity?.organizationId]);

	function retryLoadOrgProfile() {
		setRetryingOrgProfile(true);
		loadOrgProfile().finally(() => setRetryingOrgProfile(false));
	}

	const latestRequestRef = useRef(0);

	useEffect(() => {
		if (!opportunityId) return;

		setOrgProfile(null);
		setOrgProfileError(null);
		load();

		// eslint-disable-next-line react-hooks/exhaustive-deps
	}, [opportunityId, isAuthenticated]);

	function load() {
		if (!opportunityId) return;
		const requestId = ++latestRequestRef.current;
		setLoading(true);
		setError(null);
		setFailure(null);
		api
			.getVolunteerOpportunityDetails({ path: { opportunityId } })
			.then((details) => {
				if (requestId !== latestRequestRef.current) return;
				setOpportunity(details);
			})
			.catch((err) => {
				if (requestId !== latestRequestRef.current) return;
				setError(getApiErrorMessage(err, t("error.serverError")));
				setFailure(classifyLoadFailure(err, online));
			})
			.finally(() => {
				if (requestId !== latestRequestRef.current) return;
				setLoading(false);
			});
	}

	async function handleReportSubmit(reason: ReportReason, details: string) {
		if (!opportunity) return;
		await api.reportVolunteerOpportunity({
			path: { opportunityId: opportunity.id },
			body: {
				reason,
				details: details || null,
			},
		});
		dispatchToast("success", t("report.submitSuccess"));
	}

	async function handlePublish() {
		if (!opportunity) return;
		setPublishing(true);
		try {
			await api.publishVolunteerOpportunity({
				path: { opportunityId: opportunity.id },
			});
			dispatchToast("success", t("opportunities.publishSuccess"));
			load();
		} catch (err) {
			dispatchToast("error", getApiErrorMessage(err, t("error.serverError")));
		} finally {
			setPublishing(false);
		}
	}

	async function handleWithdrawConfirm() {
		if (!withdrawTarget) return;
		setWithdrawing(true);
		setWithdrawError(null);
		try {
			await api.withdrawEngagement({
				path: { engagementId: withdrawTarget.id },
			});
			dispatchToast(
				"success",
				t(
					isInterestBased
						? "myEngagements.withdrawSuccessInterest"
						: "myEngagements.withdrawSuccess",
				),
			);
			setWithdrawTarget(null);
			load();
			void invalidateMyEngagementStates();
		} catch (err) {
			setWithdrawError(
				getApiErrorMessage(
					err,
					t(
						isInterestBased
							? "myEngagements.withdrawErrorInterest"
							: "myEngagements.withdrawError",
					),
				),
			);
		} finally {
			setWithdrawing(false);
		}
	}

	if (loading)
		return (
			<div className="mx-auto max-w-4xl" role="status">
				<span className="sr-only">{t("opportunities.loading")}</span>
				<Skeleton className="mb-6 h-56 w-full sm:h-72" />

				<div className="max-w-2xl">
					<div className="mb-3 flex items-center justify-between gap-3">
						<Skeleton className="h-6 w-32 rounded-full" />
						<Skeleton className="h-8 w-20 rounded-lg" />
					</div>
					<Skeleton className="mb-3 h-8 w-3/4" />
					<Skeleton className="mb-2 h-4 w-full" />
					<Skeleton className="mb-6 h-4 w-2/3" />
					<Skeleton className="h-32 w-full" />
				</div>
			</div>
		);
	if (failureKind || !opportunity)
		return (
			<DetailLoadFailure
				kind={failureKind ?? "notFound"}
				notFoundTitle={t("opportunities.notFoundTitle")}
				notFoundMessage={t("opportunities.notFoundMessage")}
				errorMessage={error ?? t("error.serverError")}
				offlineMessage={t("opportunities.offlineDetail")}
				onRetry={load}
				action={{ label: t("nav.findOpportunities"), to: "/opportunities" }}
				data-testid="opportunity-load-failure"
			/>
		);

	const isOwner = viewerOwnsOpportunity;
	const isDraft = opportunity.status === "Draft";

	const upcomingTimeSlots = opportunity.timeSlots.filter(
		(ts) => !isTimeSlotEnded(ts),
	);
	const pastTimeSlots = opportunity.timeSlots.filter((ts) =>
		isTimeSlotEnded(ts),
	);

	// The API sends an absent time slot as JSON null, not an omitted field, so
	// currentUserEngagements[].timeSlotId is `null` at runtime for an
	// IndividualContact engagement even though the generated type claims
	// `string | undefined` - truthiness catches both.
	const engagementsBySlot = new Map(
		opportunity.currentUserEngagements
			.filter((e) => e.timeSlotId)
			.map((e) => [e.timeSlotId as string, e]),
	);
	const individualContactEngagement = opportunity.currentUserEngagements.find(
		(e) => !e.timeSlotId,
	);

	const canInteract = isAuthenticated && !isOwner && !isDraft;
	const canSignUpForMore =
		opportunity.participationType === "IndividualContact"
			? !individualContactEngagement
			: upcomingTimeSlots.some((ts) => !engagementsBySlot.has(ts.id));

	// Only the slots a visitor can still book: an ended slot's seats are gone, so
	// counting them advertised spots that could never be taken (#2318).
	const capacity = getCapacityFromTimeSlots(
		upcomingTimeSlots.map(toCapacityInput),
		Number(opportunity.currentParticipantCount),
		opportunity.participationType,
	);
	const isFull = capacity.kind === "capped" && capacity.isFull;
	/** No seat is ever released by withdrawing this - it's an expression of interest, not a sign-up (#2228). */
	const isInterestBased =
		capacity.kind === "notApplicable" && capacity.reason === "interest";
	const capacityStatus = describeCapacity(capacity, t);

	const address = opportunity.isRemote
		? ""
		: `${opportunity.street} ${opportunity.houseNumber}, ${opportunity.zipCode} ${opportunity.city}`;
	// "At a glance" is a summary, so it names the place; the street address is
	// stated once, in the location block that also offers the route (#2330).
	const locationSummary = opportunity.isRemote
		? t("opportunities.remote")
		: opportunity.city;

	const hasMap = opportunity.latitude != null && opportunity.longitude != null;

	const directionsUrl =
		opportunity.latitude != null && opportunity.longitude != null
			? `https://www.google.com/maps/dir/?api=1&destination=${opportunity.latitude},${opportunity.longitude}`
			: `https://www.google.com/maps/dir/?api=1&destination=${encodeURIComponent(address)}`;

	// Publishing a draft is the owner's alone; a visitor's controls live in the
	// action panel (calendar) and the page footer (report), so for everyone else
	// this row is gone rather than empty.
	const hasOwnerToolbar = isOwner && isDraft;

	const postedOnRelative = formatPostedAgo(
		opportunity.createdOn as unknown as string,
		t,
	);
	const postedOnAbsolute = formatDateTime(
		opportunity.createdOn as unknown as string,
		i18n.language,
	);

	const otherOrgOpportunities =
		orgProfile?.openOpportunities
			.filter((opp) => opp.id !== opportunity.id)
			.slice(0, 3) ?? [];

	const showSignUpCta = canInteract && canSignUpForMore;
	const showApplicationStatus =
		canInteract && opportunity.currentUserEngagements.length > 0;
	const showLoginPrompt = !isAuthenticated && !isDraft;

	const showOwnerNotice = isOwner && !isDraft;

	// The three facts that decide whether someone can take part, stated once,
	// inside the panel that acts on them. They used to sit in a three-column
	// band directly above the schedule and location sections that repeat them
	// in full (#2330).
	const panelFacts: PanelFact[] = [
		{
			key: "when",
			icon: <CalendarIcon className="h-4 w-4" />,
			label: t("opportunities.factWhen"),
			value: describeWhenFact(opportunity, t, i18n.language),
			"data-testid": "opportunity-detail-when",
		},
		{
			key: "how",
			icon: <UserGroupIcon className="h-4 w-4" />,
			label: t("opportunities.factFormat"),
			value: describeHowFact(opportunity, upcomingTimeSlots.length, t),
			"data-testid": "opportunity-detail-how",
		},
		{
			key: "where",
			icon: opportunity.isRemote ? (
				<GlobeIcon className="h-4 w-4" />
			) : (
				<MapPinIcon className="h-4 w-4" />
			),
			label: t("opportunities.factWhere"),
			value: locationSummary,
			"data-testid": "opportunity-detail-where",
		},
	];

	function renderActionColumn(opp: VolunteerOpportunityDetails) {
		return (
			<>
				{showOwnerNotice && (
					<RouteState
						inline
						variant="forbidden"
						title={t("opportunities.ownOpportunityNoticeTitle")}
						message={t("opportunities.ownOpportunityNoticeMessage")}
						action={{
							label: t("engagementManagement.title"),
							to: `/app/${opp.organizationId}/dashboard/opportunities/${opp.id}/engagements`,
						}}
						data-testid="opportunity-owner-notice"
					/>
				)}

				{showApplicationStatus && (
					<div
						data-testid="application-status"
						className={`${cardClass} sm:p-5`}
					>
						{/* One heading for the card, then one dated block per sign-up:
						repeating an identical "Your sign-up" label above every block
						left the withdraw buttons floating between two rows nobody
						could tell apart (#2323). */}
						<p className="mb-2 text-xs text-gray-500">
							{isInterestBased
								? t("opportunities.yourInterest")
								: opp.currentUserEngagements.length > 1
									? t("opportunities.yourApplications")
									: t("opportunities.yourApplication")}
						</p>
						<ul className="divide-y divide-gray-200">
							{opp.currentUserEngagements.map((engagement) => {
								const engagementTimeSlot = opp.timeSlots.find(
									(ts) => ts.id === engagement.timeSlotId,
								);
								// Every block's button is named "Withdraw"; the date is
								// what tells them apart, so point at it rather than
								// folding it into the name (#2323).
								const slotHeadingId = `engagement-slot-${engagement.id}`;
								return (
									<li
										key={engagement.id}
										className="flex items-start justify-between gap-4 py-3 first:pt-0 last:pb-0"
									>
										<div>
											{engagementTimeSlot && (
												<p
													id={slotHeadingId}
													className="mb-1.5 flex items-center gap-1.5 text-xs font-semibold text-gray-900"
												>
													<CalendarIcon className="h-3.5 w-3.5 shrink-0" />
													<span>
														{t("myEngagements.scheduledFor", {
															range: formatDateTimeRange(
																engagementTimeSlot.startDateTime as unknown as string,
																engagementTimeSlot.endDateTime as unknown as string,
																i18n.language,
															),
														})}
													</span>
												</p>
											)}
											<Chip
												tone={
													engagement.status === "Confirmed"
														? "success"
														: "warning"
												}
												size="sm"
											>
												{t(`myEngagements.status.${engagement.status}`)}
											</Chip>

											{engagement.status === "Pending" && (
												<p className="mt-1.5 text-xs text-gray-600">
													{t(
														isInterestBased
															? "myEngagements.pendingExplanationInterest"
															: "myEngagements.pendingExplanation",
													)}
												</p>
											)}
											{engagement.isCheckedIn && (
												<Chip tone="success" size="sm" className="mt-2">
													<CheckIconSolid className="h-3 w-3" />
													{t("checkIn.checkedInLabel")}
												</Chip>
											)}
										</div>

										{!engagement.isCheckedIn && (
											<Button
												type="button"
												variant="dangerOutline"
												size="sm"
												className="shrink-0"
												aria-describedby={
													engagementTimeSlot ? slotHeadingId : undefined
												}
												onClick={() => setWithdrawTarget(engagement)}
												disabled={withdrawing}
											>
												{t("myEngagements.withdraw")}
											</Button>
										)}
									</li>
								);
							})}
						</ul>
					</div>
				)}

				<OpportunityActionPanel
					data-testid="opportunity-action-panel"
					status={capacityStatus}
					facts={panelFacts}
				>
					{showSignUpCta && (
						<div data-testid="signup-cta" className="space-y-3">
							{isFull && (
								<p className="text-sm font-medium text-red-600">
									{t("opportunities.noSpotsLeft")}
								</p>
							)}
							{/* The deadline is the panel's "When" fact two rows up now,
							so repeating it under the button would be the same sentence
							twice inside one card (#2330). */}
							<Button
								onClick={() => {
									setPreselectedSlotId(undefined);
									setShowSignUp(true);
								}}
								disabled={isFull}
								fullWidth
								size="lg"
							>
								{opp.participationType === "ScheduledSlots"
									? t("opportunities.joinWaitlist")
									: t("opportunities.expressInterest")}
							</Button>
						</div>
					)}

					{showLoginPrompt && (
						<div data-testid="login-prompt" className="space-y-3">
							<p className="text-sm text-gray-600">
								{t("opportunities.loginPrompt")}
							</p>
							<Button
								onClick={() =>
									auth.signinRedirect(
										signinLocaleArgs(location.pathname + location.search),
									)
								}
								data-testid="opportunity-signin"
								fullWidth
								size="lg"
							>
								{t("nav.signIn")}
							</Button>
						</div>
					)}
				</OpportunityActionPanel>
			</>
		);
	}

	const headerTitle = pickLocalizedText(
		opportunity.titleDe,
		opportunity.titleEn,
		i18n.language,
	);
	const description = pickLocalizedText(
		opportunity.descriptionDe,
		opportunity.descriptionEn,
		i18n.language,
	);
	const descriptionText = description?.text.trim();
	const headerLead = description
		? { ...description, text: toHeroLead(description.text) }
		: undefined;
	// A one-line description is said in full by the band already, so the
	// section is for the two things the band cannot show: stored line breaks,
	// and whatever the lead had to cut. Comparing the *collapsed* text against
	// the lead keeps a description whose only difference is a double space
	// from rendering a second, visually identical copy.
	const showDescriptionSection =
		!!descriptionText &&
		(descriptionText.includes("\n") ||
			descriptionText.replace(/\s+/g, " ") !== headerLead?.text);

	const isGermanFallback =
		headerTitle.lang !== i18n.language ||
		(headerLead !== undefined && headerLead.lang !== i18n.language);

	return (
		<>
			<PageHeaderBand
				eyebrow={
					<Link
						to={`/organizations/${opportunity.organizationId}`}
						className="text-brand-200 underline-offset-2 transition-colors hover:text-white hover:underline"
					>
						{opportunity.organizationName}
					</Link>
				}
				title={headerTitle.text}
				titleLang={headerTitle.lang}
				lead={headerLead?.text}
				leadLang={headerLead?.lang}
			>
				{isGermanFallback && (
					<p className="text-sm text-brand-200">
						{t("opportunities.germanOnlyNotice")}
					</p>
				)}
			</PageHeaderBand>

			<div data-content-wrapper className="mx-auto max-w-6xl">
				{/* Three grid items rather than two. The action panel is placed between
				the opportunity's identity (banner, chips) and its detail sections, so on
				a phone the one thing a visitor came to do is reachable without scrolling
				past five sections - and it stays a single rendered rail instead of the
				duplicated desktop/mobile copies it used to be, which had every control
				in it twice in the DOM. It spans both content rows so `sticky` has room
				to travel: an item sized to its own single-row area never sticks. */}
				<div className="grid gap-x-10 gap-y-6 lg:grid-cols-[minmax(0,1fr)_20rem] lg:items-start">
					<div
						className="min-w-0 space-y-4 lg:col-start-1 lg:row-start-1"
						data-testid="opportunity-identity"
					>
						{opportunity.bannerImageUrl && (
							// Inside the content column, not above the whole grid: a
							// full-container banner over a 792px column left a visible
							// step in the page's right edge. One aspect ratio at every
							// width, rather than a fixed height per breakpoint, so a
							// banner cropped to fit on desktop survives on a phone
							// too (#2330).
							<img
								src={opportunity.bannerImageUrl}
								alt=""
								width={1200}
								height={480}
								className="aspect-5/2 w-full rounded-card object-cover shadow-resting"
							/>
						)}

						{/* The owner's draft controls, and nothing else. Add-to-calendar
						moved into the action panel, where it belongs next to the date it
						saves, and Report to the page footer: a rare moderation action does
						not deserve the same weight at the top of the page as the schedule
						(#2330). Every control still keeps its label at every width. */}
						{hasOwnerToolbar && (
							<div
								className="flex flex-wrap items-center gap-2"
								data-testid="opportunity-detail-actions"
							>
								<Chip
									tone="warning"
									size="sm"
									data-testid="opportunity-detail-draft-badge"
								>
									{t("opportunities.draftBadge")}
								</Chip>

								<div className="ml-auto flex flex-wrap items-center justify-end gap-2">
									<Button
										variant="outline"
										size="sm"
										onClick={() => setShowEditModal(true)}
										data-testid="opportunity-detail-edit"
									>
										{t("opportunities.edit")}
									</Button>
									<Button
										type="button"
										size="sm"
										onClick={() => void handlePublish()}
										disabled={publishing}
										data-testid="opportunity-detail-publish"
									>
										{publishing
											? t("opportunities.publishing")
											: t("opportunities.publish")}
									</Button>
								</div>
							</div>
						)}

						{/* Taxonomy only. Capacity moved to the panel's status strip and
						the posted date to the page footer: the row used to mix pills,
						coloured bare text and grey bare text from three unrelated kinds of
						information into one line (#2330). */}
						<div className="flex flex-wrap items-center gap-2">
							{opportunity.category && (
								<Chip tone="brand">
									{t(`opportunities.category.${opportunity.category}`)}
								</Chip>
							)}

							{/* Default size, like the category and tag chips either side of it:
							`size="sm"` made this one pill 4px shorter and 8px tighter than its
							visually identical neighbours on the same row, at the same font
							size (#2329 F9). */}
							<Chip tone="neutral" data-testid="opportunity-occurrence">
								{formatOccurrence(opportunity.occurrence, t)}
							</Chip>
							{opportunity.tags?.map((tag) => (
								<Chip
									key={tag}
									tone="neutral"
									to={`/opportunities?tag=${encodeURIComponent(tag)}`}
									aria-label={t("opportunities.filterByTag", { tag })}
								>
									{tag}
								</Chip>
							))}
						</div>
					</div>

					<aside
						aria-label={t("opportunities.actionPanelLabel")}
						className="space-y-6 lg:sticky lg:top-24 lg:col-start-2 lg:row-span-2 lg:row-start-1"
					>
						{renderActionColumn(opportunity)}
					</aside>

					<div className="min-w-0 space-y-14 lg:col-start-1 lg:row-start-2">
						{showDescriptionSection && (
							<DetailSection
								title={t("opportunities.aboutOpportunity")}
								data-testid="opportunity-description"
							>
								{/* Organizers compose this in a multi-line textarea and the
								API stores the newlines verbatim, so `pre-line` is what
								keeps their paragraphs and hyphen bullets from collapsing
								into one run-on block (#2330). */}
								<p
									lang={description?.lang}
									className="max-w-prose leading-relaxed whitespace-pre-line text-gray-700"
								>
									{descriptionText}
								</p>
							</DetailSection>
						)}

						{opportunity.participationType === "ScheduledSlots" &&
							(upcomingTimeSlots.length > 0 || pastTimeSlots.length > 0) && (
								<DetailSection
									{...(upcomingTimeSlots.length > 0
										? { title: t("opportunities.availableTimeSlots") }
										: {})}
									data-testid="opportunity-time-slots"
								>
									{upcomingTimeSlots.length > 0 && (
										<ul className="space-y-2">
											{upcomingTimeSlots.map((ts) => {
												const capacityInput = toCapacityInput(ts);
												const slotEngagement = engagementsBySlot.get(ts.id);
												// currentUserEngagements holds only Pending and
												// Confirmed engagements; the generated type says string.
												const slotEngagementChip = slotEngagement
													? viewerEngagementChip(
															slotEngagement.status === "Confirmed"
																? "Confirmed"
																: "Pending",
															opportunity.participationType,
															t,
														)
													: undefined;
												const clickable =
													canInteract &&
													!engagementsBySlot.has(ts.id) &&
													!isSlotFull(
														capacityInput.maxParticipants,
														capacityInput.bookedCount,
													);
												const rowContent = (
													<>
														<span>
															{formatDateTimeRange(
																ts.startDateTime as unknown as string,
																ts.endDateTime as unknown as string,
																i18n.language,
															)}
														</span>

														<span className="flex shrink-0 items-center gap-1.5 text-xs text-gray-600">
															{/* The row a viewer already holds says so, rather
															than only dropping its chevron (#2079). */}
															{slotEngagementChip && (
																<Chip
																	data-testid="opportunity-time-slot-viewer-engagement"
																	tone={slotEngagementChip.tone}
																	size="sm"
																>
																	{slotEngagementChip.label}
																</Chip>
															)}
															{slotCapacityLabel(capacityInput, t)}
															{clickable && (
																<ChevronRightIcon className="h-3.5 w-3.5 text-gray-400" />
															)}
														</span>
													</>
												);
												return (
													<li key={ts.id}>
														{clickable ? (
															<button
																type="button"
																onClick={() => {
																	setPreselectedSlotId(ts.id);
																	setShowSignUp(true);
																}}
																data-testid="opportunity-time-slot-row"
																className={`${SLOT_ROW_CLASS} w-full ${cardClass} text-left text-sm text-gray-700 transition-shadow hover:shadow-raised`}
															>
																{rowContent}
															</button>
														) : (
															<div
																className={`${SLOT_ROW_CLASS} ${cardClass} text-sm text-gray-700`}
															>
																{rowContent}
															</div>
														)}
													</li>
												);
											})}
										</ul>
									)}

									{pastTimeSlots.length > 0 && (
										<details
											className="group mt-3"
											data-testid="opportunity-past-time-slots"
										>
											{/* The same disclosure treatment as FaqAccordion: the
											app's own chevron rather than whichever triangle the
											browser draws by default (#2330). */}
											<summary className="flex cursor-pointer list-none items-center gap-2 text-sm font-medium text-gray-600 hover:text-gray-800 [&::-webkit-details-marker]:hidden">
												<ChevronDownIcon className="h-4 w-4 shrink-0 text-gray-500 transition-transform group-open:rotate-180" />
												{t("opportunities.pastTimeSlots", {
													count: pastTimeSlots.length,
												})}
											</summary>
											<ul className="mt-2 space-y-2">
												{pastTimeSlots.map((ts) => (
													<li key={ts.id}>
														{/* Expanded, a past row sat under "Available time
														slots" looking exactly like a bookable one - so
														it says which it is (#2330). */}
														<div
															className={`${SLOT_ROW_CLASS} ${cardSubtleClass} text-sm text-gray-500`}
														>
															<span>
																{formatDateTimeRange(
																	ts.startDateTime as unknown as string,
																	ts.endDateTime as unknown as string,
																	i18n.language,
																)}
															</span>
															<Chip tone="neutral" size="sm">
																{t("opportunities.pastSlotBadge")}
															</Chip>
														</div>
													</li>
												))}
											</ul>
										</details>
									)}
								</DetailSection>
							)}

						{!opportunity.isRemote && (
							<DetailSection
								title={t("opportunities.locationHeading")}
								data-testid="opportunity-location"
							>
								{/* Map, address and route in one card rather than three stacked
								blocks. The street address is stated here and nowhere else: the
								panel's "Where" fact names the town only, so a page without
								coordinates no longer prints the same line three times on one
								screen (#2330). */}
								<div className="overflow-hidden rounded-card border border-gray-100 bg-white shadow-resting">
									{/* Narrowed on the two fields rather than on the derived
									`hasMap`: a boolean tells TypeScript nothing, and buying
									it back with a pair of `as number` casts trades a
									compile-time guarantee for nothing. */}
									{opportunity.latitude != null &&
									opportunity.longitude != null ? (
										<div data-testid="opportunity-map">
											<Suspense fallback={<Skeleton className="h-64 w-full" />}>
												<SingleMarkerMap
													latitude={Number(opportunity.latitude)}
													longitude={Number(opportunity.longitude)}
													label={address}
												/>
											</Suspense>
										</div>
									) : null}

									<div
										className={`flex flex-wrap items-center justify-between gap-x-6 gap-y-2 p-4 ${hasMap ? "border-t border-gray-100" : ""}`}
									>
										<p
											className="flex items-center gap-3 text-sm font-medium text-gray-900"
											data-testid="opportunity-address"
										>
											<MapPinIcon className="h-4 w-4 shrink-0 text-brand-700" />
											{address}
										</p>

										<a
											href={directionsUrl}
											target="_blank"
											rel="noopener noreferrer"
											data-testid="opportunity-directions-link"
											className="inline-flex min-h-6 items-center gap-1.5 text-sm font-medium text-brand-700 transition-colors hover:text-brand-800 hover:underline"
										>
											<ArrowTopRightOnSquareIcon className="h-3.5 w-3.5" />
											{t("opportunities.getDirections")}
										</a>
									</div>
								</div>
							</DetailSection>
						)}

						{orgProfileError && !orgProfile && (
							<DetailSection
								title={t("opportunities.aboutOrganization")}
								data-testid="about-organization"
							>
								<LoadMoreError
									message={orgProfileError}
									retrying={retryingOrgProfile}
									onRetry={retryLoadOrgProfile}
								/>
							</DetailSection>
						)}

						{orgProfile &&
							(orgProfile.description ||
								orgProfile.contactEmail ||
								orgProfile.contactPhone ||
								orgProfile.website ||
								orgProfile.address) && (
								<DetailSection
									title={t("opportunities.aboutOrganization")}
									data-testid="about-organization"
								>
									{/* One card for the organizer, not a paragraph plus a
									floating contact box: who they are, how to reach them and
									where to read more are one answer (#2330). */}
									<div className={`${cardClass} sm:p-5`}>
										{orgProfile.description && (
											<div className="mb-4">
												<p
													lang="de"
													className="max-w-prose leading-relaxed text-gray-600"
												>
													{orgProfile.description}
												</p>
												{/* No descriptionEn exists for an organization,
												so in the English UI this is always the German
												original - the same disclosure a German-only
												opportunity already carries (#2328). */}
												{i18n.language !== "de" && (
													<p className="mt-1 text-xs text-gray-500">
														{t("opportunities.germanOnlyNotice")}
													</p>
												)}
											</div>
										)}

										{(orgProfile.contactEmail ||
											orgProfile.contactPhone ||
											orgProfile.website ||
											orgProfile.address) && (
											// Two columns from sm: up - four contact rows stacked
											// single-file made the organizer block as tall as the
											// schedule a visitor came for.
											<div className="grid gap-2.5 text-sm text-gray-700 sm:grid-cols-2">
												{orgProfile.contactEmail && (
													<div className="flex items-center gap-3">
														<EnvelopeIcon className="h-4 w-4 shrink-0 text-brand-700" />
														<a
															href={`mailto:${orgProfile.contactEmail}`}
															className="min-h-6 break-all text-brand-700 transition-colors hover:text-brand-800 hover:underline"
														>
															{orgProfile.contactEmail}
														</a>
													</div>
												)}
												{orgProfile.contactPhone && (
													<div className="flex items-center gap-3">
														<PhoneIcon className="h-4 w-4 shrink-0 text-brand-700" />
														<a
															href={`tel:${orgProfile.contactPhone}`}
															className="min-h-6 text-brand-700 transition-colors hover:text-brand-800 hover:underline"
														>
															{orgProfile.contactPhone}
														</a>
													</div>
												)}
												{orgProfile.website && (
													<div className="flex items-center gap-3">
														<GlobeIcon className="h-4 w-4 shrink-0 text-brand-700" />
														<a
															href={orgProfile.website}
															target="_blank"
															rel="noopener noreferrer"
															className="min-h-6 break-all text-brand-700 transition-colors hover:text-brand-800 hover:underline"
														>
															{orgProfile.website}
														</a>
													</div>
												)}
												{orgProfile.address && (
													<div className="flex items-center gap-3">
														<BuildingOfficeIcon className="h-4 w-4 shrink-0 text-brand-700" />
														<span>
															{orgProfile.address.street}{" "}
															{orgProfile.address.houseNumber},{" "}
															{orgProfile.address.zipCode}{" "}
															{orgProfile.address.city}
														</span>
													</div>
												)}
											</div>
										)}

										<Link
											to={`/organizations/${opportunity.organizationId}`}
											className="mt-4 inline-flex min-h-6 items-center gap-1.5 text-sm font-medium text-brand-700 transition-colors hover:text-brand-800 hover:underline"
										>
											{t("opportunities.viewOrganizationProfile")}
											<ChevronRightIcon className="h-3.5 w-3.5" />
										</Link>
									</div>
								</DetailSection>
							)}

						{otherOrgOpportunities.length > 0 && (
							<DetailSection
								title={t("opportunities.moreFromOrganization")}
								data-testid="more-from-organization"
							>
								<ul className="grid grid-cols-1 gap-4 sm:grid-cols-2">
									{otherOrgOpportunities.map((opp) => (
										<OpportunityCard
											key={opp.id}
											item={toCardItem(opp)}
											headingLevel={3}
											viewerEngagementStatus={engagementStates.get(opp.id)}
										/>
									))}
								</ul>
							</DetailSection>
						)}

						{/* Page metadata and the one action aimed at the page rather than
						the opportunity. Report sat in the top toolbar with the weight of a
						primary control; it is a rare moderation action, so it belongs where
						every comparable listing site puts it - at the foot, quiet, still
						labelled and still a 24px target (#2330). */}
						<div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 border-t border-gray-200 pt-6">
							{/* An `aria-label` here would be dropped on the floor: it is
							prohibited on a role-less <span>, so the absolute timestamp
							never reached a screen reader - and nothing catches that,
							since axe reports `aria-prohibited-attr` as inconclusive once
							the element has text of its own. An sr-only sibling delivers
							it for real; `title` stays the mouse-only bonus it always
							was. */}
							<span className="text-xs text-gray-500" title={postedOnAbsolute}>
								{postedOnRelative}
								<span className="sr-only"> ({postedOnAbsolute})</span>
							</span>

							{!isOwner && (
								<Button
									variant="secondary"
									size="sm"
									onClick={() =>
										isAuthenticated
											? setShowReport(true)
											: void auth.signinRedirect(
													reportIntentSigninArgs(
														location.pathname,
														location.search,
														opportunity.id,
													),
												)
									}
									data-testid="report-opportunity"
									title={t("opportunities.report")}
									aria-label={t("opportunities.reportOpportunity")}
								>
									<FlagIcon className="h-4 w-4" />
									<span>{t("opportunities.report")}</span>
								</Button>
							)}
						</div>
					</div>
				</div>
				{showSignUp && (
					<SignUpModal
						opportunityId={opportunity.id}
						organizationId={opportunity.organizationId}
						participationType={opportunity.participationType}
						timeSlots={upcomingTimeSlots}
						engagedTimeSlotIds={[...engagementsBySlot.keys()]}
						preselectedTimeSlotId={preselectedSlotId}
						onClose={() => {
							setShowSignUp(false);
							setPreselectedSlotId(undefined);
						}}
						onSuccess={() => {
							setShowSignUp(false);
							setPreselectedSlotId(undefined);
							dispatchToast(
								"success",
								t(
									isInterestBased ? "signUp.successInterest" : "signUp.success",
								),
							);
							load();
						}}
					/>
				)}

				{withdrawTarget && (
					<ConfirmDialog
						title={t(
							isInterestBased
								? "confirmDialog.withdraw.titleInterest"
								: "confirmDialog.withdraw.title",
						)}
						message={t(
							withdrawTarget.remainingReactivations === 0
								? isInterestBased
									? "confirmDialog.withdraw.messageLimitReachedInterest"
									: "confirmDialog.withdraw.messageLimitReached"
								: isInterestBased
									? "confirmDialog.withdraw.messageInterest"
									: "confirmDialog.withdraw.message",
							{ title: headerTitle.text },
						)}
						confirmLabel={t("confirmDialog.withdraw.confirm")}
						onConfirm={handleWithdrawConfirm}
						onClose={() => {
							setWithdrawTarget(null);
							setWithdrawError(null);
						}}
						loading={withdrawing}
						error={withdrawError}
					>
						{withdrawTarget.remainingReactivations === 0 && (
							<Link
								to={`/organizations/${opportunity.organizationId}`}
								className={`mt-1 inline-block text-sm ${inlineLinkClass}`}
							>
								{t("common.contactOrganization")}
							</Link>
						)}
						{withdrawLimitWarningActive && (
							<WarningBanner
								ref={withdrawLimitWarningRef}
								tabIndex={-1}
								className="focus:outline-none"
								message={t(
									isInterestBased
										? "confirmDialog.withdraw.limitWarningInterest"
										: "confirmDialog.withdraw.limitWarning",
								)}
							/>
						)}
					</ConfirmDialog>
				)}

				{showReport && (
					<ReportContentModal
						targetLabel={headerTitle.text}
						targetLabelLang={headerTitle.lang}
						onSubmit={handleReportSubmit}
						onClose={() => setShowReport(false)}
					/>
				)}

				{showEditModal && (
					<Suspense
						fallback={
							<ModalLoadingFallback onClose={() => setShowEditModal(false)} />
						}
					>
						<CreateVolunteerOpportunityModal
							organizationId={opportunity.organizationId}
							initialOpportunity={opportunity}
							onClose={() => setShowEditModal(false)}
							onSuccess={() => {
								setShowEditModal(false);
								load();
							}}
						/>
					</Suspense>
				)}
			</div>
		</>
	);
}
