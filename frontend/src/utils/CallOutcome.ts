/**
 * @file CallOutcome.ts
 * @brief Simulates call outcomes and creates completion log entries.
 */

import type { CallRecord, LogEntry, Severity } from '../types/models';
import { SEVERITY_LABEL, formatDuration } from './Labels';

export const random = (): number => Math.random();

const DEATH_CHANCE: Record<Severity, number> = { 1: 0.55, 2: 0.3, 3: 0.12, 4: 0.02 };
const MAX_DEATHS: Record<Severity, number> = { 1: 3, 2: 2, 3: 1, 4: 1 };

/**
 * Simulates the death count based on incident severity and response delay.
 * `rand` supplies random values so callers can control the simulation.
 */
export function rollDeaths(severity: Severity, waitedMs: number, rand: () => number): number {
  const slowPenalty = Math.min(0.3, (waitedMs / 60000) * 0.05); // slower help, higher risk
  const chance = Math.min(0.95, DEATH_CHANCE[severity] + slowPenalty);
  if (rand() >= chance) return 0;
  return 1 + Math.floor(rand() * MAX_DEATHS[severity]);
}

export function buildCompletionLog(r: CallRecord): Omit<LogEntry, 'id'> {
  return {
    time: r.completedAt,
    level: r.successful ? 'success' : 'danger',
    category: 'call',
    message: `${r.successful ? 'Call successful' : 'Call unsuccessful'}: ${r.title} at ${r.locationLabel}`,
    details: [
      `${SEVERITY_LABEL[r.severity]} ${r.incidentType.toLowerCase()} call · response time ${formatDuration(r.responseMs)}`,
      `Units sent: ${r.responders.join(', ')}`,
      r.deaths > 0 ? `Deaths: ${r.deaths}, recorded at ${r.hospitalName}` : 'Deaths: 0',
    ],
  };
}