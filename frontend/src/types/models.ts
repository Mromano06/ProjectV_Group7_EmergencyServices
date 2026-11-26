/**
 * @file models.ts
 * @brief Defines frontend incident, unit, dispatch, warning, and log data types.
 */

// Names and values match the Emergency API exactly (backend/Enums), so data can be used as-is.
export const INCIDENT_TYPES = ['Fire', 'Medical', 'Rescue', 'Hazmat', 'Other'] as const;
export type IncidentType = (typeof INCIDENT_TYPES)[number];

export type Severity = 1 | 2 | 3 | 4; // 1 = Critical, 4 = Low

export const INCIDENT_STATUSES = [
  'Reported', 'Dispatched', 'EnRoute', 'OnScene', 'Resolved', 'Closed',
] as const;
export type IncidentStatus = (typeof INCIDENT_STATUSES)[number];

export const UNIT_TYPES = ['Engine', 'Ambulance', 'Ladder'] as const;
export type UnitType = (typeof UNIT_TYPES)[number];

export type UnitStatus = 'Available' | 'Dispatched' | 'OnScene' | 'OutOfService';
export type ErStatus = 'Open' | 'Diverting' | 'Closed';
export type Role = 'Admin' | 'Dispatcher' | 'Responder' | 'Viewer';

export type DispatchPhase = 'outbound' | 'onScene' | 'returning';

export type WarningSeverity = 'Statement' | 'Advisory' | 'Watch' | 'Warning';

export interface WeatherWarning {
  id: string;
  type: string;
  severity: WarningSeverity;
  area: string;
  description?: string;
  startsAt: string;
  endsAt: string;
  status: 'Scheduled' | 'Active' | 'Ended';
  source?: string;
  affectsTransit: boolean;
}

export type LogLevel = 'info' | 'success' | 'warning' | 'danger';
export type LogCategory = 'call' | 'unit' | 'weather' | 'system';

export interface LogEntry {
  id: number;
  time: number;
  level: LogLevel;
  category: LogCategory;
  message: string;
  details?: string[];
}

export interface HospitalTally {
  id: string;
  name: string;
  erStatus: ErStatus;
  deaths: number;
}

export interface CallRecord {
  noticeId: string;
  title: string;
  incidentType: IncidentType;
  severity: Severity;
  locationLabel: string;
  requiredUnitType: UnitType;
  responders: string[];
  responseMs: number; // call reported and correct unit on scene
  deaths: number;
  hospitalName: string | null;
  successful: boolean;
  completedAt: number;
}

export interface Unit {
  id: string;
  callSign: string;
  type: UnitType;
  homeStation: string;
  status: UnitStatus;
  active: boolean;
}

export interface ActiveDispatch {
  id: number;                  // local id for the animation
  unitId: string;
  assignmentId: string | null; // the API's assignment, used to release (recall) the unit
  callSign: string;
  unitType: UnitType;
  startLabel: string;          // home station
  endLabel: string;            // call location
  noticeId: string | null;     // the incident this unit was sent to
  phase: DispatchPhase;
  phaseStartedAt: number;      // ms since epoch
  phaseDurationMs: number;
  travelMs: number;            // full one-way trip
  dwellMs: number;             // random wait at the scene
  returnFromProgress: number;  // 0..1, where the return trip starts
}

/** An open incident as the console shows it. */
export interface EmergencyNotice {
  id: string;
  title: string;
  incidentType: IncidentType;
  severity: Severity;
  status: IncidentStatus;
  locationLabel: string;
  requiredUnitType: UnitType;
  createdAt: number;
}

export interface Stats {
  byType: Record<string, number>;
  byStatus: Record<string, number>;
  avgResponseMinutes: number;
  deaths: number;
}
