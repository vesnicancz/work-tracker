import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { KeyBoard } from "../src/key-board.ts";
import { getFavoriteKeyView, getNextKeyView, getSuggestionKeyView, isActiveFavorite, isActiveSuggestion, renderKeyImage, wrapText } from "../src/key-view.ts";
import { readSuggestions } from "../src/suggestion-board.ts";

const ticket = { title: "Fix login", ticketId: "PROJ-1", startTime: null, endTime: null, source: "Jira" };
const event = { title: "Standup", ticketId: null, startTime: "2026-11-04T10:30:00+01:00", endTime: "2026-11-04T11:00:00+01:00", source: "O365 Calendar" };

function decode(image) {
	const [prefix, payload] = image.split(",");
	assert.equal(prefix, "data:image/svg+xml;base64");
	return Buffer.from(payload, "base64").toString("utf8");
}

async function loadedBoard(items, failedSources = [], pageSize = 6) {
	const board = new KeyBoard(async () => ({ success: true, error: null, suggestions: { items, failedSources } }), readSuggestions);
	board.setPageSize(pageSize);
	await board.load(new Date("2026-11-04T09:00:00+01:00"));
	return board;
}

describe("wrapText", () => {
	it("keeps short text on one line", () => {
		assert.deepEqual(wrapText("Fix login", 12, 3), ["Fix login"]);
	});

	it("wraps at words", () => {
		assert.deepEqual(wrapText("Oprava chyby v přihlášení", 12, 3), ["Oprava chyby", "v přihlášení"]);
	});

	it("breaks a word too long for a line", () => {
		assert.deepEqual(wrapText("abcdefghij", 4, 3), ["abcd", "efgh", "ij"]);
	});

	it("marks text that did not fit", () => {
		assert.deepEqual(wrapText("one two three four", 5, 2), ["one", "two…"]);
	});

	it("is empty for blank text", () => {
		assert.deepEqual(wrapText("   ", 12, 3), []);
	});
});

describe("getSuggestionKeyView", () => {
	it("heads a ticket with its ID", () => {
		assert.equal(getSuggestionKeyView(ticket, false).heading.text, "PROJ-1");
	});

	it("heads an event with its local time", () => {
		assert.equal(getSuggestionKeyView(event, false).heading.text, "10:30");
	});

	it("tells events and tickets apart by color", () => {
		assert.notEqual(getSuggestionKeyView(ticket, false).heading.color, getSuggestionKeyView(event, false).heading.color);
	});

	it("marks the running work", () => {
		const running = getSuggestionKeyView(ticket, true);

		assert.notEqual(running.heading.color, getSuggestionKeyView(ticket, false).heading.color);
		assert.equal(running.lines[0].color, running.heading.color);
	});

	it("dims an event that is over, unless it is the running work", () => {
		const over = getSuggestionKeyView(event, false, true);

		assert.notEqual(over.heading.color, getSuggestionKeyView(event, false).heading.color);
		assert.equal(over.lines[0].color, over.heading.color);
		assert.equal(getSuggestionKeyView(event, true, true).heading.color, getSuggestionKeyView(event, true).heading.color);
	});

	it("has no heading without a time or ticket", () => {
		assert.equal(getSuggestionKeyView({ ...ticket, ticketId: null }, false).heading, null);
	});

	it("shortens a long ticket ID", () => {
		assert.equal(getSuggestionKeyView({ ...ticket, ticketId: "LONGPROJECT-12345" }, false).heading.text, "LONGPROJE…");
	});
});

describe("isActiveSuggestion", () => {
	const work = { ticketId: "PROJ-1", description: "Fix login", startedAt: "2026-11-04T09:00:00+01:00" };

	it("matches the same ticket and title", () => {
		assert.equal(isActiveSuggestion(ticket, work), true);
	});

	it("does not match other work or none", () => {
		assert.equal(isActiveSuggestion(ticket, { ...work, ticketId: "PROJ-2" }), false);
		assert.equal(isActiveSuggestion(ticket, null), false);
	});

	it("matches an event without a ticket by its title", () => {
		assert.equal(isActiveSuggestion(event, { ...work, ticketId: null, description: "Standup" }), true);
	});
});

