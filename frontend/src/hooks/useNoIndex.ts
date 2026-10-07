import { useEffect } from "react";
import { registerNoIndex } from "../lib/documentMeta";

export function useNoIndex(active = true) {
	useEffect(() => {
		if (!active) return;
		return registerNoIndex();
	}, [active]);
}
