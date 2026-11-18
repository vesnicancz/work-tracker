import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { formatElapsed, getLatestStartShiftMinutes, getWorkDialView } from "../src/work-dial-view.ts";

const pomodoro = { phase: "idle", remainingSeconds: 0, completedPomodoros: 0, pomodorosBeforeLongBreak: 4 } as const;
const now = new Date("2026-11-04T12:00:00+01:00");

describe("formatElapsed", () => {
	it("formats hours, minutes and seconds", () => {
		assert.equal(formatElapsed((2 * 3600 + 5 * 60 + 9) * 1000), "02:05:09");
	});

	it("does not wrap past a day", () => {
		assert.equal(formatElapsed(100 * 3600 * 1000), "100:00:00");
	});

	it("clamps a start ahead of the clock to zero", () => {
		assert.equal(formatElapsed(-30000), "00:00:00");
	});
});

describe("getLatestStartShiftMinutes", () => {
	const startedAt = (time) => ({ ticketId: "PROJ-1", description: null, startedAt: `2026-11-04T${time}+01:00` });

	it("rounds down to whole minutes", () => {
		assert.equal(getLatestStartShiftMinutes(startedAt("11:57:30"), now), 2);
	});

	it("is zero for a start ahead of the clock", () => {
		assert.equal(getLatestStartShiftMinutes(startedAt("12:01:00"), now), 0);
	});
});

describe("getWorkDialView", () => {
	it("shows offline without a connection", () => {
		assert.equal(getWorkDialView(null, now).value, "Offline");
	});

	it("shows the ticket and running time of active work", () => {
		const view = getWorkDialView({
			activeWork: { ticketId: "PROJ-1", description: "Coding", startedAt: "2026-11-04T11:30:00+01:00" },
			lastWork: null,
			pomodoro
		}, now);

		assert.equal(view.title, "PROJ-1");
		assert.equal(view.value, "00:30:00");
	});

	it("adds an unconfirmed start correction to the running time", () => {
		const state = {
			activeWork: { ticketId: "PROJ-1", description: null, startedAt: "2026-11-04T11:30:00+01:00" },
			lastWork: null,
			pomodoro
		};

		assert.equal(getWorkDialView(state, now, -5).value, "00:35:00");
		assert.equal(getWorkDialView(state, now, 5).value, "00:25:00");
	});

	it("falls back to the description for work without a ticket", () => {
		const view = getWorkDialView({
			activeWork: { ticketId: null, description: "Meeting", startedAt: "2026-11-04T11:30:00+01:00" },
			lastWork: null,
			pomodoro
		}, now);

		assert.equal(view.title, "Meeting");
	});

	it("offers the last work when idle", () => {
		const view = getWorkDialView({
			activeWork: null,
			lastWork: { ticketId: "PROJ-2", description: null, startedAt: "2026-11-04T09:00:00+01:00" },
			pomodoro
		}, now);

		assert.equal(view.title, "PROJ-2");
		assert.equal(view.value, "Stopped");
	});

	describe("during scheduled work", () => {
		// Local times, so that the expected text doesn't depend on the time zone of the machine
		const meeting = {
			ticketId: null,
			description: "Standup",
			startTime: new Date(2026, 10, 4, 10, 0).toISOString(),
			endTime: new Date(2026, 10, 4, 10, 30).toISOString()
		};
		const state = {
			activeWork: null,
			scheduledWork: meeting,
			lastWork: { ticketId: "PROJ-2", description: null, startedAt: "2026-11-04T09:00:00+01:00" },
			pomodoro
		};

		it("shows its name and time range instead of the last work", () => {
			const view = getWorkDialView(state, new Date(2026, 10, 4, 10, 15));

			assert.equal(view.title, "Standup");
			assert.equal(view.value, "10:00–10:30");
			assert.notEqual(view.color, getWorkDialView({ ...state, scheduledWork: null }, now).color);
		});

		it("goes back to the last work once it is over, before a new state arrives", () => {
			const view = getWorkDialView(state, new Date(2026, 10, 4, 10, 30));

			assert.equal(view.title, "PROJ-2");
			assert.equal(view.value, "Stopped");
		});
	});

	it("says so when there is no work at all", () => {
		assert.equal(getWorkDialView({ activeWork: null, lastWork: null, pomodoro }, now).value, "No work");
	});
});
