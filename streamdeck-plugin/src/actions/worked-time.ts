import { action, type DialDownEvent, SingletonAction, type TouchTapEvent, type WillAppearEvent, type WillDisappearEvent } from "@elgato/streamdeck";

import type { WorkTrackerClient } from "../client";
import { getWorkedTimeView, togglePeriod, type WorkedTimePeriod } from "../worked-time-view";

const RENDER_INTERVAL_MS = 1000;

type Settings = {
	period?: WorkedTimePeriod;
};

type Dial = DialDownEvent<Settings>["action"];

/**
 * Shows the time worked today against the daily target from the plugin's configuration in WorkTracker.
 * Pressing the dial or tapping the strip switches between today and this week; the choice is kept in
 * the settings of the dial.
 */
@action({ UUID: "com.vesnicancz.worktracker.worked-time" })
export class WorkedTimeDial extends SingletonAction<Settings> {
	readonly #client: WorkTrackerClient;
	readonly #periods = new Map<string, WorkedTimePeriod>();

	constructor(client: WorkTrackerClient) {
		super();
		this.#client = client;

		// The state only says how much was worked when it was sent, so the time on the strip is counted here
		client.onStateChanged(() => this.#renderAll());
		setInterval(() => this.#renderAll(), RENDER_INTERVAL_MS);
	}

	override async onWillAppear(ev: WillAppearEvent<Settings>): Promise<void> {
		if (ev.action.isDial()) {
			this.#periods.set(ev.action.id, ev.payload.settings.period ?? "today");
			await this.#render(ev.action);
		}
	}

	override onWillDisappear(ev: WillDisappearEvent<Settings>): void {
		this.#periods.delete(ev.action.id);
	}

	override async onDialDown(ev: DialDownEvent<Settings>): Promise<void> {
		await this.#togglePeriod(ev.action);
	}

	override async onTouchTap(ev: TouchTapEvent<Settings>): Promise<void> {
		await this.#togglePeriod(ev.action);
	}

	async #togglePeriod(dial: Dial): Promise<void> {
		const period = togglePeriod(this.#periods.get(dial.id) ?? "today");
		this.#periods.set(dial.id, period);

		await this.#render(dial);
		await dial.setSettings({ period });
	}

	#renderAll(): void {
		for (const dial of this.actions) {
			if (dial.isDial()) {
				void this.#render(dial);
			}
		}
	}

	async #render(dial: Dial): Promise<void> {
		const view = getWorkedTimeView(this.#client.state, this.#periods.get(dial.id) ?? "today", new Date());

		await dial.setFeedback({
			title: { value: view.title, color: view.color },
			value: { value: view.value, color: view.color },
			progress: { value: view.progress, bar_fill_c: view.progressColor }
		});
	}
}
