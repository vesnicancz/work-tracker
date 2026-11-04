import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { readFileSync } from "node:fs";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import { afterEach, describe, it } from "node:test";

import { PROTOCOL_VERSION, WorkTrackerClient } from "../src/client.ts";

/**
 * RemoteControlProtocolFixtureTests on the C# side checks that WorkTracker produces and accepts these
 * same files, so a change on either side fails here or there.
 */
function fixture(name) {
	return JSON.parse(readFileSync(new URL(`./fixtures/protocol/${name}.json`, import.meta.url), "utf8"));
}

function createPipePath() {
	const name = `worktracker-test-${randomUUID()}`;
	return process.platform === "win32" ? `\\\\.\\pipe\\${name}` : path.join(os.tmpdir(), `${name}.sock`);
}

function listen(pipePath, onConnection) {
	return new Promise((resolve) => {
		const server = net.createServer(onConnection);
		server.listen(pipePath, () => resolve(server));
	});
}

function nextState(client) {
	return new Promise((resolve) => client.onStateChanged(resolve));
}

describe("protocol fixtures", () => {
	let client;
	let server;

	afterEach(async () => {
		client?.stop();
		server?.closeAllConnections?.();
		await new Promise((resolve) => (server ? server.close(resolve) : resolve()));
		client = undefined;
		server = undefined;
	});

	/**
	 * Connects to a server that pushes the state fixture and answers each request with
	 * the given fixture under the id of the request.
	 */
	async function connect(responseFixture, requests = []) {
		const pipePath = createPipePath();
		server = await listen(pipePath, (socket) => {
			socket.write(`${JSON.stringify(fixture("state"))}\n`);
			socket.setEncoding("utf8");
			socket.on("data", (chunk) => {
				const request = JSON.parse(chunk);
				requests.push(request);
				socket.write(`${JSON.stringify({ ...fixture(responseFixture), id: request.id })}\n`);
			});
		});

		client = new WorkTrackerClient(pipePath);
		const received = nextState(client);
		client.start();
		return received;
	}

	it("uses the version the fixture states", () => {
		assert.equal(fixture("state").protocol, PROTOCOL_VERSION);
	});

	it("reads the state", async () => {
		const state = await connect("favorites-result");

		assert.equal(state.activeWork.ticketId, "PROJ-1");
		assert.equal(state.activeWork.description, "Coding");
		assert.equal(state.activeWork.startedAt, "2026-11-04T09:30:00+01:00");
		assert.equal(state.lastWork, null);
		assert.equal(state.pomodoro.phase, "shortBreak");
		assert.equal(state.pomodoro.remainingSeconds, 90);
		assert.equal(state.pomodoro.completedPomodoros, 2);
		assert.equal(state.pomodoro.pomodorosBeforeLongBreak, 4);
	});

	it("reads suggestions", async () => {
		await connect("suggestions-result");

		const result = await client.getSuggestions();

		assert.equal(result.success, true);
		assert.deepEqual(result.suggestions.failedSources, ["GitHub"]);
		const [standup, fix] = result.suggestions.items;
		assert.equal(standup.title, "Standup");
		assert.equal(standup.startTime, "2026-11-04T10:00:00+01:00");
		assert.equal(standup.endTime, "2026-11-04T10:15:00+01:00");
		assert.equal(standup.source, "O365 Calendar");
		assert.equal(fix.ticketId, "PROJ-1");
	});

	it("reads favorites", async () => {
		await connect("favorites-result");

		const result = await client.getFavorites();

		assert.equal(result.success, true);
		const [support, meeting] = result.favorites;
		assert.equal(support.name, "Support");
		assert.equal(support.ticketId, "PROJ-2");
		assert.equal(support.showAsTemplate, false);
		assert.equal(meeting.description, "Weekly");
		assert.equal(meeting.showAsTemplate, true);
	});

	it("sends a request in the shape of the fixture", async () => {
		const requests = [];
		await connect("favorites-result", requests);
		const { id: _id, command, ...args } = fixture("add-work-request");

		await client.send(command, args);

		assert.deepEqual({ ...requests[0], id: 1 }, fixture("add-work-request"));
	});
});

describe("protocol version", () => {
	let client;
	let server;

	afterEach(async () => {
		client?.stop();
		server?.closeAllConnections?.();
		await new Promise((resolve) => (server ? server.close(resolve) : resolve()));
	});

	async function connectTo(firstLines) {
		const pipePath = createPipePath();
		server = await listen(pipePath, (socket) => {
			socket.write(firstLines.map((line) => `${line}\n`).join(""));
		});

		client = new WorkTrackerClient(pipePath);
		client.start();
		await new Promise((resolve) => setTimeout(resolve, 100));
	}

	it("refuses a different version and says why", async () => {
		await connectTo([JSON.stringify({ ...fixture("state"), protocol: PROTOCOL_VERSION + 1 })]);

		const result = await client.send("toggleWork");

		assert.equal(client.state, null);
		assert.equal(result.success, false);
		assert.match(result.error, /different versions/);
	});

	it("accepts a WorkTracker from before the protocol had a version", async () => {
		const { protocol: _protocol, ...state } = fixture("state");
		await connectTo([JSON.stringify(state)]);

		assert.equal(client.state.activeWork.ticketId, "PROJ-1");
	});

	it("survives a line that is not JSON", async () => {
		await connectTo(["not json", JSON.stringify(fixture("state"))]);

		assert.equal(client.state.activeWork.ticketId, "PROJ-1");
	});
});
