import type { CommandResult, Suggestion } from "./protocol";

export type SuggestionBoardStatus = "loading" | "ready" | "failed";

type FetchSuggestions = () => Promise<CommandResult>;
type ChangeListener = () => void;

/**
 * Whether a suggestion with a time slot has passed, by the same rule as the application's dialog: an
 * event without an end is over once it has started.
 */
export function isSuggestionOver(suggestion: Suggestion, now: Date): boolean {
	return suggestion.startTime !== null && Date.parse(suggestion.endTime ?? suggestion.startTime) <= now.getTime();
}

/**
 * The order on the keys: what is still ahead in the calendar by time, then what is over from the most
 * recent, which can still be added afterwards, then everything without a time slot in the order the
 * plugins gave it.
 */
export function arrangeSuggestions(items: Suggestion[], now: Date): Suggestion[] {
	const events = items.filter((item) => item.startTime !== null);
	const byStart = (a: Suggestion, b: Suggestion) => Date.parse(a.startTime!) - Date.parse(b.startTime!);

	return [
		...events.filter((item) => !isSuggestionOver(item, now)).sort(byStart),
		...events.filter((item) => isSuggestionOver(item, now)).sort((a, b) => byStart(b, a)),
		...items.filter((item) => item.startTime === null)
	];
}

/**
 * The suggestions spread over pages of as many items as there are keys to show them on.
 */
export class SuggestionBoard {
	readonly #fetch: FetchSuggestions;
	readonly #listeners: ChangeListener[] = [];

	#items: Suggestion[] = [];
	#failedSources: string[] = [];
	#status: SuggestionBoardStatus = "loading";
	#page = 0;
	#pageSize = 1;
	#lastRequest = 0;

	constructor(fetch: FetchSuggestions) {
		this.#fetch = fetch;
	}

	get status(): SuggestionBoardStatus {
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

	itemAt(position: number): Suggestion | null {
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

		if (result.success && result.suggestions) {
			this.#items = arrangeSuggestions(result.suggestions.items, now);
			this.#failedSources = result.suggestions.failedSources;
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
