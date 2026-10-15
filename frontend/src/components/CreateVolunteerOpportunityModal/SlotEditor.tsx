import { useEffect, useMemo, useRef } from "react";
import { useTranslation } from "react-i18next";
import Button from "../Button";
import DateTimePicker from "../DateTimePicker";
import Dropdown from "../Dropdown";
import { ChevronLeftIcon } from "../icons";
import {
	checkboxClass,
	inputSurfaceClass,
	radioClass,
} from "../../lib/formClasses";
import {
	CANONICAL_TIME_ZONE,
	toZonedDatetimeLocalValue,
	zonedDatetimeLocalToUtc,
} from "../../lib/timezone";
import type { SeriesEditScope } from "./DetailsStep";
import {
	isCompleteDateTime,
	MAX_PARTICIPANTS_LIMIT,
	overlapsAnySlot,
} from "./timeSlots";
import type { CapacityInput, SlotField, SlotRange } from "./timeSlots";

export const SLOT_EDITOR_TITLE_ID = "time-slot-editor-title";
const SLOT_ERROR_ID = "time-slot-error";
const DATE_TIME_HINT_ID = "slot-datetime-hint";

const SCOPES = ["Only", "ThisAndFollowing", "EntireSeries"] as const;

export interface SlotDraft {
	startDateTime: string;
	endDateTime: string;
	maxParticipants: CapacityInput;
}

interface Recurrence {
	frequency: string;
	onFrequencyChange: (value: string) => void;
	count: number;
	onCountChange: (count: number) => void;
}

interface SeriesScope {
	value: SeriesEditScope;
	onChange: (scope: SeriesEditScope) => void;
}

interface Props {
	mode: "add" | "edit";
	draft: SlotDraft;
	onDraftChange: (draft: SlotDraft) => void;
	/** The other slots on the list, to warn about a double-bookable overlap. */
	otherSlots: readonly SlotRange[];
	/** Only while adding a slot to a recurring opportunity. */
	recurrence?: Recurrence;
	/** Only while editing one occurrence of a series. */
	seriesScope?: SeriesScope;
	bookedCount: number;
	slotChangesAreImmediate: boolean;
	error: string | null;
	invalidFields: ReadonlySet<SlotField>;
	busy: boolean;
	onCancel: () => void;
	onSubmit: () => void;
}

function joinDescribedBy(
	...ids: (string | undefined | false)[]
): string | undefined {
	const present = ids.filter((id): id is string => Boolean(id));
	return present.length > 0 ? present.join(" ") : undefined;
}

/**
 * One time slot at a time, in a view of its own that replaces the step it was
 * opened from. Adding used to happen in a form sitting under the list, whose
 * entries only joined the list once its own "Add" button was pressed - user
 * testing found organizers typed a slot in, pressed Publish and lost it
 * (#2388). A dedicated view with exactly two ways out (add it, or go back)
 * leaves no such in-between state, follows the "new event" sheet of Apple
 * Calendar and Outlook, and gets the full width of a phone screen.
 */
