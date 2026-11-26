/**
 * @file RoadClosures.ts
 * @brief Remembers which incidents we reported a closed road for (ES-FR-015), plus the
 *        choices offered in the road-closure form.
 */

import { useCallback, useState } from 'react';

/** What we show for an incident whose road we reported closed. */
export interface ActiveClosure {
  /** When the road is expected to reopen (ms since epoch). */
  until: number;
  restrictionType: string;
}

export const RESTRICTION_TYPES = ['Full closure', 'Lane closure', 'Detour'] as const;
export const REOPEN_HOURS = [1, 2, 4, 8, 24] as const;

const STORE_KEY = 'emergency.roadClosures';

function readStored(): Record<string, ActiveClosure> {
  try {
    const raw = sessionStorage.getItem(STORE_KEY);
    return raw ? (JSON.parse(raw) as Record<string, ActiveClosure>) : {};
  } catch {
    return {};
  }
}

function store(value: Record<string, ActiveClosure>) {
  try {
    sessionStorage.setItem(STORE_KEY, JSON.stringify(value));
  } catch {
    // storage blocked: still works until the page is refreshed
  }
}

/**
 * Which incidents we reported a closed road for. Our API doesn't keep this, so the console
 * remembers it for the browser tab (sessionStorage). Expired closures simply stop showing.
 */
export function useRoadClosures() {
  const [active, setActive] = useState<Record<string, ActiveClosure>>(readStored);

  const markActive = useCallback((incidentId: string, closure: ActiveClosure) => {
    setActive((prev) => {
      const next = { ...prev, [incidentId]: closure };
      store(next);
      return next;
    });
  }, []);

  const markCleared = useCallback((incidentId: string) => {
    setActive((prev) => {
      const next = { ...prev };
      delete next[incidentId];
      store(next);
      return next;
    });
  }, []);

  return { active, markActive, markCleared };
}