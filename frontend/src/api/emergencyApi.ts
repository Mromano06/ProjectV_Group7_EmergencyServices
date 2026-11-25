// Every Emergency Services endpoint the dispatch console uses, plus converters
// from the API's JSON to the shapes the components use.
import { apiRequest } from './apiClient';
import type {
  EmergencyNotice, ErStatus, HospitalTally, IncidentStatus, IncidentType, Severity, Unit,
  UnitStatus, UnitType, WarningSeverity, WeatherWarning,
} from '../types/models';
import { parseUtc } from '../utils/Labels';

const BASE = '/api/emergency/v1';

// ---------- JSON shapes sent by the API (camelCase of the C# DTOs) ----------

export interface PagedResult<T> { items: T[]; page: number; pageSize: number; totalCount: number }

export interface UnitDto { id: string; callSign: string; type: string; stationId: string; status: string }

export interface StationDto {
  id: string; name: string; type: string; latitude: number; longitude: number;
  address: string | null; units: UnitDto[];
}

export interface HospitalDto {
  id: string; name: string;
  location: { latitude: number; longitude: number; address: string | null };
  erStatus: string;          // Open / Diverting / Closed (ES-FR-012)
  availableBeds: number;
  updatedAt: string;
  distanceKm: number | null; // only when asked with ?near=
}

export interface IncidentSummaryDto {
  id: string; type: string; severity: number; status: string; latitude: number; longitude: number;
  reportedAt: string; address: string | null; description: string | null;
}

export interface AssignmentDto {
  id: string; incidentId: string; unitId: string; assignedAt: string;
  arrivedAt: string | null; releasedAt: string | null;
}

export interface WeatherWarningDto {
  id: string; type: string; severity: string; area: string; description: string | null;
  startsAt: string; endsAt: string; status: string; source: string; affectsTransit: boolean;
}

export interface CreateIncidentBody {
  type: IncidentType;
  severity: Severity;
  location: { latitude: number; longitude: number; address: string };
  description: string;
  callerName?: string;
  callerPhone?: string;
}

export interface AfterActionReportBody {
  outcome: string; injuries: number; deaths: number; hydrantWaterLitres: number; notes?: string;
}

// ---------- Endpoints ----------

export const emergencyApi = {
  listUnits: () => apiRequest<PagedResult<UnitDto>>('GET', `${BASE}/units?pageSize=100`),
  listStations: () => apiRequest<StationDto[]>('GET', `${BASE}/stations`),
  listHospitals: () => apiRequest<HospitalDto[]>('GET', `${BASE}/hospitals`),
  /** The live board: every incident that is not Closed, most severe and oldest first (ES-FR-004). */
  listOpenIncidents: () =>
    apiRequest<PagedResult<IncidentSummaryDto>>('GET', `${BASE}/incidents?pageSize=100`),
  createIncident: (body: CreateIncidentBody) =>
    apiRequest<{ id: string }>('POST', `${BASE}/incidents`, body),
  assignUnits: (incidentId: string, unitIds: string[]) =>
    apiRequest<AssignmentDto[]>('POST', `${BASE}/incidents/${incidentId}/assignments`, { unitIds }),
  changeStatus: (incidentId: string, status: IncidentStatus) =>
    apiRequest<unknown>('PATCH', `${BASE}/incidents/${incidentId}/status`, { status }),
  fileReport: (incidentId: string, body: AfterActionReportBody) =>
    apiRequest<unknown>('POST', `${BASE}/incidents/${incidentId}/report`, body),
  releaseAssignment: (incidentId: string, assignmentId: string) =>
    apiRequest<AssignmentDto>('POST',
      `${BASE}/incidents/${incidentId}/assignments/${assignmentId}/release`),
  activeWarnings: () => apiRequest<WeatherWarningDto[]>('GET', `${BASE}/weather-warnings?active=true`),
  simulateWarning: () => apiRequest<WeatherWarningDto>('POST', `${BASE}/weather-warnings/simulate`),
};

// ---------- Converters ----------

/** Which unit type a call needs. The API doesn't store this; it's a console rule. */
export function requiredUnitTypeFor(type: IncidentType): UnitType {
  switch (type) {
    case 'Medical': return 'Ambulance';
    case 'Rescue': return 'Ladder';
    default: return 'Engine'; // Fire, Hazmat, Other
  }
}

/** Calls that still need units (Resolved and Closed ones are finished). */
export const OPEN_STATUSES: IncidentStatus[] = ['Reported', 'Dispatched', 'EnRoute', 'OnScene'];

export function toNotice(dto: IncidentSummaryDto): EmergencyNotice {
  const type = dto.type as IncidentType;
  return {
    id: dto.id,
    title: dto.description?.trim() || `${type} call`,
    incidentType: type,
    severity: Math.min(4, Math.max(1, dto.severity)) as Severity,
    status: dto.status as IncidentStatus,
    locationLabel: dto.address?.trim() || `${dto.latitude.toFixed(4)}, ${dto.longitude.toFixed(4)}`,
    requiredUnitType: requiredUnitTypeFor(type),
    createdAt: parseUtc(dto.reportedAt).getTime(),
  };
}

export function toUnit(dto: UnitDto, stationNames: ReadonlyMap<string, string>): Unit {
  const status = dto.status as UnitStatus;
  return {
    id: dto.id,
    callSign: dto.callSign,
    type: dto.type as UnitType,
    homeStation: stationNames.get(dto.stationId) ?? 'Unknown station',
    status,
    active: status !== 'OutOfService',
  };
}

const ER_STATUS: Record<string, ErStatus> = { Open: 'Open', Diverting: 'Diverting', Closed: 'Closed' };

/** Keeps the console's own death tally for each hospital (the API doesn't track deaths per hospital). */
export function toHospital(dto: HospitalDto, previous?: HospitalTally): HospitalTally {
  return {
    id: dto.id,
    name: dto.name,
    erStatus: ER_STATUS[dto.erStatus] ?? 'Closed', // unknown status: don't send patients there
    deaths: previous?.deaths ?? 0,
  };
}

export function toWarning(dto: WeatherWarningDto): WeatherWarning {
  return {
    id: dto.id,
    type: dto.type,
    severity: dto.severity as WarningSeverity,
    area: dto.area,
    description: dto.description ?? undefined,
    startsAt: dto.startsAt,
    endsAt: dto.endsAt,
    status: dto.status as WeatherWarning['status'],
    source: dto.source,
    affectsTransit: dto.affectsTransit,
  };
}
