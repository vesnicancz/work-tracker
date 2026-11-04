import streamDeck, { action, type KeyDownEvent, SingletonAction, type WillAppearEvent, type WillDisappearEvent } from "@elgato/streamdeck";

import type { WorkTrackerClient } from "../client";
import type { Suggestion } from "../protocol";
import { isSuggestionOver, type SuggestionBoard } from "../suggestion-board";
import { getEmptyKeyView, getNextKeyView, getSuggestionKeyView, isActiveSuggestion, renderKeyImage } from "../suggestion-key-view";

// The profile distributed with the plugin, as named in the manifest
const SUGGESTIONS_PROFILE = "profiles/WorkTracker";

type Key = KeyDownEvent["action"];

/**
 * Switches the whole device to the suggestions. Lives in the user's own profile.
 */
@action({ UUID: "com.vesnicancz.worktracker.suggestions" })
export class OpenSuggestionsKey extends SingletonAction {
	override async onKeyDown(ev: KeyDownEvent): Promise<void> {
		await streamDeck.profiles.switchToProfile(ev.action.device.id, SUGGESTIONS_PROFILE, 0);
	}
}

/**
 * One suggestion, on as many keys as the profile has them. A press adds an event with its time slot and
 * starts a ticket, the same as choosing them in the application, or stops work that is already running.
 * Then it goes back to the profile the user came from.
 */
@action({ UUID: "com.vesnicancz.worktracker.suggestion" })
export class SuggestionKey extends SingletonAction {
	readonly #client: WorkTrackerClient;
	readonly #board: SuggestionBoard;
	readonly #keys = new Map<string, Key>();

	constructor(client: WorkTrackerClient, board: SuggestionBoard) {
		super();
		this.#client = client;
		this.#board = board;

		board.onChanged(() => this.#renderAll());

		let wasConnected = false;
		client.onStateChanged((state) => {
			const isConnected = state !== null;
			if (isConnected && !wasConnected && this.#keys.size > 0) {
				void board.load();
			}
			wasConnected = isConnected;

			this.#renderAll();
		});
	}

	override onWillAppear(ev: WillAppearEvent): void {
		if (!ev.action.isKey()) {
			return;
		}

		// The first key to appear means the profile has just been opened
		const isOpening = this.#keys.size === 0;

		this.#keys.set(ev.action.id, ev.action);
		this.#board.setPageSize(this.#keys.size);

		if (isOpening) {
			void this.#board.load();
		}
	}

	override onWillDisappear(ev: WillDisappearEvent): void {
		this.#keys.delete(ev.action.id);
	}

	override async onKeyDown(ev: KeyDownEvent): Promise<void> {
		const position = this.#getOrderedKeys().findIndex((key) => key.id === ev.action.id);
		const suggestion = this.#board.itemAt(position);
		if (!suggestion) {
			return;
		}

		const result = await this.#apply(suggestion);
		if (!result.success) {
			streamDeck.logger.warn(`Applying the suggestion failed: ${result.error}`);
			await ev.action.showAlert();
			return;
		}

		await streamDeck.profiles.switchToProfile(ev.action.device.id);
	}

	#apply(suggestion: Suggestion) {
		if (isActiveSuggestion(suggestion, this.#client.state?.activeWork)) {
			return this.#client.send("stopWork");
		}

		const work = { ticketId: suggestion.ticketId ?? undefined, description: suggestion.title };

		return suggestion.startTime !== null
			? this.#client.send("addWork", { ...work, startTime: suggestion.startTime, endTime: suggestion.endTime ?? undefined })
			: this.#client.send("startWork", work);
	}

	/**
	 * Top to bottom and left to right, the way the keys are read.
	 */
	#getOrderedKeys(): Key[] {
		return [...this.#keys.values()].sort((a, b) =>
			(a.coordinates?.row ?? 0) - (b.coordinates?.row ?? 0) || (a.coordinates?.column ?? 0) - (b.coordinates?.column ?? 0));
	}

	#renderAll(): void {
		this.#getOrderedKeys().forEach((key, position) => {
			const suggestion = this.#board.itemAt(position);
			const view = suggestion
				? getSuggestionKeyView(suggestion, isActiveSuggestion(suggestion, this.#client.state?.activeWork), isSuggestionOver(suggestion, new Date()))
				: getEmptyKeyView();

			void key.setImage(renderKeyImage(view));
		});
	}
}

/**
 * Turns the pages of the suggestions, and tells how loading them went.
 */
@action({ UUID: "com.vesnicancz.worktracker.suggestions-next" })
export class NextSuggestionsKey extends SingletonAction {
	readonly #board: SuggestionBoard;

	constructor(board: SuggestionBoard) {
		super();
		this.#board = board;

		board.onChanged(() => this.#renderAll());
	}

	override async onWillAppear(ev: WillAppearEvent): Promise<void> {
		if (ev.action.isKey()) {
			await ev.action.setImage(renderKeyImage(getNextKeyView(this.#board)));
		}
	}

	override onKeyDown(): void {
		this.#board.nextPage();
	}

	#renderAll(): void {
		for (const key of this.actions) {
			if (key.isKey()) {
				void key.setImage(renderKeyImage(getNextKeyView(this.#board)));
			}
		}
	}
}

/**
 * Leaves the suggestions without starting anything.
 */
@action({ UUID: "com.vesnicancz.worktracker.suggestions-back" })
export class CloseSuggestionsKey extends SingletonAction {
	override async onKeyDown(ev: KeyDownEvent): Promise<void> {
		await streamDeck.profiles.switchToProfile(ev.action.device.id);
	}
}
