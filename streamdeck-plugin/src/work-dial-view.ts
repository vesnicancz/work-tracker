import type { RemoteState, WorkItem } from "./protocol";

// One Dark Pro, same as the application
const TRACKING_COLOR = "#98C379";
const IDLE_COLOR = "#ABB2BF";
const OFFLINE_COLOR = "#5C6370";

const MILLISECONDS_PER_MINUTE = 60000;

export type WorkDialView = {
	title: string;
	value: string;
	color: string;
};

/**
 * What the work dial shows on its part of the touch strip.
 */
export function getWorkDialView(state: RemoteState | null, now: Date, startShiftMinutes: number = 0): WorkDialView {
	if (state === null) {
		return { title: "WorkTracker", value: "Offline", color: OFFLINE_COLOR };
	}

	if (state.activeWork !== null) {
		return {
			title: getWorkLabel(state.activeWork),
			value: formatElapsed(getElapsedMilliseconds(state.activeWork, now) - startShiftMinutes * MILLISECONDS_PER_MINUTE),
			color: TRACKING_COLOR
		};
	}

	if (state.lastWork !== null) {
		return { title: getWorkLabel(state.lastWork), value: "Stopped", color: IDLE_COLOR };
	}

	return { title: "WorkTracker", value: "No work", color: IDLE_COLOR };
}

/**
 * How many whole minutes the start of the work can move later without ending up in the future.
 */
export function getLatestStartShiftMinutes(work: WorkItem, now: Date): number {
	return Math.max(0, Math.floor(getElapsedMilliseconds(work, now) / MILLISECONDS_PER_MINUTE));
}

function getElapsedMilliseconds(work: WorkItem, now: Date): number {
	return now.getTime() - Date.parse(work.startedAt);
}

function getWorkLabel(work: WorkItem): string {
	return work.ticketId || work.description || "";
}

/**
 * The same format as the application. Clamped at zero: stored start times are rounded to the minute,
 * so work started a moment ago can have its start slightly ahead of the clock.
 */
export function formatElapsed(milliseconds: number): string {
	const totalSeconds = Math.max(0, Math.floor(milliseconds / 1000));
	const hours = Math.floor(totalSeconds / 3600);
	const minutes = Math.floor((totalSeconds % 3600) / 60);
	const seconds = totalSeconds % 60;

	return [hours, minutes, seconds].map((part) => String(part).padStart(2, "0")).join(":");
}
