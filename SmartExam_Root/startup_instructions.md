# SmartExam Project Startup Instructions

Follow these steps to get the entire Lab Exam Portal running.

## 1. Start the Database (PostgreSQL)
Ensure you have Docker Desktop running, then execute:
```cmd
cd SmartExam_Root
docker compose -f Infra/docker-compose.postgres.yml up -d
```
*Note: The database is configured to run on port **5433** to avoid conflicts with local Postgres installations.*

## 2. Start the Backend API
In a new terminal window:
```cmd
cd SmartExam_Root\Backend_API
dotnet run
```
*The API will be available at `http://localhost:5067`.*

## 3. Start the Admin Web Panel
In a another terminal window:
```cmd
cd SmartExam_Root\Admin_Web_Panel
npm install
npm run dev
```
*The web panel will be available at `http://localhost:5173` (or the URL shown in the terminal).*

## 4. Initial Setup
1. Open the Admin Web Panel URL.
2. If this is the first time, you will see the **Register Institution** screen.
3. Create your institution and admin account.
4. Once created, you will be automatically logged in and redirected to the Dashboard.
