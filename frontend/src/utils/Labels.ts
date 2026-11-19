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