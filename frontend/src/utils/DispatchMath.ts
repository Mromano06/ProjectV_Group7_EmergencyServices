import type { ActiveDispatch } from '../types/models';

// Mock travel time (30-90 s) from a station to a place. The server's ETA replaces this.
export function estimateTravelMs(homeStation: string, destination: string): number {
  let h = 0;
  for (const ch of `${homeStation}|${destination}`) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
  return (30 + (h % 61)) * 1000;
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