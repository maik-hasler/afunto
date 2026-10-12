import { useEffect, useRef, useState } from "react";
import { useNavigate, useSearchParams, Link } from "react-router";
import { useAuth } from "react-oidc-context";
import { Trans, useTranslation } from "react-i18next";
import type { MyProfileResponse, StreakSummary } from "../../client";
import { useApiClient } from "../../hooks/useApiClient";
import { usePageTitle } from "../../hooks/usePageTitle";
import {
	getInputClass,
	getTextareaClass,
	inputClass,
	labelClass,
} from "../../lib/formClasses";
import { cardClass, cardSubtleClass } from "../../lib/surfaceClasses";
import { IMAGE_UPLOAD_ACCEPT, getImageUploadHint } from "../../lib/imageUpload";
import { getInitials } from "../../lib/initials";
import { setDisplayNameOverride } from "../../lib/displayName";
import { BADGE_PROGRESS_TARGETS } from "../../components/BadgeGrid";
import Chip, { type ChipTone } from "../../components/Chip";
import Dropdown from "../../components/Dropdown";
import EmptyState from "../../components/EmptyState";
import ProfileFieldsView from "../../components/ProfileFieldsView";
import ProfileSubNav from "../../components/ProfileSubNav";
import PageHeaderBand from "../../components/PageHeaderBand";
import TwoColumnPageLayout from "../../components/TwoColumnPageLayout";
import SectionHeading from "../../components/SectionHeading";
import Skeleton from "../../components/Skeleton";
import LoadMoreError from "../../components/LoadMoreError";
import ErrorBanner from "../../components/ErrorBanner";
import SuccessBanner from "../../components/SuccessBanner";
import CharCount from "../../components/CharCount";
import ImageCropModal from "../../components/ImageCropModal";
import FileUploadButton from "../../components/FileUploadButton";
import Field from "../../components/Field";
import { getInvalidFieldNames } from "../../lib/apiError";
import Button from "../../components/Button";
import {
	ArrowTopRightOnSquareIcon,
	CalendarIcon,
	CheckIcon,
	PencilIcon,
} from "../../components/icons";
import AchievementsSection from "./AchievementsSection";
import DangerZoneCard from "./DangerZoneCard";
import { useProfileForm, type ContactPref } from "./useProfileForm";
import { useAvatarUpload } from "./useAvatarUpload";

// Keyed by the property names the API blames in its per-field 400, so a
// rejection can name the limit the field actually broke (#2320).
const FIELD_MAX_LENGTHS = {
	firstName: 100,
	lastName: 100,
	bio: 1000,
	phone: 30,
} as const;

/** `flex-1 basis-0` rather than shrink-to-fit, so however many tiles are on
 *  screen they come out the same width (#2330). */
const STAT_TILE_CLASS =
	"flex flex-1 basis-0 items-center gap-3 rounded-card border border-gray-100 bg-white px-4 py-3";

const NAME_MAX_LENGTH = 100;
const BIO_MAX_LENGTH = 1000;
const PHONE_MAX_LENGTH = 30;
const SKILL_MAX_LENGTH = 100;
const LANGUAGE_MAX_LENGTH = 50;

const LEGACY_SCROLL_SECTIONS: Record<string, string> = {
	achievements: "achievements",
};

const LEGACY_REDIRECT_TABS = new Set([
	"activity",
	"engagements",
	"invitations",
]);

function FireIcon({ className = "h-5 w-5" }: { className?: string }) {
	return (
		<svg
			className={className}
			fill="none"
			viewBox="0 0 24 24"
			strokeWidth={1.5}
			stroke="currentColor"
			aria-hidden="true"
		>
			<path
				strokeLinecap="round"
				strokeLinejoin="round"
				d="M15.362 5.214A8.252 8.252 0 0 1 12 21 8.25 8.25 0 0 1 6.038 7.047 8.287 8.287 0 0 0 9 9.601a8.983 8.983 0 0 1 3.361-6.867 8.21 8.21 0 0 0 3 2.48Z"
			/>
		</svg>
	);
}

