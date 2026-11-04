import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import { afterEach, describe, it } from "node:test";

import { WorkTrackerClient } from "../src/client.ts";

const state = {
	type: "state",
	activeWork: null,
	lastWork: null,
	pomodoro: { phase: "idle", remainingSeconds: 0, completedPomodoros: 0, pomodorosBeforeLongBreak: 4 }
};

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

describe("WorkTrackerClient", () => {
	let client;
	let server;

	afterEach(async () => {
		client?.stop();
		server?.closeAllConnections?.();
		await new Promise((resolve) => (server ? server.close(resolve) : resolve()));
		client = undefined;
		server = undefined;
	});

	it("receives pushed state, also split across chunks", async () => {
		const pipePath = createPipePath();
		const message = `${JSON.stringify({ ...state, lastWork: { ticketId: "PROJ-1", description: null, startedAt: "2026-11-04T09:00:00+01:00" } })}\n`;
		server = await listen(pipePath, (socket) => {
			socket.write(message.slice(0, 10));
			setTimeout(() => socket.write(message.slice(10)), 20);
		});

		client = new WorkTrackerClient(pipePath);
		const received = nextState(client);
		client.start();

		assert.equal((await received).lastWork.ticketId, "PROJ-1");
	});

	it("matches a response to its command", async () => {
		const pipePath = createPipePath();
		const requests = [];
		server = await listen(pipePath, (socket) => {
			socket.write(`${JSON.stringify(state)}\n`);
			socket.setEncoding("utf8");
			socket.on("data", (chunk) => {
				const request = JSON.parse(chunk);
				requests.push(request);
				socket.write(`${JSON.stringify({ type: "result", id: request.id, success: false, error: "Nope." })}\n`);
			});
		});

		client = new WorkTrackerClient(pipePath);
		const received = nextState(client);
		client.start();
		await received;

		const result = await client.send("shiftStart", { minutes: -5 });

		assert.deepEqual(result, { success: false, error: "Nope." });
		assert.equal(requests[0].command, "shiftStart");
		assert.equal(requests[0].minutes, -5);
	});

	it("fails commands without a connection", async () => {
		client = new WorkTrackerClient(createPipePath());
		client.start();

		const result = await client.send("toggleWork");

		assert.equal(result.success, false);
	});

	it("reports the disconnect and reconnects", async () => {
		const pipePath = createPipePath();
		let connections = 0;
		const sockets = [];
		server = await listen(pipePath, (socket) => {
			connections++;
			sockets.push(socket);
			socket.on("error", () => {});
			socket.write(`${JSON.stringify(state)}\n`);
		});

		client = new WorkTrackerClient(pipePath);
		const states = [];
		const reconnected = new Promise((resolve) => {
			client.onStateChanged((value) => {
				states.push(value);
				if (states.length === 1) {
					sockets[0].destroy();
				} else if (states.length === 3) {
					resolve();
				}
			});
		});
		client.start();
		await reconnected;

		assert.notEqual(states[0], null);
		assert.equal(states[1], null);
		assert.notEqual(states[2], null);
		assert.equal(connections, 2);
	});
});