describe("getFavoriteKeyView", () => {
	const favorite = { name: "Support", ticketId: "PROJ-2", description: "Helping out", showAsTemplate: false };

	it("heads a favorite with its ticket and shows its name", () => {
		const view = getFavoriteKeyView(favorite, false);

		assert.equal(view.heading.text, "PROJ-2");
		assert.deepEqual(view.lines.map((line) => line.text), ["Support"]);
	});

	it("has no heading without a ticket", () => {
		assert.equal(getFavoriteKeyView({ ...favorite, ticketId: null }, false).heading, null);
	});

	it("marks the running work", () => {
		const running = getFavoriteKeyView(favorite, true);

		assert.notEqual(running.heading.color, getFavoriteKeyView(favorite, false).heading.color);
		assert.equal(running.lines[0].color, running.heading.color);
	});

	it("tells a template from a favorite that starts right away", () => {
		const template = getFavoriteKeyView({ ...favorite, showAsTemplate: true }, false);
		const plain = getFavoriteKeyView(favorite, false);

		assert.notEqual(template.heading.color, plain.heading.color);
		assert.notEqual(template.lines[0].color, plain.lines[0].color);
	});
});

describe("isActiveFavorite", () => {
	const favorite = { name: "Support", ticketId: "PROJ-2", description: "Helping out", showAsTemplate: false };
	const work = { ticketId: "PROJ-2", description: "Helping out", startedAt: "2026-11-04T09:00:00+01:00" };

	it("matches the same ticket and description, not the name", () => {
		assert.equal(isActiveFavorite(favorite, work), true);
		assert.equal(isActiveFavorite(favorite, { ...work, description: "Support" }), false);
	});

	it("matches values that are missing on both sides", () => {
		assert.equal(isActiveFavorite({ ...favorite, description: null }, { ...work, description: null }), true);
	});

	it("does not match other work or none", () => {
		assert.equal(isActiveFavorite(favorite, { ...work, ticketId: "PROJ-3" }), false);
		assert.equal(isActiveFavorite(favorite, null), false);
	});

	it("never takes a template for the running work", () => {
		assert.equal(isActiveFavorite({ ...favorite, showAsTemplate: true }, work), false);
	});
});

describe("getNextKeyView", () => {
	it("says when loading failed", async () => {
		const board = new KeyBoard(async () => ({ success: false, error: "Not connected." }), readSuggestions);
		await board.load();

		assert.equal(getNextKeyView(board).heading.text, "Failed");
	});

	it("says when there is nothing to show", async () => {
		assert.equal(getNextKeyView(await loadedBoard([])).heading.text, "None");
	});

	it("shows the page and offers more", async () => {
		const view = getNextKeyView(await loadedBoard([ticket, ticket], [], 1));

		assert.equal(view.heading.text, "1/2");
		assert.equal(view.lines[0].text, "More");
	});

	it("does not offer more on a single page", async () => {
		assert.deepEqual(getNextKeyView(await loadedBoard([ticket])).lines, []);
	});

	it("names a source that failed", async () => {
		const view = getNextKeyView(await loadedBoard([ticket], ["Jira"]));

		assert.equal(view.lines[0].text, "! Jira");
	});

	it("names failed sources even with nothing to show", async () => {
		const view = getNextKeyView(await loadedBoard([], ["Jira"]));

		assert.equal(view.heading.text, "None");
		assert.equal(view.lines[0].text, "! Jira");
	});
});

describe("renderKeyImage", () => {
	it("escapes text from the sources", () => {
		const svg = decode(renderKeyImage({ heading: null, lines: [{ text: "R&D <sync>", color: "#FFFFFF" }] }));

		assert.ok(svg.includes("R&amp;D &lt;sync&gt;"));
		assert.ok(!svg.includes("<sync>"));
	});

	it("keeps Czech characters", () => {
		const svg = decode(renderKeyImage({ heading: null, lines: [{ text: "Přihlášení", color: "#FFFFFF" }] }));

		assert.ok(svg.includes("Přihlášení"));
	});

	it("draws an empty key", () => {
		assert.ok(!decode(renderKeyImage({ heading: null, lines: [] })).includes("<text"));
	});
});
