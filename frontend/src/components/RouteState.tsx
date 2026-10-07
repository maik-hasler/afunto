import { useTranslation } from "react-i18next";
import Button from "./Button";
import ErrorBanner from "./ErrorBanner";
import { useNoIndex } from "../hooks/useNoIndex";
import { usePageTitle } from "../hooks/usePageTitle";
import { statusTitleClass } from "../lib/headingClasses";
import {
	ExclamationTriangleIcon,
	LockClosedIcon,
	QuestionMarkCircleIcon,
	SignalSlashIcon,
} from "./icons";

export type RouteStateVariant = "notFound" | "forbidden" | "offline" | "error";

const VARIANT_ICONS = {
	notFound: QuestionMarkCircleIcon,
	forbidden: LockClosedIcon,
	offline: SignalSlashIcon,
	error: ExclamationTriangleIcon,
} as const;

const VARIANT_TONES = {
	notFound: "bg-gray-100 text-gray-600",
	forbidden: "bg-amber-50 text-amber-700",
	offline: "bg-brand-50 text-brand-700",
	error: "bg-red-50 text-red-700",
} as const;

interface Props {
	variant: RouteStateVariant;
	title: string;
	message: string;

	onRetry?: () => void;

	action?: { label: string; to: string };

	inline?: boolean;
	"data-testid"?: string;
}

export default function RouteState({
	variant,
	title,
	message,
	onRetry,
	action,
	inline = false,
	"data-testid": testId,
}: Props) {
	const { t } = useTranslation();
	const Icon = VARIANT_ICONS[variant];
	const canRetry = (variant === "error" || variant === "offline") && !!onRetry;

	usePageTitle(inline ? null : title);
	// A whole route that turned out not to exist (a deleted opportunity, say)
	// is a 404 to a crawler; an inline empty state inside a real page is not.
	useNoIndex(variant === "notFound" && !inline);

	return (
		<div
			data-testid={testId}
			className={
				inline
					? "mt-6 flex flex-col items-center rounded-card border border-dashed border-gray-200 px-4 py-10 text-center"
					: "mx-auto flex max-w-lg flex-col items-center px-4 py-14 text-center sm:py-20"
			}
		>
			<span
				className={`mb-5 flex h-16 w-16 items-center justify-center rounded-full ${VARIANT_TONES[variant]}`}
			>
				<Icon />
			</span>

			{inline ? (
				<p className="text-lg font-semibold text-gray-900">{title}</p>
			) : (
				<h1 className={`text-gray-900 ${statusTitleClass}`}>{title}</h1>
			)}

			{variant === "error" ? (
				<ErrorBanner message={message} className="mt-4 max-w-md" />
			) : (
				<p className="mt-4 max-w-md leading-relaxed text-gray-600">{message}</p>
			)}

			{(canRetry || action) && (
				<div className="mt-8 flex flex-wrap justify-center gap-3">
					{canRetry && <Button onClick={onRetry}>{t("orgApp.retry")}</Button>}
					{action && (
						<Button to={action.to} variant={canRetry ? "secondary" : "primary"}>
							{action.label}
						</Button>
					)}
				</div>
			)}
		</div>
	);
}
