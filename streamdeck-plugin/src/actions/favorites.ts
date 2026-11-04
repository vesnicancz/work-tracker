import { action } from "@elgato/streamdeck";

import { getFavoriteKeyView, isActiveFavorite, type KeyView } from "../key-view";
import type { CommandResult, Favorite } from "../protocol";
import { BoardItemKey, CloseBoardKey, NextBoardKey, OpenBoardKey } from "./board-keys";

// The profile distributed with the plugin, as named in the manifest
const FAVORITES_PROFILE = "profiles/WorkTrackerFavorites";

@action({ UUID: "com.vesnicancz.worktracker.favorites" })
export class OpenFavoritesKey extends OpenBoardKey {
	constructor() {
		super(FAVORITES_PROFILE);
	}
}

/**
 * A press does what the favorite does in the tray menu: a template opens the new entry dialog and anything
 * else starts work. Unlike there, a press stops work that is already running, as on the suggestion keys.
 */
@action({ UUID: "com.vesnicancz.worktracker.favorite" })
export class FavoriteKey extends BoardItemKey<Favorite> {
	protected override getView(favorite: Favorite): KeyView {
		return getFavoriteKeyView(favorite, isActiveFavorite(favorite, this.client.state?.activeWork));
	}

	protected override apply(favorite: Favorite): Promise<CommandResult> {
		const work = { ticketId: favorite.ticketId ?? undefined, description: favorite.description ?? undefined };

		if (favorite.showAsTemplate) {
			return this.client.send("showNewWorkDialog", work);
		}

		return isActiveFavorite(favorite, this.client.state?.activeWork)
			? this.client.send("stopWork")
			: this.client.send("startWork", work);
	}
}

@action({ UUID: "com.vesnicancz.worktracker.favorites-next" })
export class NextFavoritesKey extends NextBoardKey {}

@action({ UUID: "com.vesnicancz.worktracker.favorites-back" })
export class CloseFavoritesKey extends CloseBoardKey {}
