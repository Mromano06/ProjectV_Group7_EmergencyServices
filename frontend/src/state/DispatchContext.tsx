import {
  createContext, useContext, useEffect, useReducer, useRef,
  type ReactNode,
} from 'react';
import { INITIAL_HOSPITALS, INITIAL_UNITS, makeNotice, makeWarning } from '../api/mockData';
import type {
  ActiveDispatch, CallRecord, EmergencyNotice, HospitalTally, LogEntry, Unit, UnitStatus,
  WeatherWarning,
} from '../types/models';
import { buildCompletionLog, rollDeaths } from '../utils/CallOutcome';
import { estimateTravelMs, getProgress } from '../utils/DispatchMath';
import { SEVERITY_LABEL, formatDuration, parseUtc } from '../utils/Labels';

type NewLog = Omit<LogEntry, 'id'>;
const MAX_LOGS = 300;

interface State {
  now: number;
  units: Unit[];
  notices: EmergencyNotice[];
  dispatches: ActiveDispatch[];
  selectedNoticeId: number | null;
  selectedUnitId: number | null;
  hospitals: HospitalTally[];
  logs: LogEntry[];                       // newest first
  records: CallRecord[];                  // finished calls
  responders: Record<number, string[]>;   // call sign list per call
  seenWarningIds: number[];
  nextLogId: number;
}

type Action =
  | { type: 'TICK'; now: number }
  | { type: 'ADD_NOTICE'; notice: EmergencyNotice }
  | { type: 'SELECT_NOTICE'; id: number | null }
  | { type: 'SELECT_UNIT'; id: number | null }
  | { type: 'SEND'; now: number; dispatchId: number; travelMs: number; dwellMs: number }
  | { type: 'CANCEL'; id: number; now: number }
  | { type: 'RECEIVE_WARNING'; warning: WeatherWarning; now: number }
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

