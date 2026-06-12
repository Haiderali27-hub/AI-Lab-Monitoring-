# SmartExam — AI-Driven Lab Examination Monitoring System

SmartExam is a secure, client-server university lab examination monitoring platform. It restricts student devices during exams, streams focus and process telemetry in real-time, leverages AI for grading and feedback, detects plagiarism, and provides robust analytics and communication hubs.

---

## 📂 Documentation Map (Quick Links)

Use this directory map to navigate the guides, scripts, and documentation built for this project:

| Guide / Document | Purpose | Audience | Link |
|:---|:---|:---|:---|
| **Quick Start** | 2-minute setup, test credentials, and quick Swagger test workflows | Developers, Testers | [QUICK_START.md](file:///c:/Users/DELL/Desktop/AI_Labmonitoring/QUICK_START.md) |
| **Setup & Run Guide** | Step-by-step installation instructions for Backend API & React Web Panel | System Admins, Developers | [SETUP_AND_RUN_GUIDE.md](file:///c:/Users/DELL/Desktop/AI_Labmonitoring/SETUP_AND_RUN_GUIDE.md) |
| **Manual Testing Guide** | End-to-end user journeys (Admin setup, Teacher creation, Student login/run, Live proctoring, AI grading, Analytics, Student Portal) | QA Teams, Evaluators | [MANUAL_TESTING_GUIDE.md](file:///c:/Users/DELL/Desktop/AI_Labmonitoring/MANUAL_TESTING_GUIDE.md) |
| **Modules 1–3 Backend Build** | Step-by-step documentation on how Modules 1–3 backend structures were built | Developers | [SmartExam_Backend_Build_Guide.md](file:///c:/Users/DELL/Desktop/AI_Labmonitoring/SmartExam_Backend_Build_Guide.md) |
| **Modules 4–6 Backend Build** | Detailed documentation of Analytics, Student Portal, and Notification SMTP additions | Developers | [SmartExam_Modules456_Backend_Guide.md](file:///c:/Users/DELL/Desktop/AI_Labmonitoring/SmartExam_Modules456_Backend_Guide.md) |
| **Frontend UI Design Guide** | Detailed Stitch visual mocks, colors, sizing, typography, and wireframes | UI/UX Designers | [SmartExam_Frontend_UI_Guide.md](file:///c:/Users/DELL/Desktop/AI_Labmonitoring/SmartExam_Frontend_UI_Guide.md) |
| **Frontend Dev Walkthrough** | Implementation instructions for building the React Web Panel with Tailwind CSS | Frontend Developers | [SmartExam_Frontend_Dev_Guide.md](file:///c:/Users/DELL/Desktop/AI_Labmonitoring/SmartExam_Frontend_Dev_Guide.md) |
| **Modules 4–6 Frontend Guide** | Detailed guide for Analytics, Student Portal, and Notification dropdown React UI integration | Frontend Developers | [SmartExam_Modules456_Frontend_Guide.md](file:///c:/Users/DELL/Desktop/AI_Labmonitoring/SmartExam_Modules456_Frontend_Guide.md) |
| **Baseline Certificate** | Validation certificate proving Modules 1–3 compile and pass all tests | Project Stakeholders | [COMPLETION_CERTIFICATE.md](file:///c:/Users/DELL/Desktop/AI_Labmonitoring/COMPLETION_CERTIFICATE.md) |

---

## 🛠️ Modules & Project Completion Status

| Module | Purpose | Backend API Status | Admin Web Panel Status | Desktop Client Status |
|:---:|:---|:---:|:---:|:---:|
| **M1** | **Auth & HWID Binding** | ✅ Complete | ✅ Complete | ✅ Complete |
| **M2** | **Exam Dashboard** | ✅ Complete | ✅ Complete | ✅ Complete |
| **M3** | **Live Monitoring** | ✅ Complete | ✅ Complete (Live Focus Timeline) | ✅ Complete (Heartbeat + Window Ingestion) |
| **M4** | **Reporting & Analytics** | ✅ Complete (PDF + System Stats) | ✅ Complete (System & Exam Charts) | N/A |
| **M5** | **Student Portal** | ✅ Complete (Grades + AI feedback) | ✅ Complete (Dashboard & SVG Performance) | N/A |
| **M6** | **Notifications** | ✅ Complete (In-app + SMTP Email) | ✅ Complete (Bell Dropdown & Broadcasts) | N/A |

---

## 🏗️ Repository Layout

The project source code is housed inside the [SmartExam_Root](file:///c:/Users/DELL/Desktop/AI_Labmonitoring/SmartExam_Root) folder:

```
SmartExam_Root/
│
├── Backend_API/               ← ASP.NET Core 9.0 Web API (PostgreSQL + EF Core)
│   ├── Controllers/           ← Web endpoints separated by module context
│   ├── Data/                  ← AppDbContext and seed scripts
│   ├── Models/                ← Database entities (Users, Exams, Reports, etc.)
│   ├── Services/              ← Business services (Email, Analytics, PDF generation)
│   └── Properties/            ← launchSettings (configured for port 5050)
│
├── Admin_Web_Panel/           ← React + TypeScript SPA (Vite + Tailwind CSS)
│   ├── src/
│   │   ├── api/               ← Axios client and endpoint wrappers
│   │   ├── components/        ← Reusable panels, layouts, navigation
│   │   ├── context/           ← User session AuthContext providers
│   │   └── pages/             ← Login, dashboard, user, live monitoring pages
│   └── package.json           
│
├── Student_Desktop_App/       ← Secure Student WPF Client (C# .NET 9)
│   └── Views/                 ← Native lockdown browser and timer dashboard
│
└── SmartExam.sln              ← Master solution file
```

---

## 🚀 Quick Execution Guide

To run the complete system on your local machine:

### 1. Database Configuration
Add your connection credentials to [appsettings.json](file:///c:/Users/DELL/Desktop/AI_Labmonitoring/SmartExam_Root/Backend_API/appsettings.json):
```json
"ConnectionStrings": {
  "DefaultConnection": "Host=your-postgres-host;Database=neondb;Username=your-username;Password=your-password;SSL Mode=Require;"
}
```

### 2. Run the Backend API Server
```bash
cd SmartExam_Root/Backend_API
dotnet restore
dotnet build
dotnet run --urls "http://localhost:5050"
```
*Verify Swagger: `http://localhost:5050/swagger`*

### 3. Run the Admin Web Panel
```bash
cd SmartExam_Root/Admin_Web_Panel
npm install
npm run dev
```
*Open Panel: `http://localhost:5173`*

### 4. Run the Student Desktop App
```bash
cd SmartExam_Root/Student_Desktop_App
dotnet run
```
