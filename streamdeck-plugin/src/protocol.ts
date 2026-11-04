/**
 * The wire format: one JSON object per line. Keep in sync with
 * plugins/WorkTracker.Plugin.StreamDeck/RemoteControlProtocol.cs.
 */

export type WorkItem = {
	ticketId: string | null;
	description: string | null;
	startedAt: string;
};

export type PomodoroPhase = "idle" | "work" | "shortBreak" | "longBreak";

export type PomodoroState = {
	phase: PomodoroPhase;
	remainingSeconds: number;
	completedPomodoros: number;
	pomodorosBeforeLongBreak: number;
};

export type RemoteState = {
	activeWork: WorkItem | null;
	lastWork: WorkItem | null;
	pomodoro: PomodoroState;
};

export type Suggestion = {
	title: string;
	ticketId: string | null;
	startTime: string | null;
	endTime: string | null;
	source: string;
};

export type Suggestions = {
	items: Suggestion[];
	failedSources: string[];
};

export type Favorite = {
	name: string;
	ticketId: string | null;
	description: string | null;
	showAsTemplate: boolean;
};

export type CommandResult = {
	success: boolean;
	error: string | null;
	suggestions?: Suggestions | null;
	favorites?: Favorite[] | null;
};

export type StateMessage = RemoteState & { type: "state" };

export type ResultMessage = CommandResult & { type: "result"; id: number | null };

export type ServerMessage = StateMessage | ResultMessage;

export type Command =
	| "toggleWork"
	| "startWork"
	| "stopWork"
	| "resumeWork"
	| "shiftStart"
	| "addWork"
	| "getSuggestions"
	| "getFavorites"
	| "showNewWorkDialog"
	| "startPomodoro"
	| "stopPomodoro"
	| "skipPomodoro";

export type CommandArguments = {
	ticketId?: string;
	description?: string;
	minutes?: number;
	startTime?: string;
	endTime?: string;
};
