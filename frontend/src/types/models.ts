export const INCIDENT_TYPES = ['Fire', 'Medical', 'Rescue', 'Other'] as const;
export type IncidentType = (typeof INCIDENT_TYPES)[number];

export type Severity = 1 | 2 | 3 | 4; // 1 = Critical, 4 = Low

export const INCIDENT_STATUSES = [
  'Reported', 'Dispatched', 'En Route', 'On Scene', 'Resolved', 'Closed',
] as const;
export type IncidentStatus = (typeof INCIDENT_STATUSES)[number];

export const UNIT_TYPES = ['FireTruck', 'Ambulance', 'Ladder'] as const;
export type UnitType = (typeof UNIT_TYPES)[number];

export type UnitStatus = 'Available' | 'Dispatched' | 'On Scene' | 'Out of Service';
export type ErStatus = 'Open' | 'Diverting' | 'Closed';
export type Role = 'Dispatcher' | 'Responder' | 'Admin' | 'Viewer';

export type DispatchPhase = 'outbound' | 'onScene' | 'returning';


export interface Location {
  address?: string;
  lat: number;
  lng: number;
}

export interface StatusHistoryEntry {
  oldStatus: IncidentStatus;
  newStatus: IncidentStatus;
  user: string;
  time: string; // ISO date string
}

export interface Assignment {
  unitId: number;
  callSign: string;
  assignedAt: string;
}

export interface Incident {
  id: number;
  type: IncidentType;
  severity: Severity;
  status: IncidentStatus;
  location: Location;
  description: string;
  reportedAt: string;
  // Optional: the server omits these for Viewers (ES-NFR-007)
  callerName?: string;
  callerPhone?: string;
  history?: StatusHistoryEntry[];
  assignments?: Assignment[];
}

export interface Unit {
  id: number;
  callSign: string;
  type: UnitType;
  homeStation: string;
  status: UnitStatus;
  active: boolean;
}

export interface Station {
  id: number;
  name: string;
  type: 'Fire' | 'EMS';
  location: Location;
  units?: Unit[];
}

export interface Hospital {
  id: number;
  name: string;
  location: Location;
  erStatus: ErStatus;
  availableBeds: number;
  updatedAt: string;
}

export interface WeatherWarning {
  id: number;
  type: string;
  severity: string;
  area: string;
  start: string;
  end: string;
}

export interface ActiveDispatch {
  id: number;
  unitId: number;
  callSign: string;
  unitType: UnitType;
  startLabel: string;          // home station
  endLabel: string;            // call location
  noticeId: number | null;     // the call this unit was sent to
  phase: DispatchPhase;
  phaseStartedAt: number;      // ms since epoch
  phaseDurationMs: number;
  travelMs: number;            // full one-way trip
  dwellMs: number;             // random wait at the scene
  returnFromProgress: number;  // 0..1, where the return trip starts
}

export interface EmergencyNotice {
  id: number;
  title: string;
  incidentType: IncidentType;
  severity: Severity;
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