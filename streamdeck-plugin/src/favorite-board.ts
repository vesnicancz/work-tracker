import type { KeyBoardContent } from "./key-board";
import type { CommandResult, Favorite } from "./protocol";

/**
 * In the order from the settings, which is the user's own. They are read locally, so no source can fail.
 */
export function readFavorites(result: CommandResult): KeyBoardContent<Favorite> | null {
	return result.favorites ? { items: result.favorites, failedSources: [] } : null;
}
