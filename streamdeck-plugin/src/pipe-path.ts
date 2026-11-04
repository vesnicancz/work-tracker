import os from "node:os";
import path from "node:path";

/**
 * Where WorkTracker listens. Keep in sync with plugins/WorkTracker.Plugin.StreamDeck/PipeEndpoint.cs.
 */
export function getPipePath(): string {
	return process.platform === "win32"
		? "\\\\.\\pipe\\worktracker-streamdeck"
		: path.join(os.tmpdir(), "worktracker-streamdeck.sock");
}