export default function SlotEditor({
	mode,
	draft,
	onDraftChange,
	otherSlots,
	recurrence,
	seriesScope,
	bookedCount,
	slotChangesAreImmediate,
	error,
	invalidFields,
	busy,
	onCancel,
	onSubmit,
}: Props) {
	const { t } = useTranslation();
	const titleRef = useRef<HTMLHeadingElement>(null);

	useEffect(() => {
		titleRef.current?.focus();
	}, []);

	const editsDateTime = !seriesScope || seriesScope.value === "Only";

	const overlapsAnother = useMemo(() => {
		if (
			!editsDateTime ||
			!isCompleteDateTime(draft.startDateTime) ||
			!isCompleteDateTime(draft.endDateTime)
		)
			return false;
		return overlapsAnySlot(
			zonedDatetimeLocalToUtc(
				draft.startDateTime,
				CANONICAL_TIME_ZONE,
			).getTime(),
			zonedDatetimeLocalToUtc(draft.endDateTime, CANONICAL_TIME_ZONE).getTime(),
			otherSlots,
		);
	}, [editsDateTime, draft.startDateTime, draft.endDateTime, otherSlots]);

	const title =
		mode === "add" ? t("timeSlots.addTitle") : t("timeSlots.editTitle");
	const submitLabel = busy
		? mode === "add"
			? t("timeSlots.adding")
			: t("timeSlots.editing")
		: mode === "add"
			? t("timeSlots.addButton")
			: t("timeSlots.saveButton");

	return (
		<div className="flex min-h-0 flex-col" data-testid="time-slot-editor">
			<div className="flex items-center gap-2 border-b border-gray-100 px-4 py-2 sm:px-6">
				<button
					type="button"
					onClick={onCancel}
					aria-label={t("timeSlots.backToList")}
					className="-ml-2 inline-flex min-h-11 min-w-11 items-center justify-center rounded-lg text-gray-600 transition-colors hover:bg-gray-100 hover:text-gray-800"
				>
					<ChevronLeftIcon className="h-5 w-5" />
				</button>
				<h3
					ref={titleRef}
					id={SLOT_EDITOR_TITLE_ID}
					tabIndex={-1}
					className="text-base font-semibold text-gray-900 focus:outline-none"
				>
					{title}
				</h3>
			</div>

			<div className="max-h-[min(70vh,640px)] space-y-4 overflow-y-auto px-6 py-5">
				{slotChangesAreImmediate && (
					<p className="text-xs text-amber-700">
						{t("timeSlots.immediateHint")}
					</p>
				)}
				{bookedCount > 0 && (
					<p className="text-xs text-amber-700">
						{t("timeSlots.bookedCount", { count: bookedCount })}
					</p>
				)}

				{seriesScope && (
					<fieldset>
						<legend className="mb-1 text-xs font-medium text-gray-600">
							{t("timeSlots.editScope.legend")}
						</legend>
						<div className="space-y-1">
							{SCOPES.map((s) => (
								<label
									key={s}
									className="flex min-h-6 items-center gap-2 text-sm text-gray-700"
								>
									<input
										type="radio"
										name="edit-slot-scope"
										value={s}
										checked={seriesScope.value === s}
										onChange={() => seriesScope.onChange(s)}
										className={radioClass}
									/>
									{t(`timeSlots.editScope.${s}`)}
								</label>
							))}
						</div>
					</fieldset>
				)}

				{editsDateTime && (
					<>
						<div>
							<label
								htmlFor="slot-start"
								className="mb-1 block text-xs font-medium text-gray-600"
							>
								{t("timeSlots.fieldStart")}
							</label>
							<DateTimePicker
								id="slot-start"
								label={t("timeSlots.fieldStart")}
								value={draft.startDateTime}
								min={
									mode === "add"
										? toZonedDatetimeLocalValue(new Date(), CANONICAL_TIME_ZONE)
										: undefined
								}
								onChange={(value) =>
									onDraftChange({ ...draft, startDateTime: value })
								}
								aria-invalid={invalidFields.has("start")}
								aria-describedby={joinDescribedBy(
									DATE_TIME_HINT_ID,
									invalidFields.has("start") && SLOT_ERROR_ID,
								)}
							/>
						</div>
						<div>
							<label
								htmlFor="slot-end"
								className="mb-1 block text-xs font-medium text-gray-600"
							>
								{t("timeSlots.fieldEnd")}
							</label>
							<DateTimePicker
								id="slot-end"
								label={t("timeSlots.fieldEnd")}
								value={draft.endDateTime}
								onChange={(value) =>
									onDraftChange({ ...draft, endDateTime: value })
								}
								aria-invalid={invalidFields.has("end")}
								aria-describedby={joinDescribedBy(
									DATE_TIME_HINT_ID,
									invalidFields.has("end") && SLOT_ERROR_ID,
								)}
							/>
						</div>
						<p id={DATE_TIME_HINT_ID} className="text-xs text-gray-500">
							{t("timeSlots.dateTimeHint")}
						</p>
					</>
				)}

				<div>
					<label
						htmlFor="slot-max"
						className="mb-1 block text-xs font-medium text-gray-600"
					>
						{t("timeSlots.fieldMaxParticipants")}
					</label>
					<div className="flex items-center gap-3">
						<input
							id="slot-max"
							type="number"
							inputMode="numeric"
							min={1}
							max={MAX_PARTICIPANTS_LIMIT}
							disabled={draft.maxParticipants === null}
							aria-invalid={invalidFields.has("max") || undefined}
							aria-describedby={
								invalidFields.has("max") ? SLOT_ERROR_ID : undefined
							}
							value={draft.maxParticipants ?? ""}
							onChange={(e) =>
								onDraftChange({ ...draft, maxParticipants: e.target.value })
							}
							className="w-24 rounded-xl border border-gray-200 bg-white px-3 py-2 text-sm shadow-sm transition focus:border-brand-400 disabled:bg-gray-100 disabled:text-gray-400"
						/>
						<label
							htmlFor="slot-unlimited"
							className="flex min-h-6 items-center gap-1.5 text-sm text-gray-700"
						>
							<input
								id="slot-unlimited"
								type="checkbox"
								checked={draft.maxParticipants === null}
								onChange={(e) =>
									onDraftChange({
										...draft,
										maxParticipants: e.target.checked ? null : "1",
									})
								}
								className={`h-4 w-4 ${checkboxClass}`}
							/>
							{t("timeSlots.unlimited")}
						</label>
					</div>
				</div>

				{recurrence && (
					<div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
						<div>
							<label
								htmlFor="slot-recurrence-frequency"
								className="mb-1 block text-xs font-medium text-gray-600"
							>
								{t("timeSlots.recurrenceFrequency")}
							</label>
							<Dropdown
								id="slot-recurrence-frequency"
								value={recurrence.frequency}
								onChange={recurrence.onFrequencyChange}
								className={inputSurfaceClass}
								options={[
									{ value: "Weekly", label: t("timeSlots.recurrenceWeekly") },
									{
										value: "Monthly",
										label: t("timeSlots.recurrenceMonthly"),
									},
								]}
							/>
						</div>
						<div>
							<label
								htmlFor="slot-recurrence-count"
								className="mb-1 block text-xs font-medium text-gray-600"
							>
								{t("timeSlots.recurrenceCount")}
							</label>
							<input
								id="slot-recurrence-count"
								type="number"
								inputMode="numeric"
								min={1}
								max={52}
								value={recurrence.count}
								onChange={(e) =>
									recurrence.onCountChange(
										Math.max(
											1,
											Math.min(52, parseInt(e.target.value, 10) || 1),
										),
									)
								}
								className={inputSurfaceClass}
							/>
						</div>
					</div>
				)}

				{overlapsAnother && (
					<p className="text-xs text-amber-700" role="status">
						{t("timeSlots.overlapHint")}
					</p>
				)}
				{error && (
					<p id={SLOT_ERROR_ID} className="text-sm text-red-600" role="alert">
						{error}
					</p>
				)}
			</div>

			<div className="flex flex-col-reverse gap-3 border-t border-gray-100 bg-gray-50 px-4 py-4 sm:flex-row sm:justify-end sm:px-6">
				<Button type="button" variant="secondary" onClick={onCancel}>
					{t("timeSlots.cancelEditButton")}
				</Button>
				<Button
					type="button"
					disabled={busy}
					onClick={onSubmit}
					data-testid="slot-editor-submit"
				>
					{submitLabel}
				</Button>
			</div>
		</div>
	);
}
