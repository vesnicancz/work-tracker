import type { RemoteState } from "./protocol";

// One Dark Pro, same as the application
const RUNNING_COLOR = "#98C379";
const IDLE_COLOR = "#ABB2BF";
const OFFLINE_COLOR = "#5C6370";
const PROGRESS_COLOR = "#61AFEF";

export type WorkedTimePeriod = "today" | "week";

export type WorkedTimeView = {
	title: string;
	value: string;
	color: string;
	/** How much of the target is done, 0 to 100. */
	progress: number;
	progressColor: string;
};

export function togglePeriod(period: WorkedTimePeriod): WorkedTimePeriod {
	return period === "today" ? "week" : "today";
}

/**
 * What the worked time dial shows on its part of the touch strip: the time worked in the period against
 * its target, and a bar that turns green once the target is reached.
 */
export function getWorkedTimeView(state: RemoteState | null, period: WorkedTimePeriod, now: Date): WorkedTimeView {
	if (state === null) {
		return { title: "WorkTracker", value: "Offline", color: OFFLINE_COLOR, progress: 0, progressColor: OFFLINE_COLOR };
	}

	const title = period === "today" ? "Today" : "This week";
	const time = state.workedTime;

	// A WorkTracker from before worked time
	if (time === null) {
		return { title, value: "—", color: OFFLINE_COLOR, progress: 0, progressColor: OFFLINE_COLOR };
	}

	const sinceReceived = time.running ? Math.max(0, (now.getTime() - state.receivedAt) / 1000) : 0;
	const worked = (period === "today" ? time.todaySeconds : time.weekSeconds) + sinceReceived;
	const target = period === "today" ? time.dailyTargetSeconds : time.weeklyTargetSeconds;

	return {
		title,
		value: `${formatHoursMinutes(worked)} / ${formatHoursMinutes(target)}`,
		color: time.running ? RUNNING_COLOR : IDLE_COLOR,
		progress: target > 0 ? Math.min(100, Math.floor((worked / target) * 100)) : 0,
		progressColor: target > 0 && worked >= target ? RUNNING_COLOR : PROGRESS_COLOR
	};
}

/**
 * Whole minutes, so that the value changes once a minute and not on every render.
 */
export function formatHoursMinutes(seconds: number): string {
	const minutes = Math.floor(Math.max(0, seconds) / 60);
	return `${Math.floor(minutes / 60)}:${String(minutes % 60).padStart(2, "0")}`;
}
