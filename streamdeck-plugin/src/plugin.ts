import streamDeck from "@elgato/streamdeck";

import { CloseSuggestionsKey, NextSuggestionsKey, OpenSuggestionsKey, SuggestionKey } from "./actions/suggestions";
import { WorkDial } from "./actions/work-dial";
import { WorkTrackerClient } from "./client";
import { getPipePath } from "./pipe-path";
import { SuggestionBoard } from "./suggestion-board";

const client = new WorkTrackerClient(getPipePath());
const suggestionBoard = new SuggestionBoard(() => client.getSuggestions());

streamDeck.actions.registerAction(new WorkDial(client));
streamDeck.actions.registerAction(new OpenSuggestionsKey());
streamDeck.actions.registerAction(new SuggestionKey(client, suggestionBoard));
streamDeck.actions.registerAction(new NextSuggestionsKey(suggestionBoard));
streamDeck.actions.registerAction(new CloseSuggestionsKey());

client.start();

streamDeck.connect();