/**
 * The streak cards used to chip the badge name outright, so a 1-week streak
 * was labelled "Wochenheld" while the grid on the same page showed that badge
 * locked at "1 von 4". The chip now states progress toward the badge until
 * the streak actually reaches its target - the same threshold BadgeGrid reads
 * for the grid below, so the two can no longer disagree (#2330).
 */
function StreakBadgeChip({
	badgeKey,
	current,
}: {
	badgeKey: string;
	current: number;
}) {
	const { t } = useTranslation();
	const target = BADGE_PROGRESS_TARGETS[badgeKey]?.target;
	const name = t(`achievements.badges.${badgeKey}.name`);

	if (target === undefined || current >= target) {
		return (
			<Chip tone="brand" size="sm" className="mt-1">
				{name}
			</Chip>
		);
	}

	return (
		<Chip tone="neutral" size="sm" className="mt-1">
			{t("achievements.badgeStreakProgress", { badge: name, current, target })}
		</Chip>
	);
}

function ChipInput({
	inputRef,
	inputId,
	chips,
	inputValue,
	placeholder,
	maxLength,
	onInputChange,
	onAdd,
	onRemove,
	removeLabel,
	tone = "brand",
}: {
	inputRef: React.RefObject<HTMLInputElement | null>;
	inputId: string;
	chips: string[];
	inputValue: string;
	placeholder: string;
	maxLength: number;
	onInputChange: (v: string) => void;
	onAdd: (v: string) => void;
	onRemove: (v: string) => void;
	removeLabel: string;
	tone?: ChipTone;
}) {
	function handleKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
		if (e.key === "Enter") {
			e.preventDefault();
			onAdd(inputValue);
		}
	}

	return (
		<div className="mt-1">
			{chips.length > 0 && (
				<div className="mb-2 flex flex-wrap gap-2">
					{chips.map((chip) => (
						<Chip
							key={chip}
							tone={tone}
							onRemove={() => onRemove(chip)}
							removeLabel={`${removeLabel} ${chip}`}
						>
							{chip}
						</Chip>
					))}
				</div>
			)}
			<input
				ref={inputRef}
				id={inputId}
				type="text"
				maxLength={maxLength}
				value={inputValue}
				placeholder={placeholder}
				onChange={(e) => onInputChange(e.target.value)}
				onKeyDown={handleKeyDown}
				onBlur={() => {
					if (inputValue.trim()) onAdd(inputValue);
				}}
				className={inputClass}
			/>
		</div>
	);
}

