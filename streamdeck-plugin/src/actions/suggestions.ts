import { action } from "@elgato/streamdeck";

import { getSuggestionKeyView, isActiveSuggestion, type KeyView } from "../key-view";
import type { CommandResult, Suggestion } from "../protocol";
import { isSuggestionOver } from "../suggestion-board";
import { BoardItemKey, CloseBoardKey, NextBoardKey, OpenBoardKey } from "./board-keys";

// The profile distributed with the plugin, as named in the manifest
const SUGGESTIONS_PROFILE = "profiles/WorkTracker";

@action({ UUID: "com.vesnicancz.worktracker.suggestions" })
export class OpenSuggestionsKey extends OpenBoardKey {
	constructor() {
		super(SUGGESTIONS_PROFILE);
	}
}

/**
 * A press adds an event with its time slot and starts a ticket, the same as choosing them in the
 * application, or stops work that is already running.
 */
@action({ UUID: "com.vesnicancz.worktracker.suggestion" })
export class SuggestionKey extends BoardItemKey<Suggestion> {
	protected override getView(suggestion: Suggestion): KeyView {
		return getSuggestionKeyView(suggestion, isActiveSuggestion(suggestion, this.client.state?.activeWork), isSuggestionOver(suggestion, new Date()));
	}

	protected override apply(suggestion: Suggestion): Promise<CommandResult> {
		if (isActiveSuggestion(suggestion, this.client.state?.activeWork)) {
			return this.client.send("stopWork");
		}

		const work = { ticketId: suggestion.ticketId ?? undefined, description: suggestion.title };

		return suggestion.startTime !== null
			? this.client.send("addWork", { ...work, startTime: suggestion.startTime, endTime: suggestion.endTime ?? undefined })
			: this.client.send("startWork", work);
	}
}

@action({ UUID: "com.vesnicancz.worktracker.suggestions-next" })
export class NextSuggestionsKey extends NextBoardKey {}

@action({ UUID: "com.vesnicancz.worktracker.suggestions-back" })
export class CloseSuggestionsKey extends CloseBoardKey {}
