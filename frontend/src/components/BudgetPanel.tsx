/**
 * @file BudgetPanel.tsx
 * @brief Displays budget totals and provides Admin-only expense and funding forms.
 */


import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { ApiError } from '../api/apiClient';
import { emergencyApi, type BudgetDto } from '../api/emergencyApi';
import { useAuth } from '../auth/AuthContext';
import { isAdmin } from '../auth/Permissions';
import '../styles/Panels.css';

// ES-FR-025: our budget from Budget & Finance – allocated, spent and remaining – plus
// expense statements and funding requests (Admin only). All calls go through our own API,
// which talks to Budget (SH-C07, SH-C08, BF-07).

const REFRESH_MS = 60_000;     // the budget changes rarely; refresh once a minute
const LOW_BUDGET_SHARE = 0.1;  // Budget warns under 10% left (BF-13)

function money(value: number, currency: string | null | undefined) {
  return new Intl.NumberFormat('en-CA', {
    style: 'currency', currency: currency || 'CAD', minimumFractionDigits: 0, maximumFractionDigits: 2,
  }).format(value);
}

/** A dollar amount typed by the user: must be more than 0 with at most 2 decimals (cents). */
function parseAmount(text: string): number | string {
  const trimmed = text.trim();
  if (!/^\d+(\.\d{1,2})?$/.test(trimmed)) return 'Enter an amount in dollars, e.g. 1250 or 1250.50.';
  const value = Number(trimmed);
  return value > 0 ? value : 'The amount must be more than $0.';
}

const messageOf = (e: unknown) => (e instanceof ApiError || e instanceof Error ? e.message : String(e));

type FormMessage = { ok: boolean; text: string } | null;

const FUNDING_STATUS: Record<string, string> = {
  '0': 'Pending', '1': 'Approved', '2': 'Rejected', Pending: 'Pending', Approved: 'Approved', Rejected: 'Rejected',
};

