import { useState } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { useTranslation } from "react-i18next";
import { usePageTitle } from "../hooks/usePageTitle";
import { useApiClient } from "../hooks/useApiClient";
import { getApiErrorMessage } from "../lib/apiError";
import Button from "../components/Button";
import ErrorBanner from "../components/ErrorBanner";
import { EnvelopeIcon } from "../components/icons";
import { statusTitleClass } from "../lib/headingClasses";
import { cardClass } from "../lib/surfaceClasses";

const TYPE_LABEL_KEYS: Record<string, string> = {
	NewSignUp: "notificationPreferences.newSignUp",
	Withdrawal: "notificationPreferences.withdrawal",
	EngagementConfirmed: "notificationPreferences.engagementConfirmed",
	EngagementReminder: "notificationPreferences.engagementReminder",
};

// Both ids in the link are GUIDs the API will reject out of hand if they are
// not - checking their shape here keeps a mangled link on the app's own
// invalid-link state instead of spending a request to learn the same thing.
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export default function UnsubscribeConfirmPage() {
	const { t } = useTranslation();
	const api = useApiClient();
	const navigate = useNavigate();
	const [searchParams] = useSearchParams();

	const [submitting, setSubmitting] = useState(false);
	const [error, setError] = useState<string | null>(null);

	const userId = searchParams.get("userId");
	const type = searchParams.get("type");
	const token = searchParams.get("token");

	// `type` is checked against the real notification types rather than
	// echoed into the copy: an unvalidated value both said something the app
	// could not act on and broke the page's width when it was long (#2320).
	const isValid = Boolean(
		userId &&
		GUID.test(userId) &&
		token &&
		GUID.test(token) &&
		type &&
		TYPE_LABEL_KEYS[type],
	);
	const typeLabel =
		type && TYPE_LABEL_KEYS[type] ? t(TYPE_LABEL_KEYS[type]) : "";

	usePageTitle(
		isValid
			? t("unsubscribeConfirm.title")
			: t("unsubscribeConfirm.invalidTitle"),
	);

	async function handleConfirm() {
		if (!isValid || !userId || !type || !token) return;
		setSubmitting(true);
		setError(null);
		try {
			await api.unsubscribe({ path: { userId }, query: { type, token } });
			void navigate("/unsubscribed", { replace: true });
		} catch (err) {
			setError(getApiErrorMessage(err, t("unsubscribeConfirm.failed")));
			setSubmitting(false);
		}
	}

	return (
		<div className="mx-auto max-w-md py-10 sm:py-16">
			<div className={`${cardClass} text-center sm:p-8`}>
				<div
					aria-hidden="true"
					className="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-brand-100 text-brand-700"
				>
					<EnvelopeIcon className="h-7 w-7" />
				</div>
				<h1 className={`mt-6 text-gray-900 ${statusTitleClass}`}>
					{isValid
						? t("unsubscribeConfirm.title")
						: t("unsubscribeConfirm.invalidTitle")}
				</h1>
				{isValid ? (
					<>
						<p className="mt-4 leading-relaxed text-gray-700">
							{t("unsubscribeConfirm.description", { type: typeLabel })}
						</p>
						{error && (
							<ErrorBanner message={error} className="mt-6 text-left" />
						)}
						<Button
							onClick={handleConfirm}
							disabled={submitting}
							size="lg"
							className="mt-8"
						>
							{submitting
								? t("unsubscribeConfirm.confirming")
								: t("unsubscribeConfirm.confirm")}
						</Button>
					</>
				) : (
					<>
						<p className="mt-4 leading-relaxed text-gray-700">
							{t("unsubscribeConfirm.invalidLink")}
						</p>
						<div className="mt-8 flex flex-wrap items-center justify-center gap-3">
							<Button to="/profile" size="lg">
								{t("unsubscribe.manageInProfile")}
							</Button>
							<Button to="/" variant="secondary" size="lg">
								{t("notFound.backHome")}
							</Button>
						</div>
					</>
				)}
			</div>
		</div>
	);
}
