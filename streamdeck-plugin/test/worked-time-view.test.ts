import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { formatHoursMinutes, getHistoryKey, getWorkedTimeView, moveSelection, togglePeriod } from "../src/worked-time-view.ts";

const pomodoro = { phase: "idle", remainingSeconds: 0, completedPomodoros: 0, pomodorosBeforeLongBreak: 4 } as const;

// Local times, so that the expected text doesn't depend on the time zone of the machine. A Wednesday.
const now = new Date(2026, 10, 18, 12, 0);
const receivedAt = now.getTime();

const today = { period: "today", date: null } as const;
const thisWeek = { period: "week", date: null } as const;
const day = (date) => ({ period: "today", date });
const week = (date) => ({ period: "week", date });

function stateWith(workedTime) {
	return { activeWork: null, scheduledWork: null, lastWork: null, workedTime, pomodoro, receivedAt };
}

const workedTime = {
	todaySeconds: 5 * 3600 + 42 * 60,
	weekSeconds: 32 * 3600 + 15 * 60,
	running: false,
	dailyTargetSeconds: 8 * 3600,
	weeklyTargetSeconds: 40 * 3600
};

describe("formatHoursMinutes", () => {
	it("shows hours and whole minutes", () => {
		assert.equal(formatHoursMinutes(5 * 3600 + 42 * 60 + 59), "5:42");
	});

	it("does not wrap past a day", () => {
		assert.equal(formatHoursMinutes(40 * 3600), "40:00");
	});

	it("clamps below zero", () => {
		assert.equal(formatHoursMinutes(-30), "0:00");
	});
});

describe("togglePeriod", () => {
	it("switches between today and this week", () => {
		assert.deepEqual(togglePeriod(today, now), thisWeek);
		assert.deepEqual(togglePeriod(thisWeek, now), today);
	});

	it("goes from a past day to its week and back", () => {
		assert.deepEqual(togglePeriod(day("2026-11-10"), now), week("2026-11-10"));
		assert.deepEqual(togglePeriod(week("2026-11-10"), now), day("2026-11-10"));
	});

	it("goes from a past day of this week to this week", () => {
		assert.deepEqual(togglePeriod(day("2026-11-16"), now), thisWeek);
	});
});

describe("moveSelection", () => {
	it("goes back a day per tick", () => {
		assert.deepEqual(moveSelection(today, -1, now), day("2026-11-17"));
		assert.deepEqual(moveSelection(day("2026-11-17"), -2, now), day("2026-11-15"));
	});

	it("goes back a week per tick", () => {
		assert.deepEqual(moveSelection(thisWeek, -1, now), week("2026-11-11"));
	});

	it("stops at today", () => {
		assert.deepEqual(moveSelection(day("2026-11-17"), 1, now), today);
		assert.deepEqual(moveSelection(day("2026-11-17"), 5, now), today);
		assert.deepEqual(moveSelection(today, 1, now), today);
		assert.deepEqual(moveSelection(week("2026-11-11"), 3, now), thisWeek);
	});

	it("stops fourteen days back, and at the week of that day", () => {
		assert.deepEqual(moveSelection(today, -20, now), day("2026-11-04"));
		assert.deepEqual(moveSelection(thisWeek, -5, now), week("2026-11-04"));
	});
});

describe("getHistoryKey", () => {
	it("is null for today and this week", () => {
		assert.equal(getHistoryKey(today), null);
		assert.equal(getHistoryKey(thisWeek), null);
	});

	it("tells a day from its week", () => {
		assert.notEqual(getHistoryKey(day("2026-11-10")), getHistoryKey(week("2026-11-10")));
	});
});

describe("getWorkedTimeView", () => {
	it("shows offline without a connection", () => {
		assert.equal(getWorkedTimeView(null, today, now).value, "Offline");
		assert.equal(getWorkedTimeView(null, day("2026-11-17"), now).value, "Offline");
	});

	it("shows today against the daily target", () => {
		const view = getWorkedTimeView(stateWith(workedTime), today, now);

		assert.equal(view.title, "Today");
		assert.equal(view.value, "5:42 / 8:00");
		assert.equal(view.progress, 71);
	});

	it("shows the week against the weekly target", () => {
		const view = getWorkedTimeView(stateWith(workedTime), thisWeek, now);

		assert.equal(view.title, "This week");
		assert.equal(view.value, "32:15 / 40:00");
		assert.equal(view.progress, 80);
	});

	it("counts on from the state while work is running", () => {
		const view = getWorkedTimeView(stateWith({ ...workedTime, running: true }), today, new Date(receivedAt + 18 * 60000));

		assert.equal(view.value, "6:00 / 8:00");
	});

	it("does not count on while nothing is running", () => {
		const view = getWorkedTimeView(stateWith(workedTime), today, new Date(receivedAt + 18 * 60000));

		assert.equal(view.value, "5:42 / 8:00");
	});

	it("differs in color while running", () => {
		const idle = getWorkedTimeView(stateWith(workedTime), today, now);
		const running = getWorkedTimeView(stateWith({ ...workedTime, running: true }), today, now);

		assert.notEqual(running.color, idle.color);
	});

	it("fills the bar and changes its color once the target is reached", () => {
		const below = getWorkedTimeView(stateWith(workedTime), today, now);
		const over = getWorkedTimeView(stateWith({ ...workedTime, todaySeconds: 9 * 3600 }), today, now);

		assert.equal(over.progress, 100);
		assert.notEqual(over.progressColor, below.progressColor);
	});

	it("shows a dash for a WorkTracker that does not send worked time", () => {
		assert.equal(getWorkedTimeView(stateWith(null), today, now).value, "—");
	});

	describe("for a past day or week", () => {
		const summary = { firstDay: "2026-11-16", lastDay: "2026-11-16", seconds: 6 * 3600 + 12 * 60, targetSeconds: 8 * 3600 };

		it("names it", () => {
			const title = (selection) => getWorkedTimeView(stateWith(workedTime), selection, now, summary).title;

			assert.equal(title(day("2026-11-17")), "Yesterday");
			assert.equal(title(day("2026-11-16")), "Mon Nov 16");
			assert.equal(title(week("2026-11-11")), "Last week");
			assert.equal(title(week("2026-11-04")), "Week of Nov 2");
		});

		it("shows the answer against its target, dimmed", () => {
			const past = getWorkedTimeView(stateWith(workedTime), day("2026-11-16"), now, summary);
			const current = getWorkedTimeView(stateWith(workedTime), today, now);

			assert.equal(past.value, "6:12 / 8:00");
			assert.equal(past.progress, 77);
			assert.notEqual(past.color, current.color);
		});

		it("says when it is loading, failed or not supported", () => {
			const value = (history) => getWorkedTimeView(stateWith(workedTime), day("2026-11-16"), now, history).value;

			assert.equal(value(undefined), "…");
			assert.equal(value("loading"), "…");
			assert.equal(value("failed"), "Failed");
			assert.equal(value("unsupported"), "—");
		});
	});
});
