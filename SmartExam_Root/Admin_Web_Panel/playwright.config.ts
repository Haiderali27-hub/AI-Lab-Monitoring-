import { defineConfig, devices } from '@playwright/test';
import os from 'node:os';
import path from 'node:path';

// Only reuse already-running servers when explicitly asked (PW_REUSE=1). By default Playwright
// starts its own backend in E2E in-memory mode, so a test run can never hit a dev backend that
// is wired to the real (Neon) database.
const reuse = !!process.env.PW_REUSE;

export default defineConfig({
  timeout: 30_000,           // 30 seconds per test max
  retries: 1,                // Retry failed test once before marking as failed
  workers: 1,                // Run tests one at a time (avoids conflicts with shared state)

  reporter: [
    ['list'],                // Shows each test as it runs
    ['html', { open: 'never' }],  // Do not open HTML report automatically
  ],

  use: {
    baseURL: 'http://localhost:5173',
    headless: true,          // Run in headless mode since it is a CLI/CI runner
    screenshot: 'only-on-failure',   // Auto screenshot when test fails
    video: 'retain-on-failure',      // Save video only for failed tests
    trace: 'on-first-retry',         // Detailed trace on retry
    actionTimeout: 10_000,   // 10 seconds to find/click an element
  },

  projects: [
    {
      // Headless black-box HTTP tests (Playwright `request`, no browser)
      name: 'api',
      testDir: './tests/api',
      use: { baseURL: 'http://localhost:5050' },
    },
    {
      // Headless browser E2E (+ student desktop-client simulator over HTTP)
      name: 'chromium',
      testDir: './tests/e2e',
      use: { ...devices['Desktop Chrome'], headless: true },
    },
  ],

  // Start the backend (in-memory, seeded, rate limit lifted) and the Vite dev server
  webServer: [
    {
      command: 'dotnet run --project Backend_API.csproj --urls http://localhost:5050 --no-launch-profile',
      // Run from Backend_API so relative paths (appsettings, Reports/) resolve as in normal runs
      cwd: '../Backend_API',
      url: 'http://localhost:5050/api/health',
      reuseExistingServer: reuse,
      timeout: 180_000,
      stdout: 'ignore',
      stderr: 'pipe',
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        E2E__InMemory: 'true',
        RateLimiting__LoginPermitLimit: '100000',
        RateLimiting__RefreshPermitLimit: '100000',
        // Exercise the production access-token lifetime (the web/desktop clients refresh transparently).
        Jwt__ExpiryMinutes: '30',
        // Module 5: uploaded screen captures go to a throwaway folder, never into the source tree.
        Storage__EvidencePath: path.join(os.tmpdir(), 'smartexam-e2e-evidence'),
      },
    },
    {
      command: 'npm run dev',
      url: 'http://localhost:5173',
      reuseExistingServer: reuse,
      timeout: 60_000,
    },
  ],
});
