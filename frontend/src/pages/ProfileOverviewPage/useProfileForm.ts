import { useReducer, useRef } from "react";
import type { MyProfileResponse } from "../../client";

export type ContactPref = "Email" | "Phone" | "";

interface FormState {
	firstName: string;
	lastName: string;
	bio: string;
	phone: string;
	skills: string[];
	languages: string[];
	preferredContact: ContactPref;
	skillInput: string;
	langInput: string;
}

type FormAction =
	| { type: "reset"; profile: MyProfileResponse | null }
	| { type: "setFirstName"; value: string }
	| { type: "setLastName"; value: string }
	| { type: "setBio"; value: string }
	| { type: "setPhone"; value: string }
	| { type: "setPreferredContact"; value: ContactPref }
	| { type: "setSkillInput"; value: string }
	| { type: "setLangInput"; value: string }
	| { type: "addSkill"; value: string }
	| { type: "removeSkill"; value: string }
	| { type: "addLanguage"; value: string }
	| { type: "removeLanguage"; value: string };

function emptyState(): FormState {
	return {
		firstName: "",
		lastName: "",
		bio: "",
		phone: "",
		skills: [],
		languages: [],
		preferredContact: "",
		skillInput: "",
		langInput: "",
	};
}

function fromProfile(profile: MyProfileResponse | null): FormState {
	if (!profile) return emptyState();
	const pref = profile.preferredContact;
	return {
		firstName: profile.firstName ?? "",
		lastName: profile.lastName ?? "",
		bio: profile.bio ?? "",
		phone: profile.phone ?? "",
		skills: profile.skills ?? [],
		languages: profile.languages ?? [],
		preferredContact: pref === "Email" || pref === "Phone" ? pref : "",
		skillInput: "",
		langInput: "",
	};
}

function addChip(list: string[], value: string): string[] {
	const trimmed = value.trim();
	if (trimmed && !list.includes(trimmed)) return [...list, trimmed];
	return list;
}

function reducer(state: FormState, action: FormAction): FormState {
	switch (action.type) {
		case "reset":
			return fromProfile(action.profile);
		case "setFirstName":
			return { ...state, firstName: action.value };
		case "setLastName":
			return { ...state, lastName: action.value };
		case "setBio":
			return { ...state, bio: action.value };
		case "setPhone":
			return { ...state, phone: action.value };
		case "setPreferredContact":
			return { ...state, preferredContact: action.value };
		case "setSkillInput":
			return { ...state, skillInput: action.value };
		case "setLangInput":
			return { ...state, langInput: action.value };
		case "addSkill":
			return {
				...state,
				skills: addChip(state.skills, action.value),
				skillInput: "",
			};
		case "removeSkill":
			return {
				...state,
				skills: state.skills.filter((s) => s !== action.value),
			};
		case "addLanguage":
			return {
				...state,
				languages: addChip(state.languages, action.value),
				langInput: "",
			};
		case "removeLanguage":
			return {
				...state,
				languages: state.languages.filter((s) => s !== action.value),
			};
	}
}

export function useProfileForm(profile: MyProfileResponse | null) {
	const [state, dispatch] = useReducer(reducer, profile, fromProfile);
	const skillInputRef = useRef<HTMLInputElement>(null);
	const langInputRef = useRef<HTMLInputElement>(null);

	return {
		state,
		skillInputRef,
		langInputRef,
		reset: (profile: MyProfileResponse | null) =>
			dispatch({ type: "reset", profile }),
		setFirstName: (value: string) => dispatch({ type: "setFirstName", value }),
		setLastName: (value: string) => dispatch({ type: "setLastName", value }),
		setBio: (value: string) => dispatch({ type: "setBio", value }),
		setPhone: (value: string) => dispatch({ type: "setPhone", value }),
		setPreferredContact: (value: ContactPref) =>
			dispatch({ type: "setPreferredContact", value }),
		setSkillInput: (value: string) =>
			dispatch({ type: "setSkillInput", value }),
		setLangInput: (value: string) => dispatch({ type: "setLangInput", value }),
		addSkill: (value: string) => dispatch({ type: "addSkill", value }),
		removeSkill: (value: string) => dispatch({ type: "removeSkill", value }),
		addLanguage: (value: string) => dispatch({ type: "addLanguage", value }),
		removeLanguage: (value: string) =>
			dispatch({ type: "removeLanguage", value }),
	};
}
