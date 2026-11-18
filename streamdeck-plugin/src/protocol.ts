/**
 * The wire format: one JSON object per line. Keep in sync with
 * plugins/WorkTracker.Plugin.StreamDeck/RemoteControlProtocol.cs; the files in test/fixtures/protocol
 * are checked by the tests of both sides.
 */

export type WorkItem = {
	ticketId: string | null;
	description: string | null;
	startedAt: string;
	/** Null while the work is being tracked; WorkTracker versions from before it don't send it. */
	endedAt?: string | null;
};

/**
 * An entry with a fixed start and end, such as a meeting, whose time covers this moment.
 */
export type ScheduledWork = {
	ticketId: string | null;
	description: string | null;
	startTime: string;
	endTime: string;
};

/**
 * The time worked as of the moment the state was sent. The week starts on Monday, and its target is
 * five days of the daily one.
 */
export type WorkedTime = {
	todaySeconds: number;
	weekSeconds: number;
	running: boolean;
	dailyTargetSeconds: number;
	weeklyTargetSeconds: number;
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
	scheduledWork: ScheduledWork | null;
	lastWork: WorkItem | null;
	workedTime: WorkedTime | null;
	pomodoro: PomodoroState;
	/** When the client received the state, in milliseconds since the epoch: running times count from it. */
	receivedAt: number;
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

// WorkTracker versions from before the protocol had a version don't send one, and those from before
// scheduled work and worked time don't send those
export type StateMessage = Omit<RemoteState, "scheduledWork" | "workedTime" | "receivedAt"> & {
	type: "state";
	protocol?: number;
	scheduledWork?: ScheduledWork | null;
	workedTime?: WorkedTime | null;
};

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
