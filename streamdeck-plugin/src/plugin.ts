import streamDeck from "@elgato/streamdeck";

import { CloseFavoritesKey, FavoriteKey, NextFavoritesKey, OpenFavoritesKey } from "./actions/favorites";
import { CloseSuggestionsKey, NextSuggestionsKey, OpenSuggestionsKey, SuggestionKey } from "./actions/suggestions";
import { WorkDial } from "./actions/work-dial";
import { WorkTrackerClient } from "./client";
import { readFavorites } from "./favorite-board";
import { KeyBoard } from "./key-board";
import { getPipePath } from "./pipe-path";
import { readSuggestions } from "./suggestion-board";

const client = new WorkTrackerClient(getPipePath());
const suggestionBoard = new KeyBoard(() => client.getSuggestions(), readSuggestions);
const favoriteBoard = new KeyBoard(() => client.getFavorites(), readFavorites);

streamDeck.actions.registerAction(new WorkDial(client));
streamDeck.actions.registerAction(new OpenSuggestionsKey());
streamDeck.actions.registerAction(new SuggestionKey(client, suggestionBoard));
streamDeck.actions.registerAction(new NextSuggestionsKey(suggestionBoard));
streamDeck.actions.registerAction(new CloseSuggestionsKey());
streamDeck.actions.registerAction(new OpenFavoritesKey());
streamDeck.actions.registerAction(new FavoriteKey(client, favoriteBoard));
streamDeck.actions.registerAction(new NextFavoritesKey(favoriteBoard));
streamDeck.actions.registerAction(new CloseFavoritesKey());

client.start();

streamDeck.connect();
