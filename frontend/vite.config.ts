import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        secure: false,
      },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: './src/tests/Setup.ts',
    env: { VITE_USE_MOCK: 'true' }, // keeps the weather poller off during tests
    restoreMocks: true,
    unstubGlobals: true,
  },
})
