import type { CommandResult } from "./protocol";

const SEND_DELAY_MS = 400;

type SendShift = (minutes: number) => Promise<CommandResult>;
type ShiftSettled = (result: CommandResult) => void;

/**
 * Collects rotation of the dial into one correction of the start time. Every correction is a write to
 * the database, and a single turn of the dial is many ticks, so it is sent once the dial comes to rest.
 * Until WorkTracker confirms it, {@link minutes} is what the strip has to add to show the result.
 */
export class StartShift {
	readonly #send: SendShift;
	readonly #onSettled: ShiftSettled;
	readonly #delayMs: number;

	#pendingMinutes = 0;
	#sendingMinutes = 0;
	#timer: NodeJS.Timeout | null = null;
	#queue: Promise<void> = Promise.resolve();

	constructor(send: SendShift, onSettled: ShiftSettled, delayMs: number = SEND_DELAY_MS) {
		this.#send = send;
		this.#onSettled = onSettled;
		this.#delayMs = delayMs;
	}

	get minutes(): number {
		return this.#pendingMinutes + this.#sendingMinutes;
	}

	/**
	 * @param minutes Positive moves the start later, negative earlier.
	 * @param latestMinutes How far the confirmed start can move later before it would be in the future.
	 */
	add(minutes: number, latestMinutes: number): void {
		this.#pendingMinutes = Math.min(this.#pendingMinutes + minutes, Math.max(0, latestMinutes - this.#sendingMinutes));

		this.#clearTimer();
		this.#timer = setTimeout(() => void this.flush(), this.#delayMs);
	}

	/**
	 * Sends what has been collected right away, after any correction already on its way.
	 */
	flush(): Promise<void> {
		this.#clearTimer();
		this.#queue = this.#queue.then(() => this.#sendPending());
		return this.#queue;
	}

	discard(): void {
		this.#clearTimer();
		this.#pendingMinutes = 0;
	}

	async #sendPending(): Promise<void> {
		if (this.#pendingMinutes === 0) {
			return;
		}

		this.#sendingMinutes = this.#pendingMinutes;
		this.#pendingMinutes = 0;

		const result = await this.#send(this.#sendingMinutes);
		this.#sendingMinutes = 0;

		if (!result.success) {
			// The strip falls back to the real start, so what was added since has nothing to build on
			this.discard();
		}

		this.#onSettled(result);
	}

	#clearTimer(): void {
		if (this.#timer) {
			clearTimeout(this.#timer);
			this.#timer = null;
		}
	}
}
