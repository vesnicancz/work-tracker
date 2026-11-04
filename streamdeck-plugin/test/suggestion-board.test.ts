import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { arrangeSuggestions, SuggestionBoard } from "../src/suggestion-board.ts";

const now = new Date("2026-11-04T12:00:00+01:00");

function event(title, start, end = null) {
	return { title, ticketId: null, startTime: `2026-11-04T${start}:00+01:00`, endTime: end && `2026-11-04T${end}:00+01:00`, source: "O365 Calendar" };
}

function ticket(ticketId) {
	return { title: `Title of ${ticketId}`, ticketId, startTime: null, endTime: null, source: "Jira" };
}

function createBoard(items, failedSources = []) {
	return new SuggestionBoard(async () => ({ success: true, error: null, suggestions: { items, failedSources } }));
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

describe("SuggestionBoard", () => {
	it("spreads items over pages of the size of the key count", async () => {
		const board = createBoard([ticket("A-1"), ticket("A-2"), ticket("A-3"), ticket("A-4"), ticket("A-5")]);
		board.setPageSize(2);
		await board.load(now);

		assert.equal(board.pageCount, 3);
		assert.equal(board.itemAt(1).ticketId, "A-2");

		board.nextPage();
		assert.equal(board.itemAt(0).ticketId, "A-3");

		board.nextPage();
		assert.equal(board.itemAt(0).ticketId, "A-5");
		assert.equal(board.itemAt(1), null);
	});

	it("wraps around after the last page", async () => {
		const board = createBoard([ticket("A-1"), ticket("A-2")]);
		board.setPageSize(1);
		await board.load(now);

		board.nextPage();
		board.nextPage();

		assert.equal(board.page, 0);
	});

	it("keeps the page valid when the keys get fewer pages to fill", async () => {
		const board = createBoard([ticket("A-1"), ticket("A-2"), ticket("A-3")]);
		board.setPageSize(1);
		await board.load(now);
		board.nextPage();
		board.nextPage();

		board.setPageSize(3);

		assert.equal(board.page, 0);
		assert.equal(board.itemAt(2).ticketId, "A-3");
	});

	it("starts from the first page on every load", async () => {
		const board = createBoard([ticket("A-1"), ticket("A-2")]);
		board.setPageSize(1);
		await board.load(now);
		board.nextPage();

		await board.load(now);

		assert.equal(board.page, 0);
	});

	it("names failed sources while keeping the rest", async () => {
		const board = createBoard([ticket("A-1")], ["O365 Calendar"]);
		await board.load(now);

		assert.equal(board.status, "ready");
		assert.deepEqual(board.failedSources, ["O365 Calendar"]);
		assert.equal(board.itemCount, 1);
	});

	it("reports a failed load and shows nothing", async () => {
		const board = new SuggestionBoard(async () => ({ success: false, error: "Not connected." }));
		await board.load(now);

		assert.equal(board.status, "failed");
		assert.equal(board.itemAt(0), null);
	});

	it("ignores an answer that a newer load has overtaken", async () => {
		const answers = [];
		const board = new SuggestionBoard(() => new Promise((resolve) => answers.push(resolve)));
		const answer = (items) => ({ success: true, error: null, suggestions: { items, failedSources: [] } });

		const first = board.load(now);
		const second = board.load(now);
		answers[1](answer([ticket("NEW-1")]));
		await second;
		answers[0](answer([ticket("OLD-1")]));
		await first;

		assert.equal(board.itemAt(0).ticketId, "NEW-1");
	});

	it("announces loading and the result", async () => {
		const statuses = [];
		const board = createBoard([]);
		board.onChanged(() => statuses.push(board.status));

		await board.load(now);

		assert.deepEqual(statuses, ["loading", "ready"]);
	});
});
