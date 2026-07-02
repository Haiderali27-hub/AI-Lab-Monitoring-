import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests/e2e',
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
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],

  // Automatically start the dev server before tests run
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:5173',
    reuseExistingServer: true,  // Use already-running server if available
    timeout: 30_000,
  },
});
