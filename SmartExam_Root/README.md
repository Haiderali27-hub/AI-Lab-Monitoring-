# SmartExam MVP (Modules 1-3)

SmartExam is a client-server lab examination monitoring platform with a top-down authentication model:

- Level 1: Organization Admin bootstrap
- Level 2: Teacher/Staff accounts provisioned by admin
- Level 3: Student accounts provisioned in bulk or individually (no self-registration)

This implementation includes:

- Module 1: Authentication and Device Binding
- Module 2: Exam Dashboard
- Module 3: Live Monitoring Client Service

## Repository Layout

- `Backend_API`: ASP.NET Core REST API + PostgreSQL (EF Core) + JWT + SignalR
- `Student_Desktop_App`: WPF secure student client (HWID generation, exam dashboard, heartbeat monitoring)
- `Admin_Web_Panel`: React + TypeScript dashboard for onboarding and live control
- `Infra`: local infrastructure manifests
- `Docs`: documentation

## Implemented Backend Features

- Institution bootstrap endpoint (`/api/auth/bootstrap-admin`)
- Admin/Teacher login (`/api/auth/login`)
- Student login with HWID binding (`/api/auth/student-login`)
- Refresh and logout (`/api/auth/refresh`, `/api/auth/logout`)
- Admin controls:
  - create teacher/student
  - batch CSV upload of students
  - list device bindings
  - reset student binding
  - force terminate student sessions
- Exam dashboard APIs:
  - student current exam status
  - student exam start
  - admin live roster
- Monitoring APIs:
  - heartbeat ingestion
  - generic event ingestion
  - SignalR hub (`/hubs/monitoring`)

## Implemented Desktop Features

- Secure login window
- HWID generation from CPU + motherboard + MAC
- Student auth call with HWID payload
- Exam dashboard with status + timer + exam start
- Monitoring service:
  - periodic heartbeat
  - foreground app check
  - process list snapshot

## Implemented Web Features

- Landing page with institution bootstrap + login
- Auth context with protected routes
- Admin dashboard:
  - teacher onboarding form
  - student creation form
  - CSV student upload
  - student binding table with reset and force logout actions
  - live exam roster panel (polling)

## Prerequisites

- .NET SDK 9+
- Node.js 22+
- PostgreSQL 16+ (or Docker)

## Local Run (Single Machine)

1. Start PostgreSQL (Docker option):

   ```bash
   cd Infra
   docker compose -f docker-compose.postgres.yml up -d
   ```

2. Trust HTTPS certificate (first time):

   ```bash
   dotnet dev-certs https --trust
   ```

3. Run backend:

   ```bash
   cd Backend_API
   dotnet run
   ```

4. Run web panel:

   ```bash
   cd Admin_Web_Panel
   copy .env.example .env
   npm install
   npm run dev
   ```

5. Run desktop app:

   ```bash
   cd Student_Desktop_App
   dotnet run
   ```

## LAN Demo Notes

- Backend must be reachable from student machines (set API base URL accordingly).
- For web panel, set `VITE_API_BASE_URL` to backend LAN URL.
- For desktop client, set environment variable before launch:

  ```powershell
  $env:SMARTEXAM_API_BASE_URL = "https://<backend-lan-ip>:<port>"
  $env:SMARTEXAM_ALLOW_INSECURE_HTTPS = "true"
  ```

## CSV Upload Format

Expected file format:

```csv
username,email,password
student1,student1@university.edu,Pass@1234
student2,student2@university.edu,Pass@1234
```

## Build Verification

Validated in this workspace:

- `dotnet build SmartExam.sln` -> success
- `npm run build` in `Admin_Web_Panel` -> success
