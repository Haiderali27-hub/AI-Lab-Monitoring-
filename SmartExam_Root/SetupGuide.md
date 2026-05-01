# SmartExam Project Setup and Startup Guide

This guide provides the necessary steps to set up and start the SmartExam project, including the backend, frontend, and desktop application.

## Prerequisites

1. **Install Docker Desktop**
   - Ensure Docker Desktop is installed and running on your system.
   - [Download Docker Desktop](https://www.docker.com/products/docker-desktop/)

2. **Install .NET SDK**
   - Install the .NET SDK (version 9.0 or later).
   - [Download .NET SDK](https://dotnet.microsoft.com/download)

3. **Install Node.js**
   - Install Node.js (version 18 or later).
   - [Download Node.js](https://nodejs.org/)

---

## Step 1: Start PostgreSQL Database

1. Navigate to the project root directory:
   ```bash
   cd C:\Users\DELL\Desktop\AI_Labmonitoring\SmartExam_Root
   ```

2. Start the PostgreSQL database using Docker Compose:
   ```bash
   docker compose -f Infra/docker-compose.postgres.yml up -d
   ```

3. Verify the database is running:
   ```bash
   docker ps
   ```
   Ensure the `smartexam-postgres` container is listed and running.

---

## Step 2: Run the Backend API

1. Navigate to the backend directory:
   ```bash
   cd C:\Users\DELL\Desktop\AI_Labmonitoring\SmartExam_Root\Backend_API
   ```

2. Run the backend API:
   ```bash
   dotnet run
   ```

3. The API will be available at:
   ```
   http://localhost:5101
   ```

4. Open the Swagger UI to test the API:
   ```
   http://localhost:5101/swagger
   ```

---

## Step 3: Run the Admin Web Panel

1. Navigate to the admin web panel directory:
   ```bash
   cd C:\Users\DELL\Desktop\AI_Labmonitoring\SmartExam_Root\Admin_Web_Panel
   ```

2. Install dependencies:
   ```bash
   npm install
   ```

3. Start the development server:
   ```bash
   npm run dev
   ```

4. Open the web panel in your browser:
   ```
   http://localhost:5173/ui
   ```

---

## Step 4: Run the Student Desktop App

1. Navigate to the desktop app directory:
   ```bash
   cd C:\Users\DELL\Desktop\AI_Labmonitoring\SmartExam_Root\Student_Desktop_App
   ```

2. Run the desktop application:
   ```bash
   dotnet run
   ```

---

## Step 5: Test Authentication

1. Use the Swagger UI to test the backend authentication endpoints:
   - `POST /api/auth/login`
   - `POST /api/auth/student-login`
   - `POST /api/auth/refresh`
   - `POST /api/auth/logout`
   - `GET /api/auth/me`

2. Test the **Admin Web Panel**:
   - Open the web panel at `http://localhost:5173/ui`.
   - Test the login functionality and ensure the UI is working as expected.

3. Test the **Student Desktop App**:
   - Login with a student account.
   - Start an exam and verify live monitoring is working.

---

## Notes
- Ensure the database credentials in `appsettings.json` match the credentials in `docker-compose.postgres.yml`.
- If you encounter any issues, check the logs for the backend and database containers using:
  ```bash
  docker logs smartexam-postgres
  ```