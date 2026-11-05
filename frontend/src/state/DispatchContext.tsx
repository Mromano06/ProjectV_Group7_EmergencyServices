import {
  createContext, useCallback, useContext, useEffect, useReducer, useRef, useState,
  type ReactNode,
} from 'react';
import { INITIAL_UNITS, makeNotice } from '../api/mockData';
import type { ActiveDispatch, EmergencyNotice, Unit, UnitStatus } from '../types/models';
import { estimateTravelMs, getProgress } from '../utils/DispatchMath';

interface State {
  units: Unit[];
  notices: EmergencyNotice[];
  dispatches: ActiveDispatch[];
  selectedNoticeId: number | null;
  selectedUnitId: number | null;
}

type Action =
  | { type: 'TICK'; now: number }
  | { type: 'ADD_NOTICE'; notice: EmergencyNotice }
  | { type: 'SELECT_NOTICE'; id: number | null }
  | { type: 'SELECT_UNIT'; id: number | null }
  | { type: 'SEND'; now: number; dispatchId: number; dwellMs: number }
  | { type: 'CANCEL'; id: number; now: number };

// Moves a dispatch through outbound -> onScene -> returning -> (removed)
function advance(d: ActiveDispatch, now: number) {
  let cur = d;
  let arrived = false;
  for (;;) {
    const end = cur.phaseStartedAt + cur.phaseDurationMs;
    if (now < end) return { dispatch: cur as ActiveDispatch | null, arrived };
    if (cur.phase === 'outbound') {
      arrived = true;
      cur = { ...cur, phase: 'onScene', phaseStartedAt: end, phaseDurationMs: cur.dwellMs };
    } else if (cur.phase === 'onScene') {
      cur = {
        ...cur, phase: 'returning', phaseStartedAt: end,
        phaseDurationMs: cur.travelMs, returnFromProgress: 1,
      };
    } else {
      return { dispatch: null, arrived }; // home again
    }
  }
}

// A unit is Available only when it has no dispatch
function syncUnits(units: Unit[], dispatches: ActiveDispatch[]): Unit[] {
  let changed = false;
  const next = units.map((u) => {
    const d = dispatches.find((x) => x.unitId === u.id);
    const status: UnitStatus = d
      ? d.phase === 'onScene' ? 'On Scene' : 'Dispatched'
      : u.status === 'Out of Service' ? 'Out of Service' : 'Available';
    if (status === u.status) return u;
    changed = true;
    return { ...u, status };
  });
  return changed ? next : units;
}

function reducer(state: State, action: Action): State {
  switch (action.type) {
    case 'ADD_NOTICE':
      return { ...state, notices: [...state.notices, action.notice] };

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
      const travelMs = estimateTravelMs(unit.homeStation, notice.locationLabel);
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
      return {
        ...state,
        dispatches,
        units: syncUnits(state.units, dispatches),
        selectedUnitId: null, // keep the destination so you can send more units
      };
    }

    case 'CANCEL': {
      let changed = false;
      const dispatches = state.dispatches.map((d): ActiveDispatch => {
        if (d.id !== action.id || d.phase === 'returning') return d;
        changed = true;
        const from = getProgress(d, action.now); // 1 if already on scene
        return {
          ...d,
          phase: 'returning',
          phaseStartedAt: action.now,
          phaseDurationMs: Math.round(from * d.travelMs),
          returnFromProgress: from,
        };
      });
      if (!changed) return state;
      return { ...state, dispatches, units: syncUnits(state.units, dispatches) };
    }

    case 'TICK': {
      let changed = false;
      let notices = state.notices;
      const dispatches: ActiveDispatch[] = [];
      for (const d of state.dispatches) {
        const result = advance(d, action.now);
        if (result.dispatch !== d) changed = true;
        if (result.dispatch) dispatches.push(result.dispatch);
        // A call clears only when the REQUIRED unit type reaches the scene
        if (result.arrived && d.noticeId !== null) {
          const notice = notices.find((n) => n.id === d.noticeId);
          if (notice && notice.requiredUnitType === d.unitType) {
            notices = notices.filter((n) => n.id !== notice.id);
          }
        }
      }
      if (!changed) return state;
      return {
        ...state,
        dispatches,
        notices,
        units: syncUnits(state.units, dispatches),
        selectedNoticeId: notices.some((n) => n.id === state.selectedNoticeId)
          ? state.selectedNoticeId
          : null,
      };
    }
  }
}

function createInitialState(): State {
  return {
    units: INITIAL_UNITS,
    notices: [makeNotice(1), makeNotice(2)],
    dispatches: [],
    selectedNoticeId: null,
    selectedUnitId: null,
  };
}

interface DispatchStore {
  now: number;
  units: Unit[];
  notices: EmergencyNotice[];
  dispatches: ActiveDispatch[];
  selectedNoticeId: number | null;
  selectedUnitId: number | null;
  selectNotice: (id: number | null) => void;
  selectUnit: (id: number | null) => void;
  sendSelected: () => void;
  cancelDispatch: (id: number) => void;
  addTestNotice: () => void;
}

const DispatchContext = createContext<DispatchStore | null>(null);

export function DispatchProvider({ children }: { children: ReactNode }) {
  const [state, dispatch] = useReducer(reducer, null, createInitialState);
  const [now, setNow] = useState(() => Date.now());
  const nextNoticeId = useRef(3);
  const nextDispatchId = useRef(1);

  // One clock for everything: updates timers and advances phases
  useEffect(() => {
    const timer = setInterval(() => {
      const t = Date.now();
      setNow(t);
      dispatch({ type: 'TICK', now: t });
    }, 1000);
    return () => clearInterval(timer);
  }, []);

  const selectNotice = useCallback((id: number | null) => dispatch({ type: 'SELECT_NOTICE', id }), []);
  const selectUnit = useCallback((id: number | null) => dispatch({ type: 'SELECT_UNIT', id }), []);

  const sendSelected = useCallback(() => {
    dispatch({
      type: 'SEND',
      now: Date.now(),
      dispatchId: nextDispatchId.current++,
      dwellMs: (8 + Math.floor(Math.random() * 13)) * 1000, // random 8-20 s on scene
    });
  }, []);

  const cancelDispatch = useCallback(
    (id: number) => dispatch({ type: 'CANCEL', id, now: Date.now() }),
    [],
  );

  const addTestNotice = useCallback(
    () => dispatch({ type: 'ADD_NOTICE', notice: makeNotice(nextNoticeId.current++) }),
    [],
  );

  return (
    <DispatchContext.Provider
      value={{
        now, ...state,
        selectNotice, selectUnit, sendSelected, cancelDispatch, addTestNotice,
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