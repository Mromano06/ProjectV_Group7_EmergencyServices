/**
 * @file Setup.ts
 * @brief Configures shared Vitest matchers and cleans up rendered components.
 */


import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';

afterEach(() => cleanup());