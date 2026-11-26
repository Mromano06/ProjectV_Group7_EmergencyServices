/**
 * @file DispatchMath.ts
 * @brief Calculates simulated dispatch travel times and unit route positions.
 */


import type { ActiveDispatch } from '../types/models';


/** Returns a simulated one-way travel time between 20 and 64 seconds. */

/** Calculates a unit's position along its route: 0 at the station and 1 at the call. */


// Travel time (20-65 s) from a station to a place. 
export function estimateTravelMs(): number {
  return (20 + Math.floor(Math.random() * 45)) * 1000;
}

// Position along the line: 0 = home station, 1 = destination
export function getProgress(d: ActiveDispatch, now: number): number {
  const t =
    d.phaseDurationMs <= 0
      ? 1
      : Math.min(1, Math.max(0, (now - d.phaseStartedAt) / d.phaseDurationMs));
  if (d.phase === 'outbound') return t;
  if (d.phase === 'onScene') return 1;
  return d.returnFromProgress * (1 - t);
}