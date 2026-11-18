import { action, type DialDownEvent, type DialRotateEvent, SingletonAction, type TouchTapEvent, type WillAppearEvent, type WillDisappearEvent } from "@elgato/streamdeck";

import type { WorkTrackerClient } from "../client";
import type { CommandResult } from "../protocol";
import {
	getHistoryKey,
	getWorkedTimeView,
	moveSelection,
	togglePeriod,
	type WorkedTimeHistory,
	type WorkedTimePeriod,
	type WorkedTimeSelection
} from "../worked-time-view";

const RENDER_INTERVAL_MS = 1000;
const BACK_TO_TODAY_MS = 30000;

type Settings = {
	period?: WorkedTimePeriod;
};

type Dial = DialDownEvent<Settings>["action"];

type DialState = {
	selection: WorkedTimeSelection;
	backToToday?: ReturnType<typeof setTimeout>;
};

/**
 * Shows the time worked today against the daily target from the plugin's configuration in WorkTracker.
 * Pressing the dial or tapping the strip switches between the day and the week; the period is kept in
 * the settings of the dial. Rotating it goes back in time, a day or a week per tick, up to two weeks,
 * and the dial comes back to today on its own after a while.
 */
@action({ UUID: "com.vesnicancz.worktracker.worked-time" })
export class WorkedTimeDial extends SingletonAction<Settings> {
	readonly #client: WorkTrackerClient;
	readonly #dials = new Map<string, DialState>();
	readonly #history = new Map<string, WorkedTimeHistory>();
	// Answers asked for before the last change of the entries are thrown away
	#historyGeneration = 0;

	constructor(client: WorkTrackerClient) {
		super();
		this.#client = client;

		// The state says only how much was worked when it was sent, so the time on the strip is counted here.
		// It also comes when entries change, and then the past may have changed too.
		client.onStateChanged(() => {
			this.#history.clear();
			this.#historyGeneration++;
			this.#renderAll();
		});
		setInterval(() => this.#renderAll(), RENDER_INTERVAL_MS);
	}

	override async onWillAppear(ev: WillAppearEvent<Settings>): Promise<void> {
		if (ev.action.isDial()) {
			this.#dials.set(ev.action.id, { selection: { period: ev.payload.settings.period ?? "today", date: null } });
			await this.#render(ev.action);
		}
	}

	override onWillDisappear(ev: WillDisappearEvent<Settings>): void {
		clearTimeout(this.#dials.get(ev.action.id)?.backToToday);
		this.#dials.delete(ev.action.id);
	}

	override async onDialDown(ev: DialDownEvent<Settings>): Promise<void> {
		await this.#togglePeriod(ev.action);
	}

	override async onTouchTap(ev: TouchTapEvent<Settings>): Promise<void> {
		await this.#togglePeriod(ev.action);
	}

	override async onDialRotate(ev: DialRotateEvent<Settings>): Promise<void> {
		const dial = this.#getDial(ev.action);
		this.#select(ev.action, dial, moveSelection(dial.selection, ev.payload.ticks, new Date()));
		await this.#render(ev.action);
	}

	async #togglePeriod(action: Dial): Promise<void> {
		const dial = this.#getDial(action);
		const selection = togglePeriod(dial.selection, new Date());
		this.#select(action, dial, selection);

		await this.#render(action);
		await action.setSettings({ period: selection.period });
	}

	#getDial(action: Dial): DialState {
		let dial = this.#dials.get(action.id);
		if (!dial) {
			dial = { selection: { period: "today", date: null } };
			this.#dials.set(action.id, dial);
		}

		return dial;
	}

	/**
	 * A past day left on the strip would pass for today's at the next look, so it goes back to today.
	 */
	#select(action: Dial, dial: DialState, selection: WorkedTimeSelection): void {
		dial.selection = selection;
		clearTimeout(dial.backToToday);
		dial.backToToday = undefined;

		if (selection.date !== null) {
			dial.backToToday = setTimeout(() => {
				dial.selection = { period: dial.selection.period, date: null };
				dial.backToToday = undefined;
				void this.#render(action);
			}, BACK_TO_TODAY_MS);
		}
	}

	#loadHistory(selection: WorkedTimeSelection): WorkedTimeHistory | undefined {
		const key = getHistoryKey(selection);
		if (key === null || selection.date === null) {
			return undefined;
		}

		const known = this.#history.get(key);
		if (known !== undefined || this.#client.state === null) {
			return known;
		}

		this.#history.set(key, "loading");
		const generation = this.#historyGeneration;
		void this.#client.getWorkedTime(selection.date, selection.period === "week" ? "week" : "day").then((result) => {
			if (generation === this.#historyGeneration) {
				this.#history.set(key, toHistory(result));
				this.#renderAll();
			}
		});

		return "loading";
	}

	#renderAll(): void {
		for (const action of this.actions) {
			if (action.isDial()) {
				void this.#render(action);
			}
		}
	}

	async #render(action: Dial): Promise<void> {
		const selection = this.#getDial(action).selection;
		const view = getWorkedTimeView(this.#client.state, selection, new Date(), this.#loadHistory(selection));

		await action.setFeedback({
			title: { value: view.title, color: view.color },
			value: { value: view.value, color: view.color },
			progress: { value: view.progress, bar_fill_c: view.progressColor }
		});
	}
}

function toHistory(result: CommandResult): WorkedTimeHistory {
	if (result.success && result.workedTime) {
		return result.workedTime;
	}

	return result.error?.startsWith("Unknown command") ? "unsupported" : "failed";
}
