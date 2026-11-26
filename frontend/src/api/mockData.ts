/**
 * @file mockData.ts
 * @brief Creates sample incident payloads for the dashboard's test-call action.
 */

// Random test calls for the "+ Test call" button. They are created as REAL incidents through
// POST /incidents, exactly like a call forwarded by Security, so the whole flow is exercised.
// (Units, stations and hospitals now come from the API – see backend/Data/SeedData.cs.)
import type { CreateIncidentBody } from './emergencyApi';
import type { IncidentType, Severity } from '../types/models';

interface Template {
  title: string;
  incidentType: IncidentType;
  severity: Severity;
}

// The unit type each call needs comes from its incident type (requiredUnitTypeFor):
// Fire -> Engine, Medical -> Ambulance, Rescue -> Ladder.
const TEMPLATES: Template[] = [
  { title: 'Structure fire', incidentType: 'Fire', severity: 1 },
  { title: 'High-rise fire', incidentType: 'Fire', severity: 1 },
  { title: 'Cardiac arrest', incidentType: 'Medical', severity: 1 },
  { title: 'Vehicle collision', incidentType: 'Medical', severity: 2 },
  { title: 'Cat stuck in tree', incidentType: 'Rescue', severity: 4 },
];

// Made-up Cambridge, ON addresses with real-ish coordinates (needed for nearest-unit suggestions).
const LOCATIONS = [
  { address: '210 King St, Cambridge', latitude: 43.3615, longitude: -80.3140 },
  { address: '55 Oak Ave, Cambridge', latitude: 43.3880, longitude: -80.3290 },
  { address: '9 Elm Rd, Cambridge', latitude: 43.4100, longitude: -80.3050 },
  { address: '480 Queen Blvd, Cambridge', latitude: 43.3720, longitude: -80.3410 },
  { address: '12 Mill Lane, Cambridge', latitude: 43.3960, longitude: -80.2900 },
  { address: '1 Harbour Way, Cambridge', latitude: 43.4230, longitude: -80.3330 },
];

const pick = <T,>(list: T[]): T => list[Math.floor(Math.random() * list.length)];

export function makeTestIncident(): CreateIncidentBody {
  const t = pick(TEMPLATES);
  return {
    type: t.incidentType,
    severity: t.severity,
    location: pick(LOCATIONS),
    description: t.title,
    callerName: 'Test caller',
    callerPhone: '519-555-0100',
  };
}