export default function ProfileOverviewPage() {
	const auth = useAuth();
	const api = useApiClient();
	const { t, i18n } = useTranslation();
	const [searchParams] = useSearchParams();
	const navigate = useNavigate();
	usePageTitle(t("profile.title"));

	const [profile, setProfile] = useState<MyProfileResponse | null>(null);
	const [profileLoading, setProfileLoading] = useState(true);
	const [saving, setSaving] = useState(false);

	// Load and save failures need separate slots. The load error renders a
	// retry that re-fetches the profile and resets the form, so letting a
	// failed save render it turned "Retry" into "throw away everything I just
	// typed" (#2315).
	const [loadError, setLoadError] = useState<string | null>(null);
	const [saveError, setSaveError] = useState<string | null>(null);
	const [invalidFields, setInvalidFields] = useState<string[]>([]);

	const [retryingProfileLoad, setRetryingProfileLoad] = useState(false);
	const [successMessage, setSuccessMessage] = useState<string | null>(null);
	const [avatarUrl, setAvatarUrl] = useState<string | null>(null);
	const [editing, setEditing] = useState(false);
	const [streaks, setStreaks] = useState<StreakSummary | null>(null);
	const [engagementCount, setEngagementCount] = useState<number | null>(null);

	const profileLoadCancelledRef = useRef(false);

	const form = useProfileForm(profile);
	const avatarUpload = useAvatarUpload(setAvatarUrl);

	async function loadProfile() {
		const retryDelaysMs = [500, 1000, 2000];
		for (let attempt = 0; ; attempt++) {
			try {
				const data = await api.getUserProfile({});
				if (profileLoadCancelledRef.current) return;
				setProfile(data);
				form.reset(data);
				setAvatarUrl(data.avatarUrl ?? null);
				setLoadError(null);
				return;
			} catch {
				if (profileLoadCancelledRef.current) return;
				if (attempt >= retryDelaysMs.length) {
					setLoadError(t("profile.loadError"));
					return;
				}
				await new Promise<void>((resolve) =>
					setTimeout(resolve, retryDelaysMs[attempt]),
				);
			}
		}
	}

	useEffect(() => {
		profileLoadCancelledRef.current = false;
		setProfileLoading(true);
		loadProfile().finally(() => {
			if (!profileLoadCancelledRef.current) setProfileLoading(false);
		});

		return () => {
			profileLoadCancelledRef.current = true;
		};
		// eslint-disable-next-line react-hooks/exhaustive-deps
	}, [auth.isAuthenticated]);

	function handleRetryProfileLoad() {
		setRetryingProfileLoad(true);
		loadProfile().finally(() => setRetryingProfileLoad(false));
	}

	useEffect(() => {
		api
			.getMyStreaks({})
			.then(setStreaks)
			.catch(() => {});
		// eslint-disable-next-line react-hooks/exhaustive-deps
	}, []);

	const userId = auth.user?.profile?.sub;
	useEffect(() => {
		if (!userId) return;
		api
			.getPublicUserProfile({ path: { userId } })
			.then((p) => setEngagementCount(p.engagementCount))
			.catch(() => {});
		// eslint-disable-next-line react-hooks/exhaustive-deps
	}, [userId]);

	useEffect(() => {
		if (LEGACY_REDIRECT_TABS.has(searchParams.get("tab") ?? "")) {
			navigate("/my-signups", { replace: true });
		}
		// eslint-disable-next-line react-hooks/exhaustive-deps
	}, [searchParams]);

	useEffect(() => {
		if (profileLoading) return;
		const sectionId = LEGACY_SCROLL_SECTIONS[searchParams.get("tab") ?? ""];
		if (!sectionId) return;
		const reduceMotion = window.matchMedia(
			"(prefers-reduced-motion: reduce)",
		).matches;
		const frame = requestAnimationFrame(() => {
			document.getElementById(sectionId)?.scrollIntoView({
				block: "start",
				behavior: reduceMotion ? "auto" : "smooth",
			});
		});
		return () => cancelAnimationFrame(frame);
		// eslint-disable-next-line react-hooks/exhaustive-deps
	}, [profileLoading, streaks]);

	async function handleSave(e: React.FormEvent) {
		e.preventDefault();
		setSaving(true);
		setSaveError(null);
		setInvalidFields([]);
		setSuccessMessage(null);
		const savedValues = {
			firstName: form.state.firstName || undefined,
			lastName: form.state.lastName || undefined,
			bio: form.state.bio || undefined,
			phone: form.state.phone || undefined,
			skills: form.state.skills,
			languages: form.state.languages,
			preferredContact: form.state.preferredContact || undefined,
		};

		try {
			await api.updateUserProfile({ body: savedValues });
			setProfile((prev) =>
				prev ? ({ ...prev, ...savedValues } as MyProfileResponse) : prev,
			);
			// The header reads the id_token, which Keycloak will not re-issue
			// until the next sign-in - hand it the new name directly (#2330).
			if (userId) {
				setDisplayNameOverride(
					userId,
					`${savedValues.firstName ?? ""} ${savedValues.lastName ?? ""}`.trim() ||
						(profile?.username ?? ""),
				);
			}
			setSuccessMessage(t("profile.savedSuccess"));
			setEditing(false);
		} catch (error) {
			// The server names the fields it rejected; say which they are
			// instead of dropping that into a bare "saving failed" (#2320).
			const rejected = getInvalidFieldNames(error);
			setInvalidFields(rejected);
			setSaveError(
				rejected.length > 0
					? t("profile.saveFieldError")
					: t("profile.saveError"),
			);
		} finally {
			setSaving(false);
		}
	}

	function handleCancel() {
		form.reset(profile);
		setSaveError(null);
		setInvalidFields([]);
		setEditing(false);
	}

	const isProfileFieldsEmpty =
		!form.state.bio &&
		form.state.skills.length === 0 &&
		form.state.languages.length === 0 &&
		!form.state.preferredContact &&
		!form.state.phone;

	// The server blames its own PascalCase property names, which
	// getInvalidFieldNames lowercases - so match case-insensitively.
	type LimitedField = keyof typeof FIELD_MAX_LENGTHS;
	const fieldRejected = (name: LimitedField) =>
		invalidFields.includes(name.toLowerCase());
	const fieldError = (name: LimitedField) =>
		fieldRejected(name)
			? t("profile.fieldTooLong", { max: FIELD_MAX_LENGTHS[name] })
			: undefined;

	const displayName =
		form.state.firstName || form.state.lastName
			? `${form.state.firstName} ${form.state.lastName}`.trim()
			: (profile?.username ?? "");

	return (
		<>
			<PageHeaderBand
				eyebrow={t("profile.eyebrow")}
				title={t("profile.title")}
				compactTitle
			/>

			<TwoColumnPageLayout
				variant="subNav"
				sidebar={<ProfileSubNav active="profile" />}
			>
				{profileLoading && (
					<div
						className={`mb-6 flex items-center gap-4 ${cardSubtleClass}`}
						role="status"
					>
						<span className="sr-only">{t("profile.loading")}</span>
						<Skeleton className="h-16 w-16 shrink-0 rounded-full" />
						<div className="flex-1 space-y-2">
							<Skeleton className="h-5 w-40" />
							<Skeleton className="h-4 w-24" />
						</div>
					</div>
				)}

				{!profileLoading && (
					<>
						{loadError && (
							<LoadMoreError
								message={loadError}
								retrying={retryingProfileLoad}
								onRetry={handleRetryProfileLoad}
							/>
						)}

						{saveError && (
							<ErrorBanner
								message={saveError}
								className="mb-4"
								data-testid="profile-save-error"
							/>
						)}

						<SuccessBanner message={successMessage} className="mb-4" />

						{/* Stacked, never a side-by-side split: this column is capped
							at 800px (max-w-5xl minus the 11rem sub-nav rail and its gap), but
							the split keyed off the viewport, so from lg: up the tiles - which
							refused to shrink - took 680px of it and left the identity block
							40px wide. The name and the public-profile link then spilled out of
							their 0px-wide boxes and painted across the tiles. */}
						{!editing && (
							<div
								data-testid="profile-hero"
								className="mb-8 flex flex-col gap-5 rounded-card bg-brand-100 p-5 sm:p-6"
							>
								<div
									data-testid="profile-identity"
									className="flex min-w-0 items-center gap-4"
								>
									{avatarUrl ? (
										<img
											src={avatarUrl}
											alt=""
											width={72}
											height={72}
											className="h-18 w-18 shrink-0 rounded-full object-cover ring-3 ring-white"
										/>
									) : (
										<span className="flex h-18 w-18 shrink-0 items-center justify-center rounded-full bg-white text-2xl font-semibold text-brand-700 ring-3 ring-white">
											{getInitials(displayName)}
										</span>
									)}
									<div className="min-w-0">
										<p
											data-testid="profile-display-name"
											className="font-display text-3xl font-bold break-words text-gray-900"
										>
											{displayName}
										</p>
										<p className="truncate text-sm text-brand-800">
											@{profile?.username}
										</p>
										<p className="truncate text-sm text-brand-800">
											{profile?.email}
										</p>
										{/* The notice below tells volunteers they have a public
											profile page, and nothing in the app linked to one - a
											crawl of every volunteer-reachable page found zero
											/users/ anchors. Here it is (#2330). */}
										{userId && (
											<Link
												to={`/users/${userId}`}
												data-testid="view-public-profile"
												className="mt-1 inline-flex items-center gap-1.5 text-sm font-medium text-brand-800 underline-offset-2 transition-colors hover:text-brand-900 hover:underline"
											>
												<ArrowTopRightOnSquareIcon className="h-3.5 w-3.5" />
												{t("profile.viewPublicProfile")}
											</Link>
										)}
									</div>
								</div>

								{/* One row of equal-width tiles, rather than three
									shrink-to-fit boxes that came out 177/161/168px wide and
									wrapped into an L-shape (#2330). */}
								<div className="flex flex-col gap-3 sm:flex-row">
									{engagementCount !== null && (
										<div
											data-testid="profile-stat-engagements"
											className={STAT_TILE_CLASS}
										>
											<span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-brand-100 text-brand-700">
												<CheckIcon className="h-5 w-5" />
											</span>
											<div>
												<p className="text-xl font-bold text-gray-900">
													{engagementCount}
												</p>
												<p className="text-xs text-gray-500">
													{t("achievements.engagementStatLabel", {
														count: engagementCount,
													})}
												</p>
											</div>
										</div>
									)}

									{streaks && streaks.activityStreak > 0 && (
										<div
											data-testid="profile-stat-streak"
											className={STAT_TILE_CLASS}
										>
											<span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-brand-100 text-brand-700">
												<FireIcon />
											</span>
											<div>
												<p className="text-xl font-bold text-gray-900">
													{streaks.activityStreak}
												</p>
												<p className="text-xs text-gray-500">
													{t("achievements.activityStreak", {
														count: streaks.activityStreak,
													})}
												</p>
												<StreakBadgeChip
													badgeKey="weekly-hero-4"
													current={streaks.activityStreak}
												/>
											</div>
										</div>
									)}
									{streaks && streaks.loginStreak > 0 && (
										<div
											data-testid="profile-stat-login-streak"
											className={STAT_TILE_CLASS}
										>
											<span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-brand-100 text-brand-700">
												<CalendarIcon className="h-5 w-5" />
											</span>
											<div>
												<p className="text-xl font-bold text-gray-900">
													{streaks.loginStreak}
												</p>
												<p className="text-xs text-gray-500">
													{t("achievements.loginStreakLabel", {
														count: streaks.loginStreak,
													})}
												</p>
												<StreakBadgeChip
													badgeKey="on-a-roll-7"
													current={streaks.loginStreak}
												/>
											</div>
										</div>
									)}
								</div>
							</div>
						)}

						<section className="mb-10">
							<div className="flex items-center justify-between gap-3">
								<SectionHeading>{t("profile.sectionDetails")}</SectionHeading>
								{!editing && !isProfileFieldsEmpty && (
									<Button
										type="button"
										variant="outline"
										size="sm"
										onClick={() => setEditing(true)}
										data-testid="profile-edit"
										className="shrink-0"
									>
										<PencilIcon className="h-4 w-4" />
										{t("common.edit")}
									</Button>
								)}
							</div>

							{!editing &&
								(isProfileFieldsEmpty ? (
									<EmptyState
										title={t("profile.emptyStateTitle")}
										message={t("profile.emptyStateMessage")}
										action={{
											label: t("profile.emptyStateCta"),
											onClick: () => setEditing(true),
											testId: "profile-edit",
										}}
									/>
								) : (
									<div className={`${cardClass} sm:p-6`}>
										<ProfileFieldsView
											bio={form.state.bio}
											skills={form.state.skills}
											languages={form.state.languages}
											preferredContact={form.state.preferredContact || null}
											phone={form.state.phone || null}
										/>
									</div>
								))}

							{!editing && !isProfileFieldsEmpty && (
								<p className="mt-4 text-xs text-gray-500">
									<Trans
										i18nKey="profile.publicProfileNotice"
										components={{
											privacyLink: (
												<Link to="/privacy-policy" className="underline" />
											),
										}}
									/>
								</p>
							)}

							{editing && (
								<form onSubmit={handleSave} className="space-y-6">
									<div>
										<h3 className="mb-4 text-sm font-semibold text-gray-900">
											{t("account.title")}
										</h3>
										<div className="space-y-5">
											<div>
												<p className={`mb-1 ${labelClass}`}>
													{t("profile.fieldAvatar")}
												</p>
												<div className="flex items-center gap-4">
													{avatarUrl ? (
														<img
															src={avatarUrl}
															alt=""
															width={64}
															height={64}
															className="h-16 w-16 rounded-full object-cover ring-2 ring-brand-100"
														/>
													) : (
														<span className="flex h-16 w-16 items-center justify-center rounded-full bg-brand-100 text-2xl font-semibold text-brand-700">
															{getInitials(displayName)}
														</span>
													)}
													<div>
														<div className="flex items-center gap-3">
															<FileUploadButton
																id="avatar-upload"
																label={
																	avatarUpload.uploading
																		? t("profile.avatarUploading")
																		: t("profile.avatarUpload")
																}
																accept={IMAGE_UPLOAD_ACCEPT}
																onChange={avatarUpload.handleChange}
																disabled={
																	avatarUpload.uploading ||
																	avatarUpload.removing
																}
																inputRef={avatarUpload.inputRef}
																ariaDescribedBy={
																	avatarUpload.error
																		? "avatar-upload-error"
																		: undefined
																}
															/>
															{avatarUrl && (
																<button
																	type="button"
																	data-testid="avatar-remove"
																	onClick={() =>
																		void avatarUpload.handleRemove()
																	}
																	disabled={
																		avatarUpload.uploading ||
																		avatarUpload.removing
																	}
																	className="text-sm font-medium text-red-600 hover:underline disabled:cursor-not-allowed disabled:opacity-50"
																>
																	{avatarUpload.removing
																		? t("profile.avatarRemoving")
																		: t("profile.avatarRemove")}
																</button>
															)}
														</div>
														<p className="mt-1 text-xs text-gray-500">
															{getImageUploadHint(t, i18n.language)}
														</p>
														{avatarUpload.error && (
															<p
																id="avatar-upload-error"
																className="mt-1 text-xs text-red-600"
																role="alert"
															>
																{avatarUpload.error}
															</p>
														)}
													</div>
												</div>
											</div>

											<div className="grid grid-cols-1 gap-5 sm:grid-cols-2">
												<Field label={t("account.fieldUsername")} id="username">
													<input
														id="username"
														disabled
														autoComplete="username"
														value={profile?.username ?? ""}
														className={`${inputClass} cursor-not-allowed bg-gray-50 text-gray-500`}
													/>
												</Field>

												<Field label={t("account.fieldEmail")} id="email">
													<input
														id="email"
														disabled
														type="email"
														autoComplete="email"
														value={profile?.email ?? ""}
														className={`${inputClass} cursor-not-allowed bg-gray-50 text-gray-500`}
													/>
													<p className="mt-1 text-xs text-gray-500">
														{t("account.emailHint")}
													</p>
												</Field>

												<Field
													label={t("account.fieldFirstName")}
													id="first-name"
													error={fieldError("firstName")}
												>
													<input
														id="first-name"
														maxLength={NAME_MAX_LENGTH}
														autoComplete="given-name"
														aria-invalid={
															fieldRejected("firstName") || undefined
														}
														aria-describedby={
															fieldRejected("firstName")
																? "first-name-error"
																: undefined
														}
														value={form.state.firstName}
														onChange={(e) => form.setFirstName(e.target.value)}
														className={getInputClass(
															fieldRejected("firstName"),
														)}
													/>
												</Field>

												<Field
													label={t("account.fieldLastName")}
													id="last-name"
													error={fieldError("lastName")}
												>
													<input
														id="last-name"
														maxLength={NAME_MAX_LENGTH}
														autoComplete="family-name"
														aria-invalid={
															fieldRejected("lastName") || undefined
														}
														aria-describedby={
															fieldRejected("lastName")
																? "last-name-error"
																: undefined
														}
														value={form.state.lastName}
														onChange={(e) => form.setLastName(e.target.value)}
														className={getInputClass(fieldRejected("lastName"))}
													/>
												</Field>
											</div>
										</div>
									</div>

									<hr className="border-gray-200" />

									<div className="space-y-5">
										<Field
											label={t("profile.fieldBio")}
											id="bio"
											error={fieldError("bio")}
										>
											<textarea
												id="bio"
												rows={4}
												maxLength={BIO_MAX_LENGTH}
												aria-invalid={fieldRejected("bio") || undefined}
												aria-describedby={
													fieldRejected("bio") ? "bio-error" : undefined
												}
												value={form.state.bio}
												placeholder={t("profile.bioPlaceholder")}
												onChange={(e) => form.setBio(e.target.value)}
												className={getTextareaClass(fieldRejected("bio"))}
											/>
											<CharCount
												current={form.state.bio.length}
												max={BIO_MAX_LENGTH}
											/>
										</Field>

										<Field label={t("profile.fieldSkills")} id="skill-input">
											<ChipInput
												inputRef={form.skillInputRef}
												inputId="skill-input"
												chips={form.state.skills}
												inputValue={form.state.skillInput}
												placeholder={t("profile.skillsPlaceholder")}
												maxLength={SKILL_MAX_LENGTH}
												onInputChange={form.setSkillInput}
												onAdd={form.addSkill}
												onRemove={form.removeSkill}
												removeLabel={t("profile.removeChip")}
											/>
										</Field>

										<Field label={t("profile.fieldLanguages")} id="lang-input">
											<ChipInput
												inputRef={form.langInputRef}
												inputId="lang-input"
												chips={form.state.languages}
												inputValue={form.state.langInput}
												placeholder={t("profile.languagesPlaceholder")}
												maxLength={LANGUAGE_MAX_LENGTH}
												onInputChange={form.setLangInput}
												onAdd={form.addLanguage}
												onRemove={form.removeLanguage}
												removeLabel={t("profile.removeChip")}
												tone="neutral"
											/>
										</Field>

										<Field
											label={t("profile.fieldPhone")}
											id="phone"
											error={fieldError("phone")}
										>
											<input
												id="phone"
												type="tel"
												maxLength={PHONE_MAX_LENGTH}
												autoComplete="tel"
												aria-invalid={fieldRejected("phone") || undefined}
												aria-describedby={
													fieldRejected("phone") ? "phone-error" : undefined
												}
												value={form.state.phone}
												placeholder={t("profile.phonePlaceholder")}
												onChange={(e) => form.setPhone(e.target.value)}
												className={getInputClass(fieldRejected("phone"))}
											/>
										</Field>

										<Field
											label={t("profile.fieldPreferredContact")}
											id="preferred-contact"
										>
											<Dropdown
												id="preferred-contact"
												value={form.state.preferredContact}
												onChange={(v) =>
													form.setPreferredContact(v as ContactPref)
												}
												className={inputClass}
												options={[
													{
														value: "",
														label: t("profile.preferredContactNone"),
													},
													{
														value: "Email",
														label: t("profile.preferredContactEmail"),
													},
													{
														value: "Phone",
														label: t("profile.preferredContactPhone"),
													},
												]}
											/>
										</Field>
									</div>

									{/* Sticky, and inside the form rather than above it: the
										pair used to sit in the statically positioned section
										header, so by the time the volunteer had scrolled to the
										last field Save was 426px above the top of the viewport
										with nothing left to click (#2330). */}
									<div className="sticky bottom-0 -mx-4 flex items-center justify-end gap-2 border-t border-gray-200 bg-white/95 px-4 py-3 backdrop-blur-sm sm:-mx-6 sm:px-6">
										<Button
											type="button"
											variant="outline"
											size="sm"
											onClick={handleCancel}
											disabled={saving}
											data-testid="profile-cancel"
										>
											{t("common.cancel")}
										</Button>
										<Button
											type="submit"
											size="sm"
											disabled={saving}
											data-testid="profile-save"
										>
											<CheckIcon className="h-4 w-4" />
											{saving ? t("common.saving") : t("common.save")}
										</Button>
									</div>
								</form>
							)}
						</section>
					</>
				)}

				<AchievementsSection
					engagementCount={engagementCount}
					streaks={streaks}
				/>

				<DangerZoneCard />
			</TwoColumnPageLayout>

			{avatarUpload.croppingFile && (
				<ImageCropModal
					file={avatarUpload.croppingFile}
					aspectRatio={1}
					shape="circle"
					outputWidth={320}
					outputHeight={320}
					title={t("profile.avatarUpload")}
					onCancel={avatarUpload.handleCropCancel}
					onCropped={(f) => void avatarUpload.handleCropped(f)}
				/>
			)}
		</>
	);
}
