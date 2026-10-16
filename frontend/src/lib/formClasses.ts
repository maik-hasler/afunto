// The one border/focus recipe every form control's invalid state must share
// (#2239) - a single source of truth so a red border on error never drifts
// between the wizard's floating fields and every other form's fields.
export function fieldBorderClass(hasError: boolean): string {
	return hasError
		? "border-red-300 focus:border-red-400"
		: "border-gray-200 focus:border-brand-400";
}

export function getInputSurfaceClass(hasError = false): string {
	return `min-h-10 w-full rounded-xl border ${fieldBorderClass(hasError)} bg-white px-3 py-2 text-sm shadow-sm transition`;
}

export function getInputClass(hasError = false): string {
	return `mt-1 block ${getInputSurfaceClass(hasError)} text-gray-900`;
}

export function getTextareaClass(hasError = false): string {
	return `${getInputClass(hasError)} resize-y`;
}

export function getLabelClass(hasError = false): string {
	return hasError
		? "block text-xs font-medium text-red-600"
		: "block text-xs font-medium text-gray-600";
}

export const inputSurfaceClass = getInputSurfaceClass();

export const inputClass = getInputClass();

export const textareaClass = getTextareaClass();

// No background-image chevron here (#2225) - the deployed CSP's img-src has no
// `data:`, which silently strips a CSS-painted arrow while appearance-none has
// already removed the native one. `components/Select.tsx` draws the chevron as
// an inline <svg> instead, so every consumer should render through that
// component rather than applying this class to a bare <select>.
export const selectClass = `${inputClass} appearance-none pr-9`;

export const labelClass = getLabelClass();

// The pill-shaped, borderless search field inside a translucent hero band -
// HomePage's keyword field and location field, OpportunitiesPage's keyword
// field, and OrganizationsPage's search field each hand-rolled this same
// intent independently, and the right padding had already drifted three ways
// (pr-3/pr-4/pr-8) with nothing to notice. One recipe, so a fourth hero search
// field reuses it instead of typing a fourth variant.
export const heroSearchInputClass =
	"w-full rounded-full border-0 bg-transparent py-3 pr-4 pl-10 text-sm text-gray-900 placeholder:text-gray-600 focus:outline-none";

// A native checkbox takes its checked fill from `accent-color`, and from
// nothing else: `text-brand-*` sets a text colour the control never reads, so
// four of the eight checkboxes in the app painted the browser default blue
// against the green brand while the other four painted brand green (#2329 F8).
// `border-*` is just as inert on a control the UA paints itself. One class, so
// the two treatments cannot drift apart again.
export const checkboxClass = "shrink-0 accent-brand-600";

// The same fix as `checkboxClass`, for `type="radio"`: a native radio also
// takes its checked fill from `accent-color` alone, so a radio group left
// without this class renders the browser/OS default (blue on most platforms)
// instead of brand green. Not needed on a radio that is itself `sr-only` and
// styled entirely through its wrapping `<label>` (see `CreateVolunteerOpportunityModal/shared.tsx`'s
// `ChoiceGroup`) - there the native dot is never painted at all.
export const radioClass = "shrink-0 accent-brand-600";
