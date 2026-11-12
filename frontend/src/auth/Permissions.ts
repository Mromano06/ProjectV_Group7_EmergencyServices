import type { Role } from '../types/models';

export const canManageDispatch = (role: Role) =>
  role === 'Dispatcher' || role === 'Admin';