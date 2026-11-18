import { addDays, formatMonthDay, getCalendarDaysBetween, parseDateString, startOfDay, startOfWeek, toDateString, WEEKDAYS } from "./dates.ts";
import type { RemoteState, WorkedTimeSummary } from "./protocol";

// One Dark Pro, same as the application
const RUNNING_COLOR = "#98C379";
const IDLE_COLOR = "#ABB2BF";
const OFFLINE_COLOR = "#5C6370";
const EARLIER_COLOR = "#5C6370";
const PROGRESS_COLOR = "#61AFEF";

/** How far back the dial goes: the day this many days ago, or the week it belongs to. */
export const HISTORY_DAYS = 14;

export type WorkedTimePeriod = "today" | "week";

/**
 * What the dial shows: today or this week when `date` is null, otherwise the day of `date` or the week
 * it belongs to.
 */
export type WorkedTimeSelection = {
	period: WorkedTimePeriod;
	date: string | null;
};

/**
 * The answer for a past day or week: loading until it arrives, unsupported from a WorkTracker that
 * doesn't know the command.
 */
export type WorkedTimeHistory = WorkedTimeSummary | "loading" | "failed" | "unsupported";

export type WorkedTimeView = {
	title: string;
	value: string;
	color: string;
	/** How much of the target is done, 0 to 100. */
	progress: number;
	progressColor: string;
};

/**
 * Switches between the day and the week of the same date. The week that has today in it is this week.
 */
export function togglePeriod(selection: WorkedTimeSelection, now: Date): WorkedTimeSelection {
	if (selection.period === "week") {
		return { period: "today", date: selection.date };
	}

	const isThisWeek = selection.date === null || startOfWeek(parseDateString(selection.date)).getTime() === startOfWeek(now).getTime();
	return { period: "week", date: isThisWeek ? null : selection.date };
}

/**
 * Moves by days or by weeks, negative ticks into the past. Stops at today and at the limit of the history.
 */
export function moveSelection(selection: WorkedTimeSelection, ticks: number, now: Date): WorkedTimeSelection {
	const today = startOfDay(now);
	const current = selection.date === null ? today : parseDateString(selection.date);
	const earliest = addDays(today, -HISTORY_DAYS);

	let target = addDays(current, ticks * (selection.period === "week" ? 7 : 1));
	if (target < earliest) {
		target = earliest;
	}

	const isCurrent = selection.period === "week"
		? startOfWeek(target).getTime() >= startOfWeek(today).getTime()
		: target.getTime() >= today.getTime();

	return { period: selection.period, date: isCurrent ? null : toDateString(target) };
}

export function getHistoryKey(selection: WorkedTimeSelection): string | null {
	return selection.date === null ? null : `${selection.period}:${selection.date}`;
}

/**
 * What the worked time dial shows on its part of the touch strip: the time worked in the period against
 * its target, and a bar that turns green once the target is reached. A past period is dimmed.
 */
export function getWorkedTimeView(state: RemoteState | null, selection: WorkedTimeSelection, now: Date, history?: WorkedTimeHistory): WorkedTimeView {
	if (state === null) {
		return { title: "WorkTracker", value: "Offline", color: OFFLINE_COLOR, progress: 0, progressColor: OFFLINE_COLOR };
	}

	if (selection.date !== null) {
		return getHistoryView(getHistoryTitle(selection.period, parseDateString(selection.date), now), history);
	}

	const isToday = selection.period === "today";
	const title = isToday ? "Today" : "This week";
	const time = state.workedTime;

	// A WorkTracker from before worked time
	if (time === null) {
		return { title, value: "—", color: OFFLINE_COLOR, progress: 0, progressColor: OFFLINE_COLOR };
	}

	const sinceReceived = time.running ? Math.max(0, (now.getTime() - state.receivedAt) / 1000) : 0;
	const worked = (isToday ? time.todaySeconds : time.weekSeconds) + sinceReceived;
	const target = isToday ? time.dailyTargetSeconds : time.weeklyTargetSeconds;

	return { title, ...getProgressView(worked, target, time.running ? RUNNING_COLOR : IDLE_COLOR) };
}

function getHistoryView(title: string, history: WorkedTimeHistory | undefined): WorkedTimeView {
	const empty = { title, color: EARLIER_COLOR, progress: 0, progressColor: EARLIER_COLOR };

	if (history === undefined || history === "loading") {
		return { ...empty, value: "…" };
	}

	if (history === "failed") {
		return { ...empty, value: "Failed" };
	}

	if (history === "unsupported") {
		return { ...empty, value: "—" };
	}

	return { title, ...getProgressView(history.seconds, history.targetSeconds, EARLIER_COLOR) };
}

function getProgressView(worked: number, target: number, color: string): Omit<WorkedTimeView, "title"> {
	return {
		value: `${formatHoursMinutes(worked)} / ${formatHoursMinutes(target)}`,
		color,
		progress: target > 0 ? Math.min(100, Math.floor((worked / target) * 100)) : 0,
		progressColor: target > 0 && worked >= target ? RUNNING_COLOR : PROGRESS_COLOR
	};
}

/**
 * `Yesterday`, `Mon Nov 16`, `Last week`, `Week of Nov 2`.
 */
function getHistoryTitle(period: WorkedTimePeriod, date: Date, now: Date): string {
	if (period === "week") {
		const weekStart = startOfWeek(date);
		return getCalendarDaysBetween(weekStart, startOfWeek(now)) === 7 ? "Last week" : `Week of ${formatMonthDay(weekStart)}`;
	}

	return getCalendarDaysBetween(date, now) === 1 ? "Yesterday" : `${WEEKDAYS[date.getDay()]} ${formatMonthDay(date)}`;
}

/**
 * Whole minutes, so that the value changes once a minute and not on every render.
 */
export function formatHoursMinutes(seconds: number): string {
	const minutes = Math.floor(Math.max(0, seconds) / 60);
	return `${Math.floor(minutes / 60)}:${String(minutes % 60).padStart(2, "0")}`;
}
