# 🚀 SmartExam — First-Time Setup & Execution Guide
This guide walks you through setting up and running the SmartExam C# API Backend and React Web Panel on your local computer from scratch.

---

## 💻 System Requirements
Ensure the following tools are installed on your machine before starting:
1. **.NET 9.0 SDK** (Download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/9.0))
2. **Node.js** (v18.0 or higher recommended, download from [nodejs.org](https://nodejs.org/))
3. **Git** (optional, for version control)
4. A **PostgreSQL Database** (e.g. local PostgreSQL server or a cloud database provider like Neon DB)

---

## 🗄️ Step 1: Database Configuration
SmartExam uses PostgreSQL to store all exam, user, mapping, and logging data.
1. Open the file [appsettings.json](file:///c:/Users/DELL/Desktop/AI_Labmonitoring/SmartExam_Root/Backend_API/appsettings.json) in your text editor.
2. Locate the `ConnectionStrings` section:
   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Host=your-db-host;Database=neondb;Username=your-username;Password=your-password;SSL Mode=Require;"
   }
   ```
3. Update `DefaultConnection` with your actual PostgreSQL connection details (host, database name, username, and password). If using Neon DB, make sure `SSL Mode=Require;` is kept.

---

## ⚙️ Step 2: Restoring & Running the Backend API
The backend automatically executes EF Core migrations and seeds initial testing data if the database is empty.

1. Open a command prompt or terminal on your computer.
2. Navigate to the backend directory:
   ```cmd
   cd C:\Users\DELL\Desktop\AI_Labmonitoring\SmartExam_Root\Backend_API
   ```
3. **Restore Nuget Packages:**
   ```cmd
   dotnet restore
   ```
4. **Build the project:**
   ```cmd
   dotnet build
   ```
5. **Run the API server:**
   ```cmd
   dotnet run --urls "http://localhost:5050"
   ```
6. **Verify Running State:**
   * You should see console outputs saying:
     * `Context 'AppDbContext' started tracking...`
     * `✅ Database seeded successfully.` (if running for the first time)
     * `Now listening on: http://localhost:5050`
   * Open your browser and navigate to `http://localhost:5050/swagger`. If the Swagger UI API documentation loads, the backend is up and running.

---

## 🌐 Step 3: Installing & Running the React Web Panel
The React Web Panel is the frontend portal shared by admins and teachers.

1. Open a **second** terminal or command prompt (keep the backend server terminal running).
2. Navigate to the frontend directory:
   ```cmd
   cd C:\Users\DELL\Desktop\AI_Labmonitoring\SmartExam_Root\Admin_Web_Panel
   ```
3. **Install dependencies:**
   ```cmd
   npm install
   ```
   *(This downloads all packages, including React, Tailwind, Axios, and SignalR).*
4. **Run the development web server:**
   ```cmd
   npm run dev
   ```
5. **Verify Running State:**
   * The terminal will display:
     * `  VITE v8.0.16  ready in X ms`
     * `  ➜  Local:   http://localhost:5173/`
   * Open your browser and navigate to `http://localhost:5173/`. You should see the custom blue and white SmartExam login panel.

---

## 🔐 Step 4: Login and Test Verification
Log in using the pre-seeded credentials to verify the frontend-to-backend communication:

### Admin Credentials
* **Email:** `admin@smartexam.com`
* **Password:** `Admin@123`
* *Purpose:* Test user management, batch CSV uploads, labs station mappings, and device reset workflows.

### Teacher Credentials
* **Email:** `teacher@smartexam.com`
* **Password:** `Teacher@123`
* *Purpose:* Create mock exams, check student eligibility, and proctor exams in real time.

---

## 🛠️ Troubleshooting Guide

### 1. "Process cannot access file... bin/Debug/net9.0/Backend_API.exe"
* **Why:** You tried to run `dotnet build` while an instance of the backend was still running.
* **Fix:** Close the active backend terminal window (or press `Ctrl+C` in it) to release the file lock, then run `dotnet build` again.

### 2. White Screen / Crash loops
* **Why:** LocalStorage has cached old user data with invalid roles.
* **Fix:** Press `F12` to open your browser Developer Options. Go to the **Application** tab $\rightarrow$ **Local Storage** $\rightarrow$ right-click and choose **Clear** (or run `localStorage.clear()` in the Console). Reload the page.

### 3. Connection refused / API calls failing
* **Why:** The React frontend expects the API on port `5050`. If the backend is running on a different port, they cannot communicate.
* **Fix:** Make sure the backend is launched exactly using `--urls "http://localhost:5050"`. If you must run on a different port (e.g. `5051`), you must update `API_BASE_URL` and `SIGNALR_HUB_URL` in [constants.ts](file:///c:/Users/DELL/Desktop/AI_Labmonitoring/SmartExam_Root/Admin_Web_Panel/src/utils/constants.ts).
