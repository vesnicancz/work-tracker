import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { arrangeSuggestions, readSuggestions } from "../src/suggestion-board.ts";

const now = new Date("2026-11-04T12:00:00+01:00");

function event(title, start, end = null) {
	return { title, ticketId: null, startTime: `2026-11-04T${start}:00+01:00`, endTime: end && `2026-11-04T${end}:00+01:00`, source: "O365 Calendar" };
}

function ticket(ticketId) {
	return { title: `Title of ${ticketId}`, ticketId, startTime: null, endTime: null, source: "Jira" };
}

describe("arrangeSuggestions", () => {
	it("puts events by time before tickets, whatever order the plugins answered in", () => {
		const arranged = arrangeSuggestions([ticket("B-1"), event("Late", "15:00", "16:00"), ticket("A-1"), event("Early", "13:00", "14:00")], now);

		assert.deepEqual(arranged.map((s) => s.ticketId ?? s.title), ["Early", "Late", "B-1", "A-1"]);
	});

	it("puts events that are over after those ahead, the most recent first, and before tickets", () => {
		const arranged = arrangeSuggestions([
			ticket("A-1"),
			event("Morning", "08:00", "09:00"),
			event("Running", "11:30", "12:30"),
			event("Recent", "10:00", "11:00")
		], now);

		assert.deepEqual(arranged.map((s) => s.ticketId ?? s.title), ["Running", "Recent", "Morning", "A-1"]);
	});

	it("treats an event without an end as over once it has started", () => {
		const arranged = arrangeSuggestions([event("Started", "11:00"), event("Ahead", "13:00")], now);

		assert.deepEqual(arranged.map((s) => s.title), ["Ahead", "Started"]);
	});
});

describe("readSuggestions", () => {
	it("arranges the items and names failed sources", () => {
		const content = readSuggestions({
			success: true,
			error: null,
			suggestions: { items: [ticket("A-1"), event("Ahead", "13:00")], failedSources: ["O365 Calendar"] }
		}, now);

		assert.deepEqual(content.items.map((s) => s.ticketId ?? s.title), ["Ahead", "A-1"]);
		assert.deepEqual(content.failedSources, ["O365 Calendar"]);
	});

	it("gives nothing for an answer without suggestions", () => {
		assert.equal(readSuggestions({ success: true, error: null }, now), null);
	});
});
