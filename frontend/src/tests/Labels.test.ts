/**
 * @file Labels.test.ts
 * @brief Tests warning end-time formatting and UTC date handling.
 */

import { describe, expect, it } from 'vitest';
import { formatUntil } from '../utils/Labels';

// End times for warnings must never look like "today" when they are not (issue #21).
describe('formatUntil', () => {
  const now = new Date(2026, 10, 25, 14, 0).getTime(); // Wed Nov 25, 2:00 PM local time

  it('shows only the time for later today', () => {
    const text = formatUntil(new Date(2026, 10, 25, 22, 12).toISOString(), now);
    expect(text).not.toMatch(/tomorrow|Nov/);
    expect(text).toMatch(/10:12/);
  });

  it('says "tomorrow" for tomorrow', () => {
    expect(formatUntil(new Date(2026, 10, 26, 8, 58).toISOString(), now)).toMatch(/^tomorrow .*8:58/);
  });

  it('names the day further out', () => {
    expect(formatUntil(new Date(2026, 10, 27, 9, 0).toISOString(), now)).toMatch(/Fri.*Nov.*27.*9:00/);
  });

  it('reads server times without a "Z" as UTC', () => {
    const utc = new Date(Date.UTC(2026, 10, 25, 23, 30));
    const withoutZ = utc.toISOString().replace('Z', '');
    expect(formatUntil(withoutZ, now)).toBe(formatUntil(utc.toISOString(), now));
  });
});
