import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api/apiClient';
import { AuthProvider } from '../auth/AuthContext';
import BudgetPanel from '../components/BudgetPanel';
import type { Role } from '../types/models';

// ES-FR-025: the dashboard shows allocated, spent and remaining from Budget & Finance,
// and an Admin can send expenses and funding requests. The API calls are faked.
const api = vi.hoisted(() => ({
  getBudget: vi.fn(),
  submitExpense: vi.fn(),
  requestFunding: vi.fn(),
}));

vi.mock('../api/emergencyApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/emergencyApi')>()),
  emergencyApi: api,
}));

const EMS = (allocated: number, spent: number) => ({
  departmentCode: 'EMS', departmentName: 'Emergency Services',
  allocated, spent, remaining: allocated - spent, currency: 'CAD',
});

beforeEach(() => {
  api.getBudget.mockResolvedValue(EMS(4500000, 1000));
  api.submitExpense.mockResolvedValue({ approved: true, message: 'Expense approved.', remainingBudget: 4498000 });
  api.requestFunding.mockResolvedValue({ id: 'f1', amount: 50000, reason: 'x', urgent: true, status: 0 });
});

function showPanel(role: Role = 'Admin') {
  render(
    <AuthProvider initialRole={role}>
      <BudgetPanel />
    </AuthProvider>,
  );
}

describe('Budget panel (ES-FR-025)', () => {
  it('shows allocated, spent and remaining from Budget & Finance', async () => {
    showPanel();

    expect(await screen.findByText('Budget (EMS)')).toBeInTheDocument();
    expect(screen.getByText(/4,500,000/)).toBeInTheDocument();
    expect(screen.getByText(/^\$1,000$/)).toBeInTheDocument();
    expect(screen.getByText(/4,499,000/)).toBeInTheDocument();
    expect(screen.getByRole('meter', { name: 'Share of budget spent' })).toHaveAttribute('aria-valuenow', '0');
  });

  it('warns when less than 10% of the budget is left', async () => {
    api.getBudget.mockResolvedValue(EMS(100000, 95000));
    showPanel();

    expect(await screen.findByText('Low budget: less than 10% left.')).toBeInTheDocument();
  });

  it('shows the numbers but no forms to a non-Admin', async () => {
    showPanel('Dispatcher');

    expect(await screen.findByText(/4,500,000/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Send expense' })).not.toBeInTheDocument();
    expect(screen.getByText(/Only an Emergency Admin/)).toBeInTheDocument();
  });

  it('an Admin sends an expense, sees what is left, and the totals refresh', async () => {
    const user = userEvent.setup();
    showPanel();
    await screen.findByText(/4,500,000/);

    await user.type(screen.getByLabelText('Amount (CAD)', { selector: '#expense-amount' }), '1000');
    await user.type(screen.getByLabelText('Description'), 'Fuel');
    await user.click(screen.getByRole('button', { name: 'Send expense' }));

    expect(api.submitExpense).toHaveBeenCalledWith({ amount: 1000, description: 'Fuel' });
    expect(await screen.findByText(/Expense approved – \$4,498,000 left\./)).toBeInTheDocument();
    await waitFor(() => expect(api.getBudget).toHaveBeenCalledTimes(2)); // reloaded after the expense
  });

  it("shows Budget's reason when an expense is refused, and keeps what was typed", async () => {
    api.submitExpense.mockRejectedValue(new ApiError(400, 'budget_rejected', 'Expense exceeds the remaining EMS budget.'));
    const user = userEvent.setup();
    showPanel();
    await screen.findByText(/4,500,000/);

    const amount = screen.getByLabelText('Amount (CAD)', { selector: '#expense-amount' });
    await user.type(amount, '9999999');
    await user.type(screen.getByLabelText('Description'), 'New engine');
    await user.click(screen.getByRole('button', { name: 'Send expense' }));

    expect(await screen.findByText('Expense exceeds the remaining EMS budget.')).toBeInTheDocument();
    expect(amount).toHaveValue('9999999');
  });

  it('checks the amount before sending anything', async () => {
    const user = userEvent.setup();
    showPanel();
    await screen.findByText(/4,500,000/);
    const amount = screen.getByLabelText('Amount (CAD)', { selector: '#expense-amount' });
    await user.type(screen.getByLabelText('Description'), 'Fuel');

    await user.type(amount, '10.005');
    await user.click(screen.getByRole('button', { name: 'Send expense' }));
    expect(await screen.findByText(/Enter an amount in dollars/)).toBeInTheDocument();

    await user.clear(amount);
    await user.type(amount, '0');
    await user.click(screen.getByRole('button', { name: 'Send expense' }));
    expect(await screen.findByText('The amount must be more than $0.')).toBeInTheDocument();

    expect(api.submitExpense).not.toHaveBeenCalled();
  });

  it('an Admin asks for urgent funding', async () => {
    const user = userEvent.setup();
    showPanel();
    await screen.findByText(/4,500,000/);

    await user.type(screen.getByLabelText('Amount (CAD)', { selector: '#funding-amount' }), '50000');
    await user.type(screen.getByLabelText('Reason'), 'Winter storm overtime');
    await user.click(screen.getByLabelText(/Urgent/));
    await user.click(screen.getByRole('button', { name: 'Send request' }));

    expect(api.requestFunding).toHaveBeenCalledWith({ amount: 50000, reason: 'Winter storm overtime', urgent: true });
    expect(await screen.findByText(/Funding request for \$50,000 sent – Pending \(urgent\)\./)).toBeInTheDocument();
  });

  it('says clearly when Budget & Finance is unavailable', async () => {
    api.getBudget.mockRejectedValue(new ApiError(502, 'budget_unavailable', 'Budget service returned HTTP 500.'));
    showPanel();

    expect(await screen.findByText(/Budget unavailable: Budget service returned HTTP 500\./)).toBeInTheDocument();
  });
});
