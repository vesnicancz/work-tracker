import type { CommandResult } from "./protocol";

export type KeyBoardStatus = "loading" | "ready" | "failed";

export type KeyBoardContent<T> = {
	items: T[];
	failedSources: string[];
};

type FetchContent = () => Promise<CommandResult>;
type ReadContent<T> = (result: CommandResult, now: Date) => KeyBoardContent<T> | null;
type ChangeListener = () => void;

/**
 * Items spread over pages of as many items as there are keys to show them on.
 */
export class KeyBoard<T> {
	readonly #fetch: FetchContent;
	readonly #read: ReadContent<T>;
	readonly #listeners: ChangeListener[] = [];

	#items: T[] = [];
	#failedSources: string[] = [];
	#status: KeyBoardStatus = "loading";
	#page = 0;
	#pageSize = 1;
	#lastRequest = 0;

	constructor(fetch: FetchContent, read: ReadContent<T>) {
		this.#fetch = fetch;
		this.#read = read;
	}

	get status(): KeyBoardStatus {
		return this.#status;
	}

	get itemCount(): number {
		return this.#items.length;
	}

	get failedSources(): readonly string[] {
		return this.#failedSources;
	}

	get page(): number {
		return this.#page;
	}

	get pageCount(): number {
		return Math.max(1, Math.ceil(this.#items.length / this.#pageSize));
	}

	onChanged(listener: ChangeListener): void {
		this.#listeners.push(listener);
	}

	itemAt(position: number): T | null {
		return this.#items[this.#page * this.#pageSize + position] ?? null;
	}

	setPageSize(size: number): void {
		this.#pageSize = Math.max(1, size);
		this.#page = Math.min(this.#page, this.pageCount - 1);
		this.#notify();
	}

	nextPage(): void {
		this.#page = (this.#page + 1) % this.pageCount;
		this.#notify();
	}

	async load(now: Date = new Date()): Promise<void> {
		const request = ++this.#lastRequest;

		this.#items = [];
		this.#failedSources = [];
		this.#page = 0;
		this.#status = "loading";
		this.#notify();

		const result = await this.#fetch();

		// A newer load has taken over while this one was waiting
		if (request !== this.#lastRequest) {
			return;
		}

		const content = result.success ? this.#read(result, now) : null;
		if (content) {
			this.#items = content.items;
			this.#failedSources = content.failedSources;
			this.#status = "ready";
		} else {
			this.#status = "failed";
		}

		this.#notify();
	}

	#notify(): void {
		for (const listener of this.#listeners) {
			listener();
		}
	}
}
