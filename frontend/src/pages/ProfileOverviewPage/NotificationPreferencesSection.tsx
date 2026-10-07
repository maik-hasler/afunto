import { useTranslation } from "react-i18next";
import type { NotificationPreferencesResponse } from "../../client";
import { useMyOrganizations } from "../../hooks/useMyOrganizations";
import { cardClass } from "../../lib/surfaceClasses";
import PageSectionHeading from "../../components/PageSectionHeading";
import Skeleton from "../../components/Skeleton";
import ErrorBanner from "../../components/ErrorBanner";
import { checkboxClass } from "../../lib/formClasses";
import type { PreferenceKey } from "./useNotificationPreferencesForm";

const PREFERENCE_ROWS: {
	key: PreferenceKey;
	labelKey: string;
	organizerOnly?: boolean;
}[] = [
	{
		key: "notifyOnNewSignUp",
		labelKey: "notificationPreferences.newSignUp",
		organizerOnly: true,
	},
	{
		key: "notifyOnWithdrawal",
		labelKey: "notificationPreferences.withdrawal",
		organizerOnly: true,
	},
	{
		key: "notifyOnEngagementConfirmed",
		labelKey: "notificationPreferences.engagementConfirmed",
	},
	{
		key: "notifyOnEngagementReminder",
		labelKey: "notificationPreferences.engagementReminder",
	},
];

function PreferenceRowList({
	rows,
	preferences,
	editing,
	onToggle,
}: {
	rows: typeof PREFERENCE_ROWS;
	preferences: NotificationPreferencesResponse;
	editing: boolean;
	onToggle: (key: PreferenceKey) => void;
}) {
	const { t } = useTranslation();
	return (
		<div className="space-y-3">
			{rows.map((row) => (
				<label
					key={row.key}
					htmlFor={row.key}
					className={`flex items-start gap-3 py-1 ${editing ? "cursor-pointer" : "cursor-default"}`}
				>
					<input
						type="checkbox"
						id={row.key}
						checked={preferences[row.key]}
						disabled={!editing}
						onChange={() => onToggle(row.key)}
						className={`mt-0.5 h-4 w-4 ${checkboxClass} disabled:cursor-default disabled:opacity-60`}
					/>
					<span className="text-sm text-gray-800">{t(row.labelKey)}</span>
				</label>
			))}
		</div>
	);
}

/**
 * Read-only outside of the page's own edit mode - toggling a preference here
 * takes the same Save/Cancel round trip as the rest of the profile fields,
 * driven by the `editing` flag the parent page owns rather than a section of
 * its own (#2354).
 */
export default function NotificationPreferencesSection({
	editing,
	preferences,
	loading,
	loadError,
	onToggle,
}: {
	editing: boolean;
	preferences: NotificationPreferencesResponse | null;
	loading: boolean;
	loadError: boolean;
	onToggle: (key: PreferenceKey) => void;
}) {
	const { t } = useTranslation();
	const {
		orgs,
		loading: orgsLoading,
		failed: orgsFailed,
	} = useMyOrganizations();

	const organizerRowsVisible = orgs.length > 0 || orgsFailed;
	const visibleRows = PREFERENCE_ROWS.filter(
		(row) => !row.organizerOnly || organizerRowsVisible,
	);
	const organizerRows = visibleRows.filter((row) => row.organizerOnly);
	const volunteerRows = visibleRows.filter((row) => !row.organizerOnly);

	const isLoading = loading || orgsLoading;

	return (
		// The id is the anchor every email's "change email settings" footer link
		// points at (backend EmailLinkBuilder.NotificationSettings).
		<section
			id="email-notifications"
			className={`mb-6 max-w-3xl ${cardClass} sm:p-6`}
		>
			<PageSectionHeading>
				{t("notificationPreferences.title")}
			</PageSectionHeading>
			<p className="mb-2 text-sm text-gray-600">
				{t("notificationPreferences.description")}
			</p>
			<p className="mb-4 text-sm text-gray-600">
				{t("notificationPreferences.inAppNotice")}
			</p>

			{isLoading && (
				<div className="space-y-2" role="status">
					<span className="sr-only">{t("profile.loading")}</span>
					{PREFERENCE_ROWS.map((row) => (
						<Skeleton key={row.key} className="h-5 w-64" />
					))}
				</div>
			)}

			{!isLoading && loadError && (
				<ErrorBanner message={t("notificationPreferences.loadError")} />
			)}

			{!isLoading &&
				!loadError &&
				preferences &&
				(organizerRowsVisible ? (
					<div className="space-y-4">
						<div>
							<h3 className="mb-3 text-sm font-semibold text-gray-900">
								{t("notificationPreferences.organizerGroupLabel")}
							</h3>
							<PreferenceRowList
								rows={organizerRows}
								preferences={preferences}
								editing={editing}
								onToggle={onToggle}
							/>
						</div>
						<div>
							<h3 className="mb-3 text-sm font-semibold text-gray-900">
								{t("notificationPreferences.volunteerGroupLabel")}
							</h3>
							<PreferenceRowList
								rows={volunteerRows}
								preferences={preferences}
								editing={editing}
								onToggle={onToggle}
							/>
						</div>
					</div>
				) : (
					<PreferenceRowList
						rows={visibleRows}
						preferences={preferences}
						editing={editing}
						onToggle={onToggle}
					/>
				))}
		</section>
	);
}
