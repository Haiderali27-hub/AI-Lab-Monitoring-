# SmartExam Startup Guide

Follow these steps to start the platform components.

## 1. Local Infrastructure (Database)
Open a terminal in the root directory:
```powershell
cd Infra
docker compose -f docker-compose.postgres.yml up -d
```

## 2. Backend API
Open a new terminal:
```powershell
cd Backend_API
dotnet run
```
*Wait for the "Now listening on..." message.*

## 3. Admin Web Panel
Open a new terminal:
```powershell
cd Admin_Web_Panel
npm install
npm run dev
```
*Open http://localhost:5173 in your browser.*

## 4. Student Desktop Application (WPF)
Open a new terminal:
```powershell
cd Student_Desktop_App
dotnet run
```
*Note: Ensure the Backend API is running before starting the Desktop App.*

---

### First Run Workflow:
1. **Setup Platform:** When you open the Web Panel for the first time, you will be redirected to `/setup`. Create your Super Admin account and the first Institution.
2. **Login:** After setup, login with the Super Admin credentials.
3. **Manage Institutions:** Use the "Institutions" tab to see your registered organization.
4. **Onboard Users:** Go to the Admin Dashboard (for a specific institution) to create Teacher and Student accounts.
5. **Student Device Binding:** Run the Desktop App and login with a student account. The app will automatically bind the student to your current machine's HWID.
