import {
  createContext, useCallback, useContext, useEffect, useReducer, useRef,
  type ReactNode,
} from 'react';
import { ApiError } from '../api/apiClient';
import {
  emergencyApi, OPEN_STATUSES, toHospital, toNotice, toUnit, toWarning,
} from '../api/emergencyApi';
import { makeTestIncident } from '../api/mockData';
import type {
  ActiveDispatch, CallRecord, EmergencyNotice, HospitalTally, LogEntry, Unit, UnitStatus,
  WeatherWarning,
} from '../types/models';
import { buildCompletionLog, rollDeaths } from '../utils/CallOutcome';
import { estimateTravelMs, getProgress } from '../utils/DispatchMath';
import { SEVERITY_LABEL, formatDuration, parseUtc } from '../utils/Labels';

// How often the console asks the API for fresh data. ES-FR-004: a new incident must
// appear within 15 seconds without reloading the page.
export const DATA_POLL_MS = 5000;
export const WARNING_POLL_MS = 30000;

type NewLog = Omit<LogEntry, 'id'>;
const MAX_LOGS = 300;

export type Connection = 'connecting' | 'online' | 'offline';
export type WarningsState = 'loading' | 'ready' | 'error';

/** API work the reducer asks for; run by the effect runner in DispatchProvider. */
type Effect =
  | { id: number; kind: 'complete'; record: CallRecord }
  | { id: number; kind: 'release'; incidentId: string; assignmentId: string; callSign: string };
type NewEffect = Effect extends infer E ? (E extends Effect ? Omit<E, 'id'> : never) : never;

interface State {
  now: number;
  serverUnits: Unit[];                    // as the API last reported them
  units: Unit[];                          // serverUnits + local animation status
  notices: EmergencyNotice[];             // open incidents
  dispatches: ActiveDispatch[];
  selectedNoticeId: string | null;
  selectedUnitId: string | null;
  hospitals: HospitalTally[];
  logs: LogEntry[];                       // newest first
  records: CallRecord[];                  // finished calls (this session)
  responders: Record<string, string[]>;   // call sign list per incident
  knownNoticeIds: string[] | null;        // null until the first successful load
  closingIds: string[];                   // finished here, waiting for the API to close them
  seenWarningIds: string[];
  warnings: WeatherWarning[];
  warningsState: WarningsState;
  warningsError: string | null;
  connection: Connection;
  busy: boolean;                          // a send is in progress
  effects: Effect[];
  nextEffectId: number;
  nextLogId: number;
}

type Action =
  | { type: 'TICK'; now: number }
  | { type: 'SYNC'; now: number; units: Unit[]; notices: EmergencyNotice[]; hospitals: HospitalTally[] }
  | { type: 'CONNECTION_LOST'; now: number; message: string }
  | { type: 'WARNINGS'; now: number; warnings: WeatherWarning[] }
  | { type: 'WARNINGS_FAILED'; message: string }
  | { type: 'SELECT_NOTICE'; id: string | null }
  | { type: 'SELECT_UNIT'; id: string | null }
  | { type: 'SEND'; now: number; dispatchId: number; travelMs: number; dwellMs: number;
      notice: EmergencyNotice; unit: Unit; assignmentId: string | null }
  | { type: 'CANCEL'; id: number; now: number }
  | { type: 'LOG'; entry: NewLog }
  | { type: 'BUSY'; busy: boolean }
  | { type: 'EFFECTS_TAKEN'; ids: number[] }
  | { type: 'CLEAR_LOG' };

// Adds entries (given oldest -> newest) to the top of the log
function withLogs(state: State, entries: NewLog[]): State {
  if (entries.length === 0) return state;
  let id = state.nextLogId;
  const added: LogEntry[] = entries.map((e) => ({ ...e, id: id++ }));
  return {
    ...state,
    nextLogId: id,
    logs: [...added.reverse(), ...state.logs].slice(0, MAX_LOGS),
  };
}

function withEffects(state: State, effects: NewEffect[]): State {
  if (effects.length === 0) return state;
  let id = state.nextEffectId;
  const added = effects.map((e) => ({ ...e, id: id++ }) as Effect);
  return { ...state, nextEffectId: id, effects: [...state.effects, ...added] };
}

