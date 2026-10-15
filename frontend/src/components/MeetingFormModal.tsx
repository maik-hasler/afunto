import { useState } from "react";
import { useTranslation } from "react-i18next";
import { getApiErrorMessage } from "../lib/apiError";
import { inputClass, labelClass, textareaClass } from "../lib/formClasses";
import { parseMeetingTimes, type MeetingTimesError } from "../lib/meetings";
import {
	CANONICAL_TIME_ZONE,
	toZonedDatetimeLocalValue,
} from "../lib/timezone";
import Button from "./Button";
import CharCount from "./CharCount";
import DateTimePicker from "./DateTimePicker";
import ErrorBanner from "./ErrorBanner";
import Modal from "./Modal";
import { RequiredFieldsLegend, RequiredMark } from "./RequiredMark";

// Mirror Meeting.MaxLocationLength / MaxNoteLength (Domain/Engagements/Meeting.cs).
const MAX_LOCATION_LENGTH = 300;
const MAX_NOTE_LENGTH = 500;

function toPickerValue(date: Date | undefined): string {
	return date ? toZonedDatetimeLocalValue(date, CANONICAL_TIME_ZONE) : "";
}

export interface MeetingFormValues {
	startsAt: Date;
	endsAt: Date;
	location: string;
	note: string | null;
}

interface Props {
	title: string;
	/** One sentence on what sending does and who hears about it. */
	intro: string;
	/** Prefills the form, e.g. with the meeting being moved. */
	initialStartsAt?: Date;
	initialEndsAt?: Date;
	initialLocation?: string;
	initialNote?: string | null;
	onSubmit: (values: MeetingFormValues) => Promise<void>;
	onClose: () => void;
}

/**
 * Proposing, moving and answering with another time are the same form for
 * both sides, so the organizer's and the volunteer's version cannot drift
 * apart. A proposal is never edited in place - whatever this sends replaces
 * the current one.
 */
export default function MeetingFormModal({
	title,
	intro,
	initialStartsAt,
	initialEndsAt,
	initialLocation = "",
	initialNote = null,
	onSubmit,
	onClose,
}: Props) {
	const { t } = useTranslation();
	const [start, setStart] = useState(() => toPickerValue(initialStartsAt));
	const [end, setEnd] = useState(() => toPickerValue(initialEndsAt));
	const [location, setLocation] = useState(initialLocation);
	const [note, setNote] = useState(initialNote ?? "");
	const [timesError, setTimesError] = useState<MeetingTimesError | null>(null);
	const [submitting, setSubmitting] = useState(false);
	const [error, setError] = useState<string | null>(null);
	const minValue = toZonedDatetimeLocalValue(new Date(), CANONICAL_TIME_ZONE);

	function handleStartChange(value: string) {
		setStart(value);
		setTimesError(null);
		// Most meetings last an hour; filling the end in saves the second pick
		// without hiding it - it stays editable.
		const date = value.split("T")[0];
		if (!end && /T\d{2}:\d{2}$/.test(value) && date) {
			const [hours, minutes] = value.split("T")[1].split(":").map(Number);
			if (hours < 23)
				setEnd(
					`${date}T${String(hours + 1).padStart(2, "0")}:${String(minutes).padStart(2, "0")}`,
				);
		}
	}

	async function handleSubmit(e: React.FormEvent) {
		e.preventDefault();
		const times = parseMeetingTimes(start, end);
		if (!times.ok) {
			setTimesError(times.error);
			return;
		}
		setSubmitting(true);
		setError(null);
		try {
			await onSubmit({
				startsAt: times.startsAt,
				endsAt: times.endsAt,
				location: location.trim(),
				note: note.trim() || null,
			});
			onClose();
		} catch (err) {
			setError(getApiErrorMessage(err, t("meetings.form.submitError")));
		} finally {
			setSubmitting(false);
		}
	}

	return (
		<Modal
			onClose={onClose}
			labelledBy="meeting-form-title"
			maxWidth="max-w-lg"
			closeDisabled={submitting}
		>
			<h2
				id="meeting-form-title"
				className="mb-1 text-lg font-semibold text-gray-900"
			>
				{title}
			</h2>
			<p className="mb-5 text-sm text-gray-600">{intro}</p>

			<form onSubmit={(e) => void handleSubmit(e)} className="space-y-4">
				<RequiredFieldsLegend />

				<div>
					<label htmlFor="meeting-start" className={labelClass}>
						{t("meetings.form.startLabel")}
						<RequiredMark />
					</label>
					<DateTimePicker
						id="meeting-start"
						label={t("meetings.form.startLabel")}
						value={start}
						onChange={handleStartChange}
						min={minValue}
						aria-invalid={timesError !== null}
						aria-describedby={timesError ? "meeting-times-error" : undefined}
					/>
				</div>
				<div>
					<label htmlFor="meeting-end" className={labelClass}>
						{t("meetings.form.endLabel")}
						<RequiredMark />
					</label>
					<DateTimePicker
						id="meeting-end"
						label={t("meetings.form.endLabel")}
						value={end}
						onChange={(value) => {
							setEnd(value);
							setTimesError(null);
						}}
						min={start || minValue}
						aria-invalid={timesError !== null}
						aria-describedby={timesError ? "meeting-times-error" : undefined}
					/>
					{timesError && (
						<p
							id="meeting-times-error"
							role="alert"
							className="mt-1 text-xs text-red-700"
						>
							{t(`meetings.form.timesError.${timesError}`)}
						</p>
					)}
				</div>

				<div>
					<label htmlFor="meeting-location" className={labelClass}>
						{t("meetings.form.locationLabel")}
						<RequiredMark />
					</label>
					<input
						id="meeting-location"
						type="text"
						required
						maxLength={MAX_LOCATION_LENGTH}
						value={location}
						onChange={(e) => setLocation(e.target.value)}
						aria-describedby="meeting-location-hint"
						className={inputClass}
					/>
					<p id="meeting-location-hint" className="mt-1 text-xs text-gray-500">
						{t("meetings.form.locationHint")}
					</p>
				</div>

				<div>
					<label htmlFor="meeting-note" className={labelClass}>
						{t("meetings.form.noteLabel")}
					</label>
					<textarea
						id="meeting-note"
						rows={3}
						maxLength={MAX_NOTE_LENGTH}
						value={note}
						onChange={(e) => setNote(e.target.value)}
						placeholder={t("meetings.form.notePlaceholder")}
						className={textareaClass}
					/>
					<CharCount current={note.length} max={MAX_NOTE_LENGTH} />
				</div>

				{error && <ErrorBanner message={error} />}

				<div className="flex gap-3">
					<Button
						type="button"
						variant="secondary"
						onClick={onClose}
						disabled={submitting}
						className="flex-1"
					>
						{t("meetings.form.cancel")}
					</Button>
					<Button
						type="submit"
						disabled={submitting || location.trim() === ""}
						className="flex-1"
					>
						{submitting
							? t("meetings.form.submitting")
							: t("meetings.form.submit")}
					</Button>
				</div>
			</form>
		</Modal>
	);
}
