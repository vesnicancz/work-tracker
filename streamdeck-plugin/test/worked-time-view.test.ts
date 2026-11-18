import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { formatHoursMinutes, getWorkedTimeView, togglePeriod } from "../src/worked-time-view.ts";

const pomodoro = { phase: "idle", remainingSeconds: 0, completedPomodoros: 0, pomodorosBeforeLongBreak: 4 } as const;
const receivedAt = Date.parse("2026-11-04T12:00:00+01:00");

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
		assert.equal(togglePeriod("today"), "week");
		assert.equal(togglePeriod("week"), "today");
	});
});

describe("getWorkedTimeView", () => {
	it("shows offline without a connection", () => {
		assert.equal(getWorkedTimeView(null, "today", new Date(receivedAt)).value, "Offline");
	});

	it("shows today against the daily target", () => {
		const view = getWorkedTimeView(stateWith(workedTime), "today", new Date(receivedAt));

		assert.equal(view.title, "Today");
		assert.equal(view.value, "5:42 / 8:00");
		assert.equal(view.progress, 71);
	});

	it("shows the week against the weekly target", () => {
		const view = getWorkedTimeView(stateWith(workedTime), "week", new Date(receivedAt));

		assert.equal(view.title, "This week");
		assert.equal(view.value, "32:15 / 40:00");
		assert.equal(view.progress, 80);
	});

	it("counts on from the state while work is running", () => {
		const view = getWorkedTimeView(stateWith({ ...workedTime, running: true }), "today", new Date(receivedAt + 18 * 60000));

		assert.equal(view.value, "6:00 / 8:00");
	});

	it("does not count on while nothing is running", () => {
		const view = getWorkedTimeView(stateWith(workedTime), "today", new Date(receivedAt + 18 * 60000));

		assert.equal(view.value, "5:42 / 8:00");
	});

	it("differs in color while running", () => {
		const idle = getWorkedTimeView(stateWith(workedTime), "today", new Date(receivedAt));
		const running = getWorkedTimeView(stateWith({ ...workedTime, running: true }), "today", new Date(receivedAt));

		assert.notEqual(running.color, idle.color);
	});

	it("fills the bar and changes its color once the target is reached", () => {
		const below = getWorkedTimeView(stateWith(workedTime), "today", new Date(receivedAt));
		const over = getWorkedTimeView(stateWith({ ...workedTime, todaySeconds: 9 * 3600 }), "today", new Date(receivedAt));

		assert.equal(over.progress, 100);
		assert.notEqual(over.progressColor, below.progressColor);
	});

	it("shows a dash for a WorkTracker that does not send worked time", () => {
		assert.equal(getWorkedTimeView(stateWith(null), "today", new Date(receivedAt)).value, "—");
	});
});
