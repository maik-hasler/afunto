import { useState } from "react";
import { useTranslation } from "react-i18next";
import Modal from "../Modal";
import Button from "../Button";
import ErrorBanner from "../ErrorBanner";
import { radioClass } from "../../lib/formClasses";
import type { SeriesEditScope } from "./ScheduleStep";

interface Props {
	bookedCount: number;
	loading: boolean;
	error?: string | null;
	onConfirm: (scope: SeriesEditScope) => void;
	onClose: () => void;
}

const SCOPES: SeriesEditScope[] = ["Only", "ThisAndFollowing", "EntireSeries"];

export default function DeleteSeriesSlotDialog({
	bookedCount,
	loading,
	error = null,
	onConfirm,
	onClose,
}: Props) {
	const { t } = useTranslation();
	const [scope, setScope] = useState<SeriesEditScope>("Only");

	return (
		<Modal
			onClose={onClose}
			labelledBy="delete-series-slot-title"
			maxWidth="max-w-sm"
		>
			<h2
				id="delete-series-slot-title"
				className="text-lg font-semibold text-gray-900"
			>
				{t("timeSlots.deleteSeries.title")}
			</h2>
			<p className="mt-2 text-sm text-gray-600">
				{t("timeSlots.deleteSeries.message")}
			</p>

			<fieldset className="mt-4 space-y-2">
				<legend className="mb-1 text-xs font-semibold text-gray-700">
					{t("timeSlots.deleteSeries.scopeLegend")}
				</legend>
				{SCOPES.map((s) => (
					<label
						key={s}
						className="flex items-center gap-2 text-sm text-gray-700"
					>
						<input
							type="radio"
							name="delete-series-scope"
							value={s}
							checked={scope === s}
							onChange={() => setScope(s)}
							className={radioClass}
						/>
						{t(`timeSlots.deleteSeries.scope${s}`)}
					</label>
				))}
			</fieldset>

			{scope !== "Only" && bookedCount > 0 && (
				<p className="mt-3 text-xs text-amber-600">
					{t("timeSlots.deleteSeries.cancelsEngagementsWarning")}
				</p>
			)}

			{error && <ErrorBanner message={error} className="mt-3" />}

			<div className="mt-5 flex justify-end gap-3">
				<Button
					type="button"
					variant="secondary"
					onClick={onClose}
					disabled={loading}
				>
					{t("confirmDialog.keep")}
				</Button>
				<button
					type="button"
					onClick={() => onConfirm(scope)}
					disabled={loading}
					className="rounded bg-red-600 px-4 py-2 text-sm text-white hover:bg-red-700 disabled:opacity-50"
				>
					{loading ? "…" : t("timeSlots.deleteSeries.confirm")}
				</button>
			</div>
		</Modal>
	);
}
