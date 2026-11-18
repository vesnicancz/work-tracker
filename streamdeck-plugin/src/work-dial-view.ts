import { formatMonthDay, getCalendarDaysBetween, WEEKDAYS } from "./dates.ts";
import type { RemoteState, ScheduledWork, WorkItem } from "./protocol";

// One Dark Pro, same as the application
const TRACKING_COLOR = "#98C379";
const SCHEDULED_COLOR = "#61AFEF";
const IDLE_COLOR = "#ABB2BF";
const OFFLINE_COLOR = "#5C6370";
const EARLIER_COLOR = "#5C6370";

const DAYS_SHOWN_AS_WEEKDAY = 6;

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

	// WorkTracker sends a new state when the scheduled work ends, this only covers the moment until then
	if (state.scheduledWork && Date.parse(state.scheduledWork.endTime) > now.getTime()) {
		return {
			title: getWorkLabel(state.scheduledWork),
			value: formatTimeRange(state.scheduledWork),
			color: SCHEDULED_COLOR
		};
	}

	if (state.lastWork !== null) {
		return { title: getWorkLabel(state.lastWork), ...getLastWorkValue(state.lastWork, now) };
	}

	return { title: "WorkTracker", value: "No work", color: IDLE_COLOR };
}

/**
 * How many whole minutes the start of the work can move later without ending up in the future.
 */
export function getLatestStartShiftMinutes(work: WorkItem, now: Date): number {
	return Math.max(0, Math.floor(getElapsedMilliseconds(work, now) / MILLISECONDS_PER_MINUTE));
}

/**
 * Work from an earlier day says when it ended, dimmed, so that it isn't taken for today's: the weekday
 * within the last week, the date before that, when the same weekday would mean two different days.
 */
function getLastWorkValue(work: WorkItem, now: Date): Pick<WorkDialView, "value" | "color"> {
	if (!work.endedAt) {
		return { value: "Stopped", color: IDLE_COLOR };
	}

	const ended = new Date(work.endedAt);
	const daysAgo = getCalendarDaysBetween(ended, now);
	if (daysAgo <= 0) {
		return { value: "Stopped", color: IDLE_COLOR };
	}

	return {
		value: daysAgo <= DAYS_SHOWN_AS_WEEKDAY
			? `${WEEKDAYS[ended.getDay()]} ${formatTimeOfDay(work.endedAt)}`
			: formatMonthDay(ended),
		color: EARLIER_COLOR
	};
}

function getElapsedMilliseconds(work: WorkItem, now: Date): number {
	return now.getTime() - Date.parse(work.startedAt);
}

function getWorkLabel(work: Pick<WorkItem, "ticketId" | "description">): string {
	return work.ticketId || work.description || "";
}

function formatTimeRange(work: ScheduledWork): string {
	return `${formatTimeOfDay(work.startTime)}–${formatTimeOfDay(work.endTime)}`;
}

function formatTimeOfDay(time: string): string {
	const date = new Date(time);
	return [date.getHours(), date.getMinutes()].map((part) => String(part).padStart(2, "0")).join(":");
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
