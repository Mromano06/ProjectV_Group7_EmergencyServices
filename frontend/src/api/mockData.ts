import type { EmergencyNotice, IncidentType, Severity, Unit, UnitType } from '../types/models';

function unit(id: number, callSign: string, type: UnitType, homeStation: string): Unit {
  return { id, callSign, type, homeStation, status: 'Available', active: true };
}

export const INITIAL_UNITS: Unit[] = [
  unit(1, 'FireTruck 1', 'FireTruck', 'Station 1'),
  unit(2, 'Ladder 2', 'Ladder', 'Station 4'),
  unit(3, 'FireTruck 1', 'FireTruck', 'Station 1'),
  unit(4, 'FireTruck 3', 'FireTruck', 'Station 3'),
  unit(5, 'Medic 2', 'Ambulance', 'Station 2'),
  unit(6, 'Medic 5', 'Ambulance', 'Station 5'),
];

interface Template {
  title: string;
  incidentType: IncidentType;
  severity: Severity;
  requiredUnitType: UnitType;
}

const TEMPLATES: Template[] = [
  { title: 'Structure fire', incidentType: 'Fire', severity: 1, requiredUnitType: 'FireTruck' },
  { title: 'High-rise fire', incidentType: 'Fire', severity: 1, requiredUnitType: 'FireTruck' },
  { title: 'Cardiac arrest', incidentType: 'Medical', severity: 1, requiredUnitType: 'Ambulance' },
  { title: 'Vehicle collision', incidentType: 'Rescue', severity: 2, requiredUnitType: 'Ambulance' },
  { title: 'Cat stuck in tree', incidentType: 'Rescue', severity: 4, requiredUnitType: 'Ladder' },
];

const LOCATIONS = [
  '210 King St', '55 Oak Ave', '9 Elm Rd', '480 Queen Blvd', '12 Mill Lane', '1 Harbour Way',
];

const pick = <T,>(list: T[]): T => list[Math.floor(Math.random() * list.length)];

export function makeNotice(id: number): EmergencyNotice {
  const t = pick(TEMPLATES);
  return { id, ...t, locationLabel: pick(LOCATIONS), createdAt: Date.now() };
}