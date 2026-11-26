/**
 * @file Permissions.ts
 * @brief Defines role checks used to control which actions the UI displays.
 */


import type { Role } from '../types/models';

// What the console SHOWS for each role (SH-S05). The API checks every request again (SH-S03).

/** Create calls, send and recall units (emergency:Dispatcher, emergency:Admin). */
export const canManageDispatch = (role: Role) =>
  role === 'Dispatcher' || role === 'Admin';

/** Admin-only tools, e.g. creating a test weather warning. */
export const isAdmin = (role: Role) => role === 'Admin';