/**
 * Local calendar days, the way WorkTracker counts them. A day is a Date at local midnight.
 */

export const WEEKDAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
export const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

const MILLISECONDS_PER_DAY = 86400000;

export function startOfDay(date: Date): Date {
	return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

export function addDays(day: Date, days: number): Date {
	return new Date(day.getFullYear(), day.getMonth(), day.getDate() + days);
}

/**
 * Monday, the same as in the application.
 */
export function startOfWeek(date: Date): Date {
	const day = startOfDay(date);
	const daysFromMonday = (day.getDay() + 6) % 7;
	return addDays(day, -daysFromMonday);
}

/**
 * Rounded, because a day with a change of daylight saving time has 23 or 25 hours.
 */
export function getCalendarDaysBetween(earlier: Date, later: Date): number {
	return Math.round((startOfDay(later).getTime() - startOfDay(earlier).getTime()) / MILLISECONDS_PER_DAY);
}

/**
 * As in the protocol: `2026-11-16`.
 */
export function toDateString(day: Date): string {
	return [day.getFullYear(), day.getMonth() + 1, day.getDate()].map((part, index) => String(part).padStart(index === 0 ? 4 : 2, "0")).join("-");
}

export function parseDateString(text: string): Date {
	const [year, month, day] = text.split("-").map(Number);
	return new Date(year, month - 1, day);
}

/**
 * `Nov 16`.
 */
export function formatMonthDay(day: Date): string {
	return `${MONTHS[day.getMonth()]} ${day.getDate()}`;
}
