import streamDeck, { action, type DialDownEvent, type DialRotateEvent, SingletonAction, type TouchTapEvent, type WillAppearEvent } from "@elgato/streamdeck";

import type { WorkTrackerClient } from "../client";
import type { CommandResult } from "../protocol";
import { StartShift } from "../start-shift";
import { getLatestStartShiftMinutes, getWorkDialView } from "../work-dial-view";

const RENDER_INTERVAL_MS = 1000;

type Dial = DialDownEvent["action"];

/**
 * Shows the tracked work with its running time. Pressing the dial or tapping the strip stops the work,
 * or resumes the most recent work when nothing is being tracked. Rotating the dial corrects when the
 * work started, a minute per tick: clockwise adds time, counter-clockwise takes it away.
 */
@action({ UUID: "com.vesnicancz.worktracker.work" })
export class WorkDial extends SingletonAction {
	readonly #client: WorkTrackerClient;
	readonly #startShift: StartShift;

	constructor(client: WorkTrackerClient) {
		super();
		this.#client = client;
		this.#startShift = new StartShift(
			(minutes) => client.send("shiftStart", { minutes }),
			(result) => this.#onStartShiftSettled(result));

		// The state only says when the work started, so the time on the strip is counted here
		client.onStateChanged((state) => {
			if (!state?.activeWork) {
				this.#startShift.discard();
			}

			this.#renderAll();
		});
		setInterval(() => this.#renderAll(), RENDER_INTERVAL_MS);
	}

	override async onWillAppear(ev: WillAppearEvent): Promise<void> {
		if (ev.action.isDial()) {
			await this.#render(ev.action);
		}
	}

	override async onDialDown(ev: DialDownEvent): Promise<void> {
		await this.#toggleWork(ev.action);
	}

	override async onTouchTap(ev: TouchTapEvent): Promise<void> {
		await this.#toggleWork(ev.action);
	}

	override onDialRotate(ev: DialRotateEvent): void {
		const activeWork = this.#client.state?.activeWork;
		if (!activeWork) {
			return;
		}

		this.#startShift.add(-ev.payload.ticks, getLatestStartShiftMinutes(activeWork, new Date()));
		this.#renderAll();
	}

	async #toggleWork(dial: Dial): Promise<void> {
		// A press right after a turn must not stop the work with its start still uncorrected
		await this.#startShift.flush();

		const result = await this.#client.send("toggleWork");
		if (!result.success) {
			streamDeck.logger.warn(`Toggling work failed: ${result.error}`);
			await dial.showAlert();
		}
	}

	#onStartShiftSettled(result: CommandResult): void {
		this.#renderAll();

		if (!result.success) {
			streamDeck.logger.warn(`Correcting the start failed: ${result.error}`);
			for (const dial of this.actions) {
				if (dial.isDial()) {
					void dial.showAlert();
				}
			}
		}
	}

	#renderAll(): void {
		for (const dial of this.actions) {
			if (dial.isDial()) {
				void this.#render(dial);
			}
		}
	}

	async #render(dial: Dial): Promise<void> {
		const view = getWorkDialView(this.#client.state, new Date(), this.#startShift.minutes);

		await dial.setFeedback({
			title: { value: view.title, color: view.color },
			value: { value: view.value, color: view.color }
		});
	}
}