function noticeLog(n: EmergencyNotice): NewLog {
  return {
    time: n.createdAt,
    level: n.severity === 1 ? 'danger' : n.severity === 2 ? 'warning' : 'info',
    category: 'call',
    message: `New call: ${n.title} at ${n.locationLabel}`,
    details: [`${SEVERITY_LABEL[n.severity]} · ${n.incidentType} · needs ${n.requiredUnitType}`],
  };
}

function warningLog(w: WeatherWarning, now: number): NewLog {
  const until = parseUtc(w.endsAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  return {
    time: now,
    level: w.severity === 'Warning' ? 'danger' : w.severity === 'Watch' ? 'warning' : 'info',
    category: 'weather',
    message: `Weather ${w.severity.toLowerCase()}: ${w.type}, ${w.area}`,
    details: [`In effect until ${until}${w.affectsTransit ? ' · affects transit' : ''}`],
  };
}

// Moves a dispatch through outbound -> onScene -> returning -> (removed)
function advance(d: ActiveDispatch, now: number) {
  let cur = d;
  let arrivedAt: number | null = null;
  for (;;) {
    const end = cur.phaseStartedAt + cur.phaseDurationMs;
    if (now < end) return { dispatch: cur as ActiveDispatch | null, arrivedAt };
    if (cur.phase === 'outbound') {
      arrivedAt = end;
      cur = { ...cur, phase: 'onScene', phaseStartedAt: end, phaseDurationMs: cur.dwellMs };
    } else if (cur.phase === 'onScene') {
      cur = {
        ...cur, phase: 'returning', phaseStartedAt: end,
        phaseDurationMs: cur.travelMs, returnFromProgress: 1,
      };
    } else {
      return { dispatch: null, arrivedAt }; // home again
    }
  }
}

// A unit that is out on a local dispatch shows the animation's status;
// otherwise it shows what the API says (e.g. sent by another dispatcher).
function syncUnits(serverUnits: Unit[], dispatches: ActiveDispatch[]): Unit[] {
  return serverUnits.map((u) => {
    const d = dispatches.find((x) => x.unitId === u.id);
    if (!d) return u;
    const status: UnitStatus = d.phase === 'onScene' ? 'OnScene' : 'Dispatched';
    return status === u.status ? u : { ...u, status };
  });
}

function reducer(state: State, action: Action): State {
  switch (action.type) {
    case 'SYNC': {
      const open = action.notices.filter(
        (n) => OPEN_STATUSES.includes(n.status) && !state.closingIds.includes(n.id),
      );
      const logs: NewLog[] = [];
      if (state.knownNoticeIds === null) {
        logs.push({ time: action.now, level: 'info', category: 'system', message: 'Dispatch console online' });
        logs.push(...open.map(noticeLog));
      } else {
        if (state.connection === 'offline') {
          logs.push({ time: action.now, level: 'success', category: 'system', message: 'Connection to the API restored' });
        }
        logs.push(...open.filter((n) => !state.knownNoticeIds!.includes(n.id)).map(noticeLog));
      }
      const serverIds = new Set(action.notices.map((n) => n.id));
      const deaths = new Map(state.hospitals.map((h) => [h.id, h]));
      return withLogs(
        {
          ...state,
          now: action.now,
          connection: 'online',
          serverUnits: action.units,
          units: syncUnits(action.units, state.dispatches),
          notices: open,
          hospitals: action.hospitals.map((h) => ({ ...h, deaths: deaths.get(h.id)?.deaths ?? h.deaths })),
          knownNoticeIds: [...new Set([...(state.knownNoticeIds ?? []), ...open.map((n) => n.id)])],
          closingIds: state.closingIds.filter((id) => serverIds.has(id)),
          selectedNoticeId: open.some((n) => n.id === state.selectedNoticeId) ? state.selectedNoticeId : null,
          selectedUnitId: action.units.some((u) => u.id === state.selectedUnitId) ? state.selectedUnitId : null,
        },
        logs,
      );
    }

    case 'CONNECTION_LOST':
      if (state.connection === 'offline') return state; // log it once, not every poll
      return withLogs({ ...state, connection: 'offline' }, [{
        time: action.now, level: 'danger', category: 'system',
        message: 'Lost connection to the Emergency API', details: [action.message],
      }]);

    case 'WARNINGS': {
      const fresh = action.warnings.filter((w) => !state.seenWarningIds.includes(w.id));
      return withLogs(
        {
          ...state,
          warnings: action.warnings,
          warningsState: 'ready',
          warningsError: null,
          seenWarningIds: [...state.seenWarningIds, ...fresh.map((w) => w.id)],
        },
        fresh.map((w) => warningLog(w, action.now)),
      );
    }

    case 'WARNINGS_FAILED':
      return { ...state, warningsState: state.warningsState === 'ready' ? 'ready' : 'error', warningsError: action.message };

    case 'SELECT_NOTICE':
      return { ...state, selectedNoticeId: action.id };

    case 'SELECT_UNIT': {
      if (action.id !== null) {
        const u = state.units.find((x) => x.id === action.id);
        if (!u || u.status !== 'Available') return state;
      }
      return { ...state, selectedUnitId: action.id };
    }

    case 'SEND': {
      const { notice, unit, travelMs } = action;
      const dispatch: ActiveDispatch = {
        id: action.dispatchId,
        unitId: unit.id,
        assignmentId: action.assignmentId,
        callSign: unit.callSign,
        unitType: unit.type,
        startLabel: unit.homeStation,
        endLabel: notice.locationLabel,
        noticeId: notice.id,
        phase: 'outbound',
        phaseStartedAt: action.now,
        phaseDurationMs: travelMs,
        travelMs,
        dwellMs: action.dwellMs,
        returnFromProgress: 1,
      };
      const dispatches = [...state.dispatches, dispatch];
      const wrongType = unit.type !== notice.requiredUnitType;
      return withLogs(
        {
          ...state,
          dispatches,
          units: syncUnits(state.serverUnits, dispatches),
          notices: state.notices.map((n) => (n.id === notice.id ? { ...n, status: 'EnRoute' } : n)),
          selectedUnitId: null, // keep the destination so you can send more units
          responders: {
            ...state.responders,
            [notice.id]: [...(state.responders[notice.id] ?? []), unit.callSign],
          },
        },
        [{
          time: action.now,
          level: wrongType ? 'warning' : 'info',
          category: 'unit',
          message: `${unit.callSign} dispatched to ${notice.locationLabel}`,
          details: [
            `From ${unit.homeStation} · ETA ${formatDuration(travelMs)} · ${notice.title}`,
            ...(wrongType ? [`Not the requested type: call needs ${notice.requiredUnitType}`] : []),
          ],
        }],
      );
    }

    case 'CANCEL': {
      const target = state.dispatches.find((d) => d.id === action.id && d.phase !== 'returning');
      if (!target) return state;
      const from = getProgress(target, action.now); // 1 if already on scene
      const dispatches = state.dispatches.map((d): ActiveDispatch =>
        d.id !== target.id
          ? d
          : {
              ...d,
              phase: 'returning',
              phaseStartedAt: action.now,
              phaseDurationMs: Math.round(from * d.travelMs),
              returnFromProgress: from,
            },
      );
      const release: NewEffect[] =
        target.assignmentId && target.noticeId && state.notices.some((n) => n.id === target.noticeId)
          ? [{ kind: 'release', incidentId: target.noticeId, assignmentId: target.assignmentId, callSign: target.callSign }]
          : [];
      return withEffects(
        withLogs(
          { ...state, dispatches, units: syncUnits(state.serverUnits, dispatches) },
          [{
            time: action.now,
            level: 'warning',
            category: 'unit',
            message: `${target.callSign} recalled to ${target.startLabel}`,
            details: [
              `Cancelled while ${target.phase === 'onScene' ? 'on scene' : 'en route'} at ${target.endLabel}`,
            ],
          }],
        ),
        release,
      );
    }

    case 'TICK': {
      let changed = false;
      let notices = state.notices;
      let hospitals = state.hospitals;
      let records = state.records;
      let closingIds = state.closingIds;
      const dispatches: ActiveDispatch[] = [];
      const logs: NewLog[] = [];
      const effects: NewEffect[] = [];

      for (const d of state.dispatches) {
        const result = advance(d, action.now);
        if (result.dispatch !== d) changed = true;
        if (result.dispatch) dispatches.push(result.dispatch);

        if (result.arrivedAt !== null) {
          const arrivedAt = result.arrivedAt;
          logs.push({
            time: arrivedAt, level: 'info', category: 'unit',
            message: `${d.callSign} arrived at ${d.endLabel}`,
          });

          const notice = d.noticeId === null ? undefined : notices.find((n) => n.id === d.noticeId);
          if (notice && notice.requiredUnitType === d.unitType) {
            // The correct unit type has arrived: the call is complete
            const rand = Math.random;
            const responseMs = arrivedAt - notice.createdAt;
            const deaths = rollDeaths(notice.severity, responseMs, rand);

            const open = hospitals.filter((h) => h.erStatus === 'Open');
            const candidates = open.length > 0 ? open : hospitals;
            const hospital =
              deaths > 0 && candidates.length > 0
                ? candidates[Math.floor(rand() * candidates.length)]
                : null;
            if (hospital) {
              hospitals = hospitals.map((h) =>
                h.id === hospital.id ? { ...h, deaths: h.deaths + deaths } : h,
              );
            }

            const record: CallRecord = {
              noticeId: notice.id,
              title: notice.title,
              incidentType: notice.incidentType,
              severity: notice.severity,
              locationLabel: notice.locationLabel,
              requiredUnitType: notice.requiredUnitType,
              responders: state.responders[notice.id] ?? [d.callSign],
              responseMs,
              deaths,
              hospitalName: hospital ? hospital.name : null,
              successful: deaths === 0,
              completedAt: arrivedAt,
            };
            records = [...records, record];
            notices = notices.filter((n) => n.id !== notice.id);
            closingIds = [...closingIds, notice.id];
            effects.push({ kind: 'complete', record }); // On Scene -> Resolved -> report (closes it)
            logs.push(buildCompletionLog(record));
          } else if (notice) {
            logs.push({
              time: arrivedAt, level: 'warning', category: 'call',
              message: `Call still open: ${notice.title} needs ${notice.requiredUnitType}`,
              details: [`${d.callSign} (${d.unitType}) is not the requested type`],
            });
          }
        }

        if (result.dispatch === null) {
          logs.push({
            time: action.now, level: 'info', category: 'unit',
            message: `${d.callSign} back at ${d.startLabel}, available`,
          });
          // Came home while its call is still open (wrong unit type): free it in the API too.
          if (d.assignmentId && d.noticeId && notices.some((n) => n.id === d.noticeId)) {
            effects.push({ kind: 'release', incidentId: d.noticeId, assignmentId: d.assignmentId, callSign: d.callSign });
          }
        }
      }

      // Nothing moved: only the clock changes
      if (!changed) return { ...state, now: action.now };
      return withEffects(
        withLogs(
          {
            ...state,
            now: action.now,
            dispatches,
            notices,
            hospitals,
            records,
            closingIds,
            units: syncUnits(state.serverUnits, dispatches),
            selectedNoticeId: notices.some((n) => n.id === state.selectedNoticeId)
              ? state.selectedNoticeId
              : null,
          },
          logs,
        ),
        effects,
      );
    }

    case 'LOG':
      return withLogs(state, [action.entry]);

    case 'BUSY':
      return { ...state, busy: action.busy };

    case 'EFFECTS_TAKEN':
      return { ...state, effects: state.effects.filter((e) => !action.ids.includes(e.id)) };

    case 'CLEAR_LOG':
      return { ...state, logs: [] };
  }
}

function createInitialState(): State {
  return {
    now: Date.now(),
    serverUnits: [],
    units: [],
    notices: [],
    dispatches: [],
    selectedNoticeId: null,
    selectedUnitId: null,
    hospitals: [],
    logs: [],
    records: [],
    responders: {},
    knownNoticeIds: null,
    closingIds: [],
    seenWarningIds: [],
    warnings: [],
    warningsState: 'loading',
    warningsError: null,
    connection: 'connecting',
    busy: false,
    effects: [],
    nextEffectId: 1,
    nextLogId: 1,
  };
}

/** Runs an API call, treating the listed HTTP statuses as "fine, nothing to do". */
async function tolerate(call: () => Promise<unknown>, okStatuses: number[]): Promise<void> {
  try {
    await call();
  } catch (e) {
    if (e instanceof ApiError && okStatuses.includes(e.status)) return;
    throw e;
  }
}

/** Some roles may not read every list (e.g. Responders can't list units): show it empty instead of failing. */
async function ifAllowed<T>(call: Promise<T>, fallback: T): Promise<T> {
  try {
    return await call;
  } catch (e) {
    if (e instanceof ApiError && e.status === 403) return fallback;
    throw e;
  }
}

const messageOf = (e: unknown) => (e instanceof Error ? e.message : String(e));

interface DispatchStore {
  now: number;
  units: Unit[];
  notices: EmergencyNotice[];
  dispatches: ActiveDispatch[];
  hospitals: HospitalTally[];
  logs: LogEntry[];
  records: CallRecord[];
  warnings: WeatherWarning[];
  warningsState: WarningsState;
  warningsError: string | null;
  connection: Connection;
  busy: boolean;
  selectedNoticeId: string | null;
  selectedUnitId: string | null;
  selectNotice: (id: string | null) => void;
  selectUnit: (id: string | null) => void;
  sendSelected: () => Promise<void>;
  cancelDispatch: (id: number) => void;
  addTestNotice: () => Promise<void>;
  addTestWarning: () => Promise<void>;
  refresh: () => Promise<void>;
  clearLog: () => void;
}

const DispatchContext = createContext<DispatchStore | null>(null);

export function DispatchProvider({ children }: { children: ReactNode }) {
  const [state, dispatch] = useReducer(reducer, null, createInitialState);
  const stateRef = useRef(state);
  const nextDispatchId = useRef(1);
  const handledEffects = useRef(new Set<number>());

  useEffect(() => {
    stateRef.current = state;
  }, [state]);

  const log = useCallback((entry: Omit<NewLog, 'time'>) =>
    dispatch({ type: 'LOG', entry: { time: Date.now(), ...entry } }), []);

  // ---------- Loading from the API ----------

  const refresh = useCallback(async () => {
    try {
      const [units, stations, incidents, hospitals] = await Promise.all([
        ifAllowed(emergencyApi.listUnits(), { items: [], page: 1, pageSize: 0, totalCount: 0 }),
        ifAllowed(emergencyApi.listStations(), []),
        ifAllowed(emergencyApi.listOpenIncidents(), { items: [], page: 1, pageSize: 0, totalCount: 0 }),
        ifAllowed(emergencyApi.listHospitals(), []),
      ]);
      const stationNames = new Map(stations.map((s) => [s.id, s.name]));
      dispatch({
        type: 'SYNC',
        now: Date.now(),
        units: units.items.map((u) => toUnit(u, stationNames)),
        notices: incidents.items.map(toNotice),
        hospitals: hospitals.map((h) => toHospital(h)),
      });
    } catch (e) {
      dispatch({ type: 'CONNECTION_LOST', now: Date.now(), message: messageOf(e) });
    }
  }, []);

  const loadWarnings = useCallback(async () => {
    try {
      const list = await emergencyApi.activeWarnings();
      dispatch({ type: 'WARNINGS', now: Date.now(), warnings: list.map(toWarning) });
    } catch (e) {
      dispatch({ type: 'WARNINGS_FAILED', message: messageOf(e) });
    }
  }, []);

  useEffect(() => {
    void refresh();
    void loadWarnings();
    const data = setInterval(() => void refresh(), DATA_POLL_MS);
    const weather = setInterval(() => void loadWarnings(), WARNING_POLL_MS);
    const clock = setInterval(() => dispatch({ type: 'TICK', now: Date.now() }), 1000);
    return () => {
      clearInterval(data);
      clearInterval(weather);
      clearInterval(clock);
    };
  }, [refresh, loadWarnings]);

  // ---------- API work requested by the reducer ----------

  useEffect(() => {
    const fresh = state.effects.filter((e) => !handledEffects.current.has(e.id));
    if (fresh.length === 0) return;
    fresh.forEach((e) => handledEffects.current.add(e.id));
    dispatch({ type: 'EFFECTS_TAKEN', ids: fresh.map((e) => e.id) });

    for (const effect of fresh) {
      void (async () => {
        try {
          if (effect.kind === 'complete') {
            const r = effect.record;
            // 409 = already in that status (e.g. another dispatcher moved it on).
            await tolerate(() => emergencyApi.changeStatus(r.noticeId, 'OnScene'), [409]);
            await tolerate(() => emergencyApi.changeStatus(r.noticeId, 'Resolved'), [409]);
            try {
              await emergencyApi.fileReport(r.noticeId, {
                outcome: r.successful ? 'Successful' : 'Unsuccessful',
                injuries: 0,
                deaths: r.deaths,
                hydrantWaterLitres: 0,
                notes: `Units: ${r.responders.join(', ')}. Response time ${formatDuration(r.responseMs)}.`,
              });
            } catch (e) {
              if (e instanceof ApiError && e.status === 403) {
                log({
                  level: 'warning', category: 'call',
                  message: `${r.title} is resolved; a Responder or Admin must file the after-action report to close it`,
                });
              } else if (!(e instanceof ApiError && e.status === 409)) { // 409 = already reported
                throw e;
              }
            }
          } else {
            // 404/409 = already released (e.g. the incident was closed).
            await tolerate(() => emergencyApi.releaseAssignment(effect.incidentId, effect.assignmentId), [404, 409]);
          }
        } catch (e) {
          log({
            level: 'danger', category: 'system',
            message: effect.kind === 'complete'
              ? `Could not close "${effect.record.title}" in the system`
              : `Could not release ${effect.callSign} in the system`,
            details: [messageOf(e)],
          });
        } finally {
          void refresh();
        }
      })();
    }
  }, [state.effects, log, refresh]);

  // ---------- User actions ----------

  const selectNotice = (id: string | null) => dispatch({ type: 'SELECT_NOTICE', id });
  const selectUnit = (id: string | null) => dispatch({ type: 'SELECT_UNIT', id });

  const sendSelected = async () => {
    const s = stateRef.current;
    const notice = s.notices.find((n) => n.id === s.selectedNoticeId);
    const unit = s.units.find((u) => u.id === s.selectedUnitId);
    if (s.busy || !notice || !unit || !unit.active || unit.status !== 'Available') return;

    dispatch({ type: 'BUSY', busy: true });
    try {
      const assignments = await emergencyApi.assignUnits(notice.id, [unit.id]);
      // Dispatched -> En Route. 409 = it is already en route (another unit went first).
      await tolerate(() => emergencyApi.changeStatus(notice.id, 'EnRoute'), [409]);
      dispatch({
        type: 'SEND',
        now: Date.now(),
        dispatchId: nextDispatchId.current++,
        travelMs: estimateTravelMs(),
        dwellMs: (8 + Math.floor(Math.random() * 13)) * 1000, // random 8-20 s on scene
        notice,
        unit,
        assignmentId: assignments[0]?.id ?? null,
      });
    } catch (e) {
      log({
        level: 'danger', category: 'unit',
        message: `Could not send ${unit.callSign} to ${notice.locationLabel}`,
        details: [messageOf(e)],
      });
    } finally {
      dispatch({ type: 'BUSY', busy: false });
      void refresh();
    }
  };

  const cancelDispatch = (id: number) => dispatch({ type: 'CANCEL', id, now: Date.now() });

  const addTestNotice = async () => {
    try {
      await emergencyApi.createIncident(makeTestIncident());
      await refresh();
    } catch (e) {
      log({ level: 'danger', category: 'call', message: 'Could not create a test call', details: [messageOf(e)] });
    }
  };

  const addTestWarning = async () => {
    try {
      await emergencyApi.simulateWarning();
      await loadWarnings();
    } catch (e) {
      log({ level: 'danger', category: 'weather', message: 'Could not create a test warning', details: [messageOf(e)] });
    }
  };

  const clearLog = () => dispatch({ type: 'CLEAR_LOG' });

  return (
    <DispatchContext.Provider
      value={{
        now: state.now,
        units: state.units,
        notices: state.notices,
        dispatches: state.dispatches,
        hospitals: state.hospitals,
        logs: state.logs,
        records: state.records,
        warnings: state.warnings,
        warningsState: state.warningsState,
        warningsError: state.warningsError,
        connection: state.connection,
        busy: state.busy,
        selectedNoticeId: state.selectedNoticeId,
        selectedUnitId: state.selectedUnitId,
        selectNotice, selectUnit, sendSelected, cancelDispatch, addTestNotice,
        addTestWarning, refresh, clearLog,
      }}
    >
      {children}
    </DispatchContext.Provider>
  );
}

export function useDispatchStore() {
  const ctx = useContext(DispatchContext);
  if (!ctx) throw new Error('useDispatchStore must be used inside DispatchProvider');
  return ctx;
}
