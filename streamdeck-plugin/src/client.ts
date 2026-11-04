import net from "node:net";

import type { Command, CommandArguments, CommandResult, RemoteState, ServerMessage } from "./protocol";

const RECONNECT_DELAY_MS = 2000;
const COMMAND_TIMEOUT_MS = 10000;

// Longer than WorkTracker's own limit, so that a slow suggestion plugin gets reported from there
const SUGGESTIONS_TIMEOUT_MS = 30000;

type StateListener = (state: RemoteState | null) => void;

type PendingCommand = {
	resolve: (result: CommandResult) => void;
	timeout: NodeJS.Timeout;
};

/**
 * The connection to WorkTracker. Keeps reconnecting for as long as it runs, because either side can
 * start first and WorkTracker comes and goes with the user. Commands never reject: anything that goes
 * wrong is a failed result, which the action shows the same way as a refusal from WorkTracker.
 */
export class WorkTrackerClient {
	readonly #pipePath: string;
	readonly #pending = new Map<number, PendingCommand>();
	readonly #listeners: StateListener[] = [];

	#socket: net.Socket | null = null;
	#reconnectTimer: NodeJS.Timeout | null = null;
	#buffer = "";
	#nextId = 1;
	#stopped = false;

	state: RemoteState | null = null;

	constructor(pipePath: string) {
		this.#pipePath = pipePath;
	}

	onStateChanged(listener: StateListener): void {
		this.#listeners.push(listener);
	}

	start(): void {
		this.#connect();
	}

	stop(): void {
		this.#stopped = true;
		if (this.#reconnectTimer) {
			clearTimeout(this.#reconnectTimer);
		}
		this.#socket?.destroy();
	}

	getSuggestions(): Promise<CommandResult> {
		return this.send("getSuggestions", {}, SUGGESTIONS_TIMEOUT_MS);
	}

	getFavorites(): Promise<CommandResult> {
		return this.send("getFavorites");
	}

	send(command: Command, args: CommandArguments = {}, timeoutMs: number = COMMAND_TIMEOUT_MS): Promise<CommandResult> {
		const socket = this.#socket;
		if (!socket || this.state === null) {
			return Promise.resolve({ success: false, error: "WorkTracker is not connected." });
		}

		return new Promise((resolve) => {
			const id = this.#nextId++;
			const timeout = setTimeout(() => {
				this.#pending.delete(id);
				resolve({ success: false, error: "WorkTracker did not respond." });
			}, timeoutMs);

			this.#pending.set(id, { resolve, timeout });
			socket.write(`${JSON.stringify({ id, command, ...args })}\n`);
		});
	}

	#connect(): void {
		const socket = net.connect({ path: this.#pipePath });
		socket.setEncoding("utf8");
		this.#socket = socket;
		this.#buffer = "";

		socket.on("data", (chunk: string) => this.#onData(chunk));

		// Expected whenever WorkTracker is not running; "close" follows and schedules the retry.
		socket.on("error", () => {});

		socket.on("close", () => this.#onClose());
	}

	#onData(chunk: string): void {
		this.#buffer += chunk;

		let newline: number;
		while ((newline = this.#buffer.indexOf("\n")) >= 0) {
			const line = this.#buffer.slice(0, newline);
			this.#buffer = this.#buffer.slice(newline + 1);
			this.#onMessage(JSON.parse(line) as ServerMessage);
		}
	}

	#onMessage(message: ServerMessage): void {
		if (message.type === "state") {
			const { activeWork, lastWork, pomodoro } = message;
			this.#setState({ activeWork, lastWork, pomodoro });
			return;
		}

		const pending = message.id === null ? undefined : this.#pending.get(message.id);
		if (pending && message.id !== null) {
			this.#pending.delete(message.id);
			clearTimeout(pending.timeout);
			const { type: _type, id: _id, ...result } = message;
			pending.resolve(result);
		}
	}

	#onClose(): void {
		this.#socket = null;

		for (const pending of this.#pending.values()) {
			clearTimeout(pending.timeout);
			pending.resolve({ success: false, error: "WorkTracker disconnected." });
		}
		this.#pending.clear();

		if (this.state !== null) {
			this.#setState(null);
		}

		if (!this.#stopped) {
			this.#reconnectTimer = setTimeout(() => this.#connect(), RECONNECT_DELAY_MS);
		}
	}

	#setState(state: RemoteState | null): void {
		this.state = state;
		for (const listener of this.#listeners) {
			listener(state);
		}
	}
}