function noticeLog(n: EmergencyNotice): NewLog {
  return {
    time: n.createdAt,
    level: n.severity === 1 ? 'danger' : n.severity === 2 ? 'warning' : 'info',
    category: 'call',
    message: `New call: ${n.title} at ${n.locationLabel}`,
    details: [`${SEVERITY_LABEL[n.severity]} · ${n.incidentType} · needs ${n.requiredUnitType}`],
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

// A unit is Available only when it has no dispatch
function syncUnits(units: Unit[], dispatches: ActiveDispatch[]): Unit[] {
  return units.map((u) => {
    const d = dispatches.find((x) => x.unitId === u.id);
    const status: UnitStatus = d
      ? d.phase === 'onScene' ? 'On Scene' : 'Dispatched'
      : u.status === 'Out of Service' ? 'Out of Service' : 'Available';
    return status === u.status ? u : { ...u, status };
  });
}

function reducer(state: State, action: Action): State {
  switch (action.type) {
    case 'ADD_NOTICE':
      return withLogs(
        { ...state, notices: [...state.notices, action.notice] },
        [noticeLog(action.notice)],
      );

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
      const notice = state.notices.find((n) => n.id === state.selectedNoticeId);
      const unit = state.units.find((u) => u.id === state.selectedUnitId);
      if (!notice || !unit || !unit.active || unit.status !== 'Available') return state;
      const { travelMs } = action;
      const dispatch: ActiveDispatch = {
        id: action.dispatchId,
        unitId: unit.id,
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
          units: syncUnits(state.units, dispatches),
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
      return withLogs(
        { ...state, dispatches, units: syncUnits(state.units, dispatches) },
        [{
          time: action.now,
          level: 'warning',
          category: 'unit',
          message: `${target.callSign} recalled to ${target.startLabel}`,
          details: [
            `Cancelled while ${target.phase === 'onScene' ? 'on scene' : 'en route'} at ${target.endLabel}`,
          ],
        }],
      );
    }

    case 'TICK': {
      let changed = false;
      let notices = state.notices;
      let hospitals = state.hospitals;
      let records = state.records;
      const dispatches: ActiveDispatch[] = [];
      const logs: NewLog[] = [];

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
        }
      }

      // Nothing moved: only the clock changes
      if (!changed) return { ...state, now: action.now };
      return withLogs(
        {
          ...state,
          now: action.now,
          dispatches,
          notices,
          hospitals,
          records,
          units: syncUnits(state.units, dispatches),
          selectedNoticeId: notices.some((n) => n.id === state.selectedNoticeId)
            ? state.selectedNoticeId
            : null,
        },
        logs,
      );
    }

    case 'RECEIVE_WARNING': {
      const w = action.warning;
      if (state.seenWarningIds.includes(w.id)) return state; // log each warning once
      const until = parseUtc(w.endsAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
      return withLogs(
        { ...state, seenWarningIds: [...state.seenWarningIds, w.id] },
        [{
          time: action.now,
          level: w.severity === 'Warning' ? 'danger' : w.severity === 'Watch' ? 'warning' : 'info',
          category: 'weather',
          message: `Weather ${w.severity.toLowerCase()}: ${w.type}, ${w.area}`,
          details: [`In effect until ${until}${w.affectsTransit ? ' · affects transit' : ''}`],
        }],
      );
    }

    case 'CLEAR_LOG':
      return { ...state, logs: [] };
  }
}

function createInitialState(): State {
  const notices = [makeNotice(1), makeNotice(2)];
  return withLogs(
    {
      now: Date.now(),
      units: INITIAL_UNITS,
      notices,
      dispatches: [],
      selectedNoticeId: null,
      selectedUnitId: null,
      hospitals: INITIAL_HOSPITALS,
      logs: [],
      records: [],
      responders: {},
      seenWarningIds: [],
      nextLogId: 1,
    },
    [
      { time: Date.now(), level: 'info', category: 'system', message: 'Dispatch console online' },
      ...notices.map(noticeLog),
    ],
  );
}

interface DispatchStore {
  now: number;
  units: Unit[];
  notices: EmergencyNotice[];
  dispatches: ActiveDispatch[];
  hospitals: HospitalTally[];
  logs: LogEntry[];
  records: CallRecord[];
  selectedNoticeId: number | null;
  selectedUnitId: number | null;
  selectNotice: (id: number | null) => void;
  selectUnit: (id: number | null) => void;
  sendSelected: () => void;
  cancelDispatch: (id: number) => void;
  addTestNotice: () => void;
  receiveWarning: (warning: WeatherWarning) => void;
  addTestWarning: () => void;
  clearLog: () => void;
}

const DispatchContext = createContext<DispatchStore | null>(null);

export function DispatchProvider({ children }: { children: ReactNode }) {
  const [state, dispatch] = useReducer(reducer, null, createInitialState);
  const nextNoticeId = useRef(3);
  const nextDispatchId = useRef(1);
  const nextWarningId = useRef(1);

  // One clock for everything: updates timers and advances phases
  useEffect(() => {
    const timer = setInterval(() => dispatch({ type: 'TICK', now: Date.now() }), 1000);
    return () => clearInterval(timer);
  }, []);

  const selectNotice = (id: number | null) => dispatch({ type: 'SELECT_NOTICE', id });
  const selectUnit = (id: number | null) => dispatch({ type: 'SELECT_UNIT', id });

  // Randomness is picked here, not in the reducer, so the reducer stays pure
  const sendSelected = () =>
    dispatch({
      type: 'SEND',
      now: Date.now(),
      dispatchId: nextDispatchId.current++,
      travelMs: estimateTravelMs(),
      dwellMs: (8 + Math.floor(Math.random() * 13)) * 1000, // random 8-20 s on scene
    });

  const cancelDispatch = (id: number) => dispatch({ type: 'CANCEL', id, now: Date.now() });

  const addTestNotice = () =>
    dispatch({ type: 'ADD_NOTICE', notice: makeNotice(nextNoticeId.current++) });

  // Later, a polling hook will call this for each warning from the server
  const receiveWarning = (warning: WeatherWarning) =>
    dispatch({ type: 'RECEIVE_WARNING', warning, now: Date.now() });

  const addTestWarning = () => receiveWarning(makeWarning(nextWarningId.current++));

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
        selectedNoticeId: state.selectedNoticeId,
        selectedUnitId: state.selectedUnitId,
        selectNotice, selectUnit, sendSelected, cancelDispatch, addTestNotice,
        receiveWarning, addTestWarning, clearLog,
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