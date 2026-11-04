import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { readFavorites } from "../src/favorite-board.ts";
import { KeyBoard } from "../src/key-board.ts";

const now = new Date("2026-11-04T12:00:00+01:00");

function answer(items, failedSources = []) {
	return { success: true, error: null, items, failedSources };
}

function read(result) {
	return { items: result.items, failedSources: result.failedSources };
}

function createBoard(items, failedSources = []) {
	return new KeyBoard(async () => answer(items, failedSources), read);
}

describe("KeyBoard", () => {
	it("spreads items over pages of the size of the key count", async () => {
		const board = createBoard(["A-1", "A-2", "A-3", "A-4", "A-5"]);
		board.setPageSize(2);
		await board.load(now);

		assert.equal(board.pageCount, 3);
		assert.equal(board.itemAt(1), "A-2");

		board.nextPage();
		assert.equal(board.itemAt(0), "A-3");

		board.nextPage();
		assert.equal(board.itemAt(0), "A-5");
		assert.equal(board.itemAt(1), null);
	});

	it("wraps around after the last page", async () => {
		const board = createBoard(["A-1", "A-2"]);
		board.setPageSize(1);
		await board.load(now);

		board.nextPage();
		board.nextPage();

		assert.equal(board.page, 0);
	});

	it("keeps the page valid when the keys get fewer pages to fill", async () => {
		const board = createBoard(["A-1", "A-2", "A-3"]);
		board.setPageSize(1);
		await board.load(now);
		board.nextPage();
		board.nextPage();

		board.setPageSize(3);

		assert.equal(board.page, 0);
		assert.equal(board.itemAt(2), "A-3");
	});

	it("starts from the first page on every load", async () => {
		const board = createBoard(["A-1", "A-2"]);
		board.setPageSize(1);
		await board.load(now);
		board.nextPage();

		await board.load(now);

		assert.equal(board.page, 0);
	});

	it("names failed sources while keeping the rest", async () => {
		const board = createBoard(["A-1"], ["O365 Calendar"]);
		await board.load(now);

		assert.equal(board.status, "ready");
		assert.deepEqual(board.failedSources, ["O365 Calendar"]);
		assert.equal(board.itemCount, 1);
	});

	it("reports a failed load and shows nothing", async () => {
		const board = new KeyBoard(async () => ({ success: false, error: "Not connected." }), read);
		await board.load(now);

		assert.equal(board.status, "failed");
		assert.equal(board.itemAt(0), null);
	});

	it("reports a successful answer without any content as failed", async () => {
		const board = new KeyBoard(async () => ({ success: true, error: null }), () => null);
		await board.load(now);

		assert.equal(board.status, "failed");
	});

	it("ignores an answer that a newer load has overtaken", async () => {
		const answers = [];
		const board = new KeyBoard(() => new Promise((resolve) => answers.push(resolve)), read);

		const first = board.load(now);
		const second = board.load(now);
		answers[1](answer(["NEW-1"]));
		await second;
		answers[0](answer(["OLD-1"]));
		await first;

		assert.equal(board.itemAt(0), "NEW-1");
	});

	it("announces loading and the result", async () => {
		const statuses = [];
		const board = createBoard([]);
		board.onChanged(() => statuses.push(board.status));

		await board.load(now);

		assert.deepEqual(statuses, ["loading", "ready"]);
	});
});

describe("readFavorites", () => {
	const favorites = [
		{ name: "Zebra", ticketId: null, description: "Last by name", showAsTemplate: false },
		{ name: "Alpha", ticketId: "PROJ-1", description: null, showAsTemplate: true }
	];

	it("keeps the user's order", async () => {
		const board = new KeyBoard(async () => ({ success: true, error: null, favorites }), readFavorites);
		board.setPageSize(2);
		await board.load(now);

		assert.equal(board.status, "ready");
		assert.equal(board.itemAt(0).name, "Zebra");
		assert.equal(board.itemAt(1).name, "Alpha");
	});

	it("treats an empty list as loaded", () => {
		assert.deepEqual(readFavorites({ success: true, error: null, favorites: [] }), { items: [], failedSources: [] });
	});

	it("gives nothing for an answer without favorites", () => {
		assert.equal(readFavorites({ success: true, error: null, favorites: null }), null);
	});
});
