/**
 * @file Labels.ts
 * @brief Formats dates, times, severity labels, and durations for the dashboard.
 */

import type { Severity } from '../types/models';

export const SEVERITY_LABEL: Record<Severity, string> = {
  1: 'Critical', 2: 'High', 3: 'Medium', 4: 'Low',
};

export function formatDuration(ms: number): string {
  const total = Math.max(0, Math.round(ms / 1000));
  const m = Math.floor(total / 60);
  const s = total % 60;
  return m > 0 ? `${m}m ${String(s).padStart(2, '0')}s` : `${s}s`;
}

export function formatClock(ms: number): string {
  return new Date(ms).toLocaleTimeString([], { hour12: false });
}

// Server dates are UTC but may arrive without a trailing "Z";
// without one, JavaScript would read them as local time.
export function parseUtc(value: string): Date {
  return /(Z|[+-]\d{2}:?\d{2})$/.test(value) ? new Date(value) : new Date(`${value}Z`);
}

/**
 * When a warning ends, as people say it: "10:12 PM" today, "tomorrow 8:58 AM",
 * or "Fri, Nov 27, 8:58 AM" further out – so an end time is never mistaken for today.
 */
export function formatUntil(value: string, now: number = Date.now()): string {
  const end = parseUtc(value);
  const time = end.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
  const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const days = Math.round((startOfDay(end) - startOfDay(new Date(now))) / 86_400_000);
  if (days === 0) return time;
  if (days === 1) return `tomorrow ${time}`;
  const date = end.toLocaleDateString([], { weekday: 'short', month: 'short', day: 'numeric' });
  return `${date}, ${time}`;
}