export default function BudgetPanel() {
  const { user } = useAuth();
  const admin = isAdmin(user.role);

  const [budget, setBudget] = useState<BudgetDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [expenseAmount, setExpenseAmount] = useState('');
  const [expenseDescription, setExpenseDescription] = useState('');
  const [expenseMessage, setExpenseMessage] = useState<FormMessage>(null);
  const [fundingAmount, setFundingAmount] = useState('');
  const [fundingReason, setFundingReason] = useState('');
  const [fundingUrgent, setFundingUrgent] = useState(false);
  const [fundingMessage, setFundingMessage] = useState<FormMessage>(null);
  const [busy, setBusy] = useState(false);

  // State is only set when the request finishes (never synchronously inside the effect below).
  const load = useCallback(
    () =>
      emergencyApi.getBudget().then(
        (result) => {
          setBudget(result);
          setLoadError(null);
          setLoading(false);
        },
        (e: unknown) => {
          setLoadError(messageOf(e));
          setLoading(false);
        },
      ),
    [],
  );

  useEffect(() => {
    void load();
    const timer = setInterval(() => void load(), REFRESH_MS);
    return () => clearInterval(timer);
  }, [load]);

  async function sendExpense(e: FormEvent) {
    e.preventDefault();
    const amount = parseAmount(expenseAmount);
    if (typeof amount === 'string') return setExpenseMessage({ ok: false, text: amount });
    if (!expenseDescription.trim()) return setExpenseMessage({ ok: false, text: 'Describe the expense (e.g. Fuel, Overtime).' });

    setBusy(true);
    try {
      const result = await emergencyApi.submitExpense({ amount, description: expenseDescription.trim() });
      const left = result?.remainingBudget;
      setExpenseMessage({
        ok: true,
        text: `Expense approved${left != null ? ` – ${money(left, budget?.currency)} left` : ''}.`,
      });
      setExpenseAmount('');
      setExpenseDescription('');
      await load();
    } catch (err) {
      setExpenseMessage({ ok: false, text: messageOf(err) }); // e.g. Budget's "over budget" reason
    } finally {
      setBusy(false);
    }
  }

  async function sendFunding(e: FormEvent) {
    e.preventDefault();
    const amount = parseAmount(fundingAmount);
    if (typeof amount === 'string') return setFundingMessage({ ok: false, text: amount });
    if (!fundingReason.trim()) return setFundingMessage({ ok: false, text: 'Give a reason for the request.' });

    setBusy(true);
    try {
      const result = await emergencyApi.requestFunding({ amount, reason: fundingReason.trim(), urgent: fundingUrgent });
      const status = FUNDING_STATUS[String(result?.status ?? '0')] ?? 'Pending';
      setFundingMessage({
        ok: true,
        text: `Funding request for ${money(amount, budget?.currency)} sent – ${status}${fundingUrgent ? ' (urgent)' : ''}.`,
      });
      setFundingAmount('');
      setFundingReason('');
      setFundingUrgent(false);
    } catch (err) {
      setFundingMessage({ ok: false, text: messageOf(err) });
    } finally {
      setBusy(false);
    }
  }

  const usedShare = budget && budget.allocated > 0 ? Math.min(1, budget.spent / budget.allocated) : 0;
  const low = budget !== null && budget.allocated > 0 && budget.remaining / budget.allocated < LOW_BUDGET_SHARE;

  return (
    <section className="panel panel-budget" aria-label="Budget">
      <header className="panel-header">
        <h2>Budget{budget ? ` (${budget.departmentCode})` : ''}</h2>
        <button className="panel-btn" onClick={() => void load()} disabled={busy}>Refresh</button>
      </header>

      <div className="budget-body">
        {loading ? (
          <p className="panel-empty">Loading budget…</p>
        ) : budget === null ? (
          <p className="form-msg form-error" role="alert">Budget unavailable: {loadError}</p>
        ) : (
          <>
            <div className="budget-stats">
              <div className="stat">
                <span className="stat-num">{money(budget.allocated, budget.currency)}</span>
                <span className="stat-label">Allocated</span>
              </div>
              <div className="stat stat-bad">
                <span className="stat-num">{money(budget.spent, budget.currency)}</span>
                <span className="stat-label">Spent</span>
              </div>
              <div className={`stat ${low ? 'stat-bad' : 'stat-good'}`}>
                <span className="stat-num">{money(budget.remaining, budget.currency)}</span>
                <span className="stat-label">Remaining</span>
              </div>
            </div>

            <div
              className={`budget-bar${low ? ' budget-bar-low' : ''}`}
              role="meter"
              aria-label="Share of budget spent"
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuenow={Math.round(usedShare * 100)}
            >
              <span className="budget-bar-fill" style={{ width: `${usedShare * 100}%` }} />
            </div>
            <p className="budget-hint">
              {Math.round(usedShare * 100)}% spent
              {budget.departmentName ? ` · ${budget.departmentName}` : ''}
            </p>
            {low && <p className="form-msg form-error" role="alert">Low budget: less than 10% left.</p>}
            {loadError && <p className="budget-hint">Showing the last known values – {loadError}</p>}
          </>
        )}

        {admin ? (
          <>
            <form className="budget-form" onSubmit={(e) => void sendExpense(e)} aria-label="Submit an expense">
              <h3>Submit an expense</h3>
              <label htmlFor="expense-amount">Amount (CAD)</label>
              <input id="expense-amount" inputMode="decimal" value={expenseAmount}
                onChange={(e) => setExpenseAmount(e.target.value)} placeholder="1000" />
              <label htmlFor="expense-description">Description</label>
              <input id="expense-description" value={expenseDescription} maxLength={500}
                onChange={(e) => setExpenseDescription(e.target.value)} placeholder="Fuel, overtime, equipment…" />
              <button className="panel-btn" type="submit" disabled={busy}>Send expense</button>
              {expenseMessage && (
                <p className={`form-msg ${expenseMessage.ok ? 'form-ok' : 'form-error'}`} role="status">
                  {expenseMessage.text}
                </p>
              )}
            </form>

            <form className="budget-form" onSubmit={(e) => void sendFunding(e)} aria-label="Request more funding">
              <h3>Request more funding</h3>
              <label htmlFor="funding-amount">Amount (CAD)</label>
              <input id="funding-amount" inputMode="decimal" value={fundingAmount}
                onChange={(e) => setFundingAmount(e.target.value)} placeholder="50000" />
              <label htmlFor="funding-reason">Reason</label>
              <input id="funding-reason" value={fundingReason} maxLength={1000}
                onChange={(e) => setFundingReason(e.target.value)} placeholder="Winter storm overtime" />
              <label className="budget-check" htmlFor="funding-urgent">
                <input id="funding-urgent" type="checkbox" checked={fundingUrgent}
                  onChange={(e) => setFundingUrgent(e.target.checked)} />
                Urgent (reviewed first)
              </label>
              <button className="panel-btn" type="submit" disabled={busy}>Send request</button>
              {fundingMessage && (
                <p className={`form-msg ${fundingMessage.ok ? 'form-ok' : 'form-error'}`} role="status">
                  {fundingMessage.text}
                </p>
              )}
            </form>
          </>
        ) : (
          <p className="budget-hint">Only an Emergency Admin can send expenses or funding requests.</p>
        )}
      </div>
    </section>
  );
}
