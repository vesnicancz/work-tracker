import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { StartShift } from "../src/start-shift.ts";

const DELAY_MS = 10;
const success = { success: true, error: null };
const failure = { success: false, error: "Overlap." };

function createShift(result = success) {
	const sent = [];
	const settled = [];
	const shift = new StartShift(async (minutes) => {
		sent.push(minutes);
		return result;
	}, (settledResult) => settled.push(settledResult), DELAY_MS);

	return { shift, sent, settled };
}

function waitForDelay() {
	return new Promise((resolve) => setTimeout(resolve, DELAY_MS * 5));
}

describe("StartShift", () => {
	it("sends a whole turn of the dial as one correction", async () => {
		const { shift, sent } = createShift();

		shift.add(-1, 60);
		shift.add(-2, 60);
		shift.add(-1, 60);
		await waitForDelay();

		assert.deepEqual(sent, [-4]);
	});

	it("counts the correction until it is confirmed, and not after", async () => {
		const { shift } = createShift();

		shift.add(-3, 60);
		assert.equal(shift.minutes, -3);

		await shift.flush();
		assert.equal(shift.minutes, 0);
	});

	it("sends nothing when the dial is turned back to where it was", async () => {
		const { shift, sent, settled } = createShift();

		shift.add(-2, 60);
		shift.add(2, 60);
		await waitForDelay();

		assert.deepEqual(sent, []);
		assert.deepEqual(settled, []);
	});

	it("does not move the start into the future", async () => {
		const { shift, sent } = createShift();

		shift.add(5, 3);
		shift.add(1, 3);
		assert.equal(shift.minutes, 3);

		await shift.flush();
		assert.deepEqual(sent, [3]);
	});

	it("can move back from the limit without working off the excess first", () => {
		const { shift } = createShift();

		shift.add(50, 3);
		shift.add(-1, 3);

		assert.equal(shift.minutes, 2);
		shift.discard();
	});

	it("counts a correction on its way towards the limit", async () => {
		let release;
		const sent = [];
		const shift = new StartShift((minutes) => {
			sent.push(minutes);
			return new Promise((resolve) => {
				release = () => resolve(success);
			});
		}, () => {}, DELAY_MS);

		shift.add(2, 3);
		const first = shift.flush();
		await new Promise((resolve) => setImmediate(resolve));

		shift.add(5, 3);
		assert.equal(shift.minutes, 3);

		release();
		await first;
		const second = shift.flush();
		await new Promise((resolve) => setImmediate(resolve));
		release();
		await second;

		assert.deepEqual(sent, [2, 1]);
	});

	it("flush sends right away and only once", async () => {
		const { shift, sent } = createShift();

		shift.add(-5, 60);
		await shift.flush();
		await waitForDelay();

		assert.deepEqual(sent, [-5]);
	});

	it("reports a refusal and forgets the correction", async () => {
		const { shift, settled } = createShift(failure);

		shift.add(-5, 60);
		await shift.flush();

		assert.deepEqual(settled, [failure]);
		assert.equal(shift.minutes, 0);
	});

	it("discard drops what has not been sent", async () => {
		const { shift, sent } = createShift();

		shift.add(-5, 60);
		shift.discard();
		await waitForDelay();

		assert.equal(shift.minutes, 0);
		assert.deepEqual(sent, []);
	});
});
