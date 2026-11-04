import type { KeyBoardContent } from "./key-board";
import type { CommandResult, Suggestion } from "./protocol";

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

export function readSuggestions(result: CommandResult, now: Date): KeyBoardContent<Suggestion> | null {
	return result.suggestions
		? { items: arrangeSuggestions(result.suggestions.items, now), failedSources: result.suggestions.failedSources }
		: null;
}
