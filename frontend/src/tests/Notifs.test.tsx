import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import App from '../App';
import { AuthProvider } from '../auth/AuthContext';
import { DispatchProvider } from '../state/DispatchContext';


function showApp() {
  render(
    <AuthProvider>
      <DispatchProvider>
        <App />
      </DispatchProvider>
    </AuthProvider>,
  );
}


describe('Notifications', () => {
  it('starts with 2 calls', () => {
    showApp();
    expect(screen.getByText(/Notifications \(2\)/)).toBeInTheDocument();
  });

  it('shows "No unit sent" on every new call', () => {
    showApp();
    expect(screen.getAllByText('No unit sent')).toHaveLength(2);
  });

  it('adds a call when + Test call is clicked', async () => {
    const user = userEvent.setup();
    showApp();

    await user.click(screen.getByRole('button', { name: '+ Test call' }));

    expect(screen.getByText(/Notifications \(3\)/)).toBeInTheDocument();
  });

  it('lets a Dispatcher set a destination', async () => {
    const user = userEvent.setup();
    showApp();

    const buttons = screen.getAllByRole('button', { name: /set as destination/i });
    await user.click(buttons[0]);

    expect(screen.getByRole('button', { name: /destination set/i })).toBeInTheDocument();
  });

  it('hides the destination buttons from a Viewer', async () => {
    const user = userEvent.setup();
    showApp();

    await user.selectOptions(screen.getByRole('combobox'), 'Viewer');

    expect(screen.queryByRole('button', { name: /set as destination/i })).not.toBeInTheDocument();
  });
});