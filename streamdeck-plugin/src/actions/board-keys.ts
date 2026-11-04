import streamDeck, { type KeyDownEvent, SingletonAction, type WillAppearEvent, type WillDisappearEvent } from "@elgato/streamdeck";

import type { WorkTrackerClient } from "../client";
import type { KeyBoard } from "../key-board";
import { getEmptyKeyView, getNextKeyView, type KeyView, renderKeyImage } from "../key-view";
import type { CommandResult } from "../protocol";

type Key = KeyDownEvent["action"];

/**
 * Switches the whole device to a profile with a board. Lives in the user's own profile.
 */
export abstract class OpenBoardKey extends SingletonAction {
	readonly #profile: string;

	constructor(profile: string) {
		super();
		this.#profile = profile;
	}

	override async onKeyDown(ev: KeyDownEvent): Promise<void> {
		await streamDeck.profiles.switchToProfile(ev.action.device.id, this.#profile, 0);
	}
}

/**
 * One item of a board, on as many keys as the profile has them. After a press that worked it goes back
 * to the profile the user came from.
 */
export abstract class BoardItemKey<T> extends SingletonAction {
	protected readonly client: WorkTrackerClient;
	readonly #board: KeyBoard<T>;
	readonly #keys = new Map<string, Key>();

	constructor(client: WorkTrackerClient, board: KeyBoard<T>) {
		super();
		this.client = client;
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

	protected abstract getView(item: T): KeyView;

	protected abstract apply(item: T): Promise<CommandResult>;

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
		const item = this.#board.itemAt(position);
		if (item === null) {
			return;
		}

		const result = await this.apply(item);
		if (!result.success) {
			streamDeck.logger.warn(`Applying the item failed: ${result.error}`);
			await ev.action.showAlert();
			return;
		}

		await streamDeck.profiles.switchToProfile(ev.action.device.id);
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
			const item = this.#board.itemAt(position);

			void key.setImage(renderKeyImage(item !== null ? this.getView(item) : getEmptyKeyView()));
		});
	}
}

/**
 * Turns the pages of a board, and tells how loading it went.
 */
export abstract class NextBoardKey extends SingletonAction {
	readonly #board: KeyBoard<unknown>;

	constructor(board: KeyBoard<unknown>) {
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
 * Leaves a board without starting anything.
 */
export abstract class CloseBoardKey extends SingletonAction {
	override async onKeyDown(ev: KeyDownEvent): Promise<void> {
		await streamDeck.profiles.switchToProfile(ev.action.device.id);
	}
}
