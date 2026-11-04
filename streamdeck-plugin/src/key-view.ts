import type { KeyBoard } from "./key-board";
import type { Favorite, Suggestion, WorkItem } from "./protocol";

// One Dark Pro, same as the application
const BACKGROUND_COLOR = "#282C34";
const TEXT_COLOR = "#ABB2BF";
const TICKET_COLOR = "#61AFEF";
const EVENT_COLOR = "#E5C07B";
const ACTIVE_COLOR = "#98C379";
const ERROR_COLOR = "#E06C75";
const OVER_COLOR = "#5C6370";
const TEMPLATE_COLOR = "#C678DD";

const KEY_SIZE = 144;
const HEADING_MAX_CHARACTERS = 10;
const LINE_MAX_CHARACTERS = 12;
const MAX_LINES = 3;

export type KeyLine = {
	text: string;
	color: string;
};

export type KeyView = {
	heading: KeyLine | null;
	lines: KeyLine[];
};

export function isActiveSuggestion(suggestion: Suggestion, activeWork: WorkItem | null | undefined): boolean {
	return !!activeWork && activeWork.ticketId === suggestion.ticketId && activeWork.description === suggestion.title;
}

/**
 * What a key shows for a suggestion: when it is, or which ticket, and the title below. The time and the
 * ticket differ in color, so that the source can be told at a glance. An event that is over is dimmed,
 * as in the application's dialog.
 */
export function getSuggestionKeyView(suggestion: Suggestion, isActive: boolean, isOver: boolean = false): KeyView {
	const headingColor = isActive ? ACTIVE_COLOR : isOver ? OVER_COLOR : suggestion.startTime !== null ? EVENT_COLOR : TICKET_COLOR;
	const lineColor = isActive ? ACTIVE_COLOR : isOver ? OVER_COLOR : TEXT_COLOR;
	const heading = getHeading(suggestion);

	return {
		heading: heading ? { text: truncate(heading, HEADING_MAX_CHARACTERS), color: headingColor } : null,
		lines: wrapText(suggestion.title, LINE_MAX_CHARACTERS, MAX_LINES).map((text) => ({ text, color: lineColor }))
	};
}

/**
 * A template only opens a dialog, so it never counts as the running work.
 */
export function isActiveFavorite(favorite: Favorite, activeWork: WorkItem | null | undefined): boolean {
	return !favorite.showAsTemplate && !!activeWork && activeWork.ticketId === favorite.ticketId && activeWork.description === favorite.description;
}

/**
 * What a key shows for a favorite: its ticket, if it has one, and its name below. A template differs in
 * color, because a press on it opens a dialog in the application rather than starting work.
 */
export function getFavoriteKeyView(favorite: Favorite, isActive: boolean): KeyView {
	const headingColor = isActive ? ACTIVE_COLOR : favorite.showAsTemplate ? TEMPLATE_COLOR : TICKET_COLOR;
	const lineColor = isActive ? ACTIVE_COLOR : favorite.showAsTemplate ? TEMPLATE_COLOR : TEXT_COLOR;

	return {
		heading: favorite.ticketId ? { text: truncate(favorite.ticketId, HEADING_MAX_CHARACTERS), color: headingColor } : null,
		lines: wrapText(favorite.name, LINE_MAX_CHARACTERS, MAX_LINES).map((text) => ({ text, color: lineColor }))
	};
}

export function getEmptyKeyView(): KeyView {
	return { heading: null, lines: [] };
}

/**
 * The key that turns the pages, which also tells how loading went.
 */
export function getNextKeyView(board: KeyBoard<unknown>): KeyView {
	if (board.status === "loading") {
		return { heading: { text: "Loading", color: TEXT_COLOR }, lines: [] };
	}

	if (board.status === "failed") {
		return { heading: { text: "Failed", color: ERROR_COLOR }, lines: [] };
	}

	const failures = board.failedSources.flatMap((source) => wrapText(`! ${source}`, LINE_MAX_CHARACTERS, 1))
		.map((text) => ({ text, color: ERROR_COLOR }));

	if (board.itemCount === 0) {
		return { heading: { text: "None", color: TEXT_COLOR }, lines: failures };
	}

	return {
		heading: { text: `${board.page + 1}/${board.pageCount}`, color: TEXT_COLOR },
		lines: [...(board.pageCount > 1 ? [{ text: "More", color: TEXT_COLOR }] : []), ...failures].slice(0, MAX_LINES)
	};
}

export function wrapText(text: string, maxCharacters: number, maxLines: number): string[] {
	const lines: string[] = [];
	let line = "";

	for (const word of text.trim().split(/\s+/).filter((part) => part.length > 0)) {
		let rest = word;

		if (line.length > 0 && line.length + 1 + rest.length <= maxCharacters) {
			line += ` ${rest}`;
			continue;
		}

		if (line.length > 0) {
			lines.push(line);
		}

		while (rest.length > maxCharacters) {
			lines.push(rest.slice(0, maxCharacters));
			rest = rest.slice(maxCharacters);
		}

		line = rest;
	}

	if (line.length > 0) {
		lines.push(line);
	}

	if (lines.length <= maxLines) {
		return lines;
	}

	const kept = lines.slice(0, maxLines);
	kept[maxLines - 1] = `${kept[maxLines - 1].slice(0, maxCharacters - 1)}…`;
	return kept;
}

/**
 * As a data URI, because key images are the only way to lay out text on a key. Base64, so that the
 * characters of a title cannot get lost on the way.
 */
export function renderKeyImage(view: KeyView): string {
	const parts = [`<rect width="${KEY_SIZE}" height="${KEY_SIZE}" fill="${BACKGROUND_COLOR}"/>`];
	let baseline = 38;

	if (view.heading) {
		parts.push(renderText(view.heading, baseline, 26, 700));
		baseline += 34;
	}

	for (const line of view.lines) {
		parts.push(renderText(line, baseline, 22, 400));
		baseline += 28;
	}

	const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${KEY_SIZE}" height="${KEY_SIZE}" viewBox="0 0 ${KEY_SIZE} ${KEY_SIZE}">${parts.join("")}</svg>`;
	return `data:image/svg+xml;base64,${Buffer.from(svg, "utf8").toString("base64")}`;
}

function getHeading(suggestion: Suggestion): string | null {
	// Sent with the local offset, so the digits in the text are already the local time
	return suggestion.startTime !== null ? suggestion.startTime.slice(11, 16) : suggestion.ticketId;
}

function truncate(text: string, maxCharacters: number): string {
	return text.length > maxCharacters ? `${text.slice(0, maxCharacters - 1)}…` : text;
}

function renderText(line: KeyLine, baseline: number, size: number, weight: number): string {
	return `<text x="10" y="${baseline}" font-family="Segoe UI, Helvetica, Arial, sans-serif" font-size="${size}" font-weight="${weight}" fill="${line.color}">${escapeXml(line.text)}</text>`;
}

function escapeXml(text: string): string {
	return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}
