import { defineConfig, devices } from '@playwright/test';
import os from 'node:os';
import path from 'node:path';

// Documentation-only Playwright setup: captures the thesis UI figures from the REAL app.
// Not part of the normal test run (playwright.config.ts only looks in tests/api and tests/e2e).
// Run: npm run docs:screenshots   (PNG files go to $SMARTEXAM_SHOTS_DIR, default <tmp>/smartexam-thesis-shots)
const reuse = !!process.env.PW_REUSE;

export default defineConfig({
  testDir: './tests/docs',
  outputDir: './test-results/docs',
  timeout: 15 * 60_000,      // one long scenario: builds the demo state, then captures every page
  retries: 0,
  workers: 1,
  reporter: [['list']],

  use: {
    ...devices['Desktop Chrome'],
    baseURL: 'http://localhost:5173',
    headless: true,
    viewport: { width: 1440, height: 900 },
    deviceScaleFactor: 1,
    actionTimeout: 15_000,
    screenshot: 'only-on-failure',
  },

  // Same servers as playwright.config.ts: the backend in E2E in-memory mode (never the Neon database) + Vite.
  webServer: [
    {
      command: 'dotnet run --project Backend_API.csproj --urls http://localhost:5050 --no-launch-profile',
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
        Jwt__ExpiryMinutes: '30',
        Storage__EvidencePath: path.join(os.tmpdir(), 'smartexam-e2e-evidence'),
        Grading__Runner: 'Auto',
        Grading__Llm__Enabled: 'false',
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
