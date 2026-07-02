import { Page } from '@playwright/test';

export class LoginPage {
  constructor(private page: Page) {}

  async goto() { await this.page.goto('/login'); }

  async fillEmail(email: string) {
    await this.page.getByLabel(/email/i).fill(email);
  }

  async fillPassword(password: string) {
    await this.page.getByLabel(/password/i).fill(password);
  }

  async submit() {
    await this.page.getByRole('button', { name: /sign in/i }).click();
  }

  async loginAs(email: string, password: string) {
    await this.goto();
    await this.fillEmail(email);
    await this.fillPassword(password);
    await this.submit();
  }
}
