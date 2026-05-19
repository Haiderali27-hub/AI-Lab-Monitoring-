# SmartExam API — Quick Reference Card

**API Status:** ✅ **LIVE on http://localhost:5050**

---

## 🚀 Get Started in 2 Minutes

### Option 1: Swagger (Recommended for Quick Test)

```
1. Open browser: http://localhost:5050/swagger
2. Find: POST /api/auth/login
3. Click "Try it out"
4. Paste this JSON:
   {
     "email": "admin@smartexam.com",
     "password": "Admin@123"
   }
5. Click "Execute"
6. Copy the token
7. Click green "Authorize" button at top
8. Paste token and authorize
9. Now test other endpoints!
```

### Option 2: Postman (Recommended for Production)

```
1. Download Postman from https://www.postman.com/downloads/
2. Create new collection "SmartExam API"
3. Add variables:
   - baseUrl = http://localhost:5050
   - token = (will be set by login)
4. Create POST request to {{baseUrl}}/api/auth/login
5. Same JSON body as above
6. Tests tab: pm.collectionVariables.set("token", body.token)
7. Send request
8. Token auto-saves for all future requests!
```

---

## 📋 Test Credentials

| User | Email | Password | Role | HWID |
|------|-------|----------|------|------|
| Admin | admin@smartexam.com | Admin@123 | Admin | N/A |
| Teacher | teacher@smartexam.com | Teacher@123 | Teacher | N/A |
| Student 1 | ali@smartexam.com | Student@123 | Student | `abc123fakeHWIDhashForTesting9999` |
| Student 2 | sara@smartexam.com | Student@123 | Student | `abc123fakeHWIDhashForTesting9999` |

---

## 🔑 Key Endpoints to Test

### Authentication
- **POST** `/api/auth/login` → Get JWT token
- **POST** `/api/auth/logout` → Revoke token
- **GET** `/api/auth/me` → Get current user

### Users (Admin Only)
- **GET** `/api/users` → List all users
- **GET** `/api/users?role=Student` → Filter by role
- **POST** `/api/users` → Create new user
- **DELETE** `/api/users/{id}/device-binding` → Reset device
- **POST** `/api/users/{id}/force-logout` → Logout user

### Exams
- **GET** `/api/exams` → Student sees assigned, Admin sees all
- **GET** `/api/exams/{id}` → Exam details + questions
- **POST** `/api/exams/{id}/start-session` → Begin exam
- **POST** `/api/exams/sessions/{sessionId}/save-answer` → Save answer
- **POST** `/api/exams/sessions/{sessionId}/monitoring-event` → Log activity
- **POST** `/api/exams/sessions/{sessionId}/submit` → Submit exam

---

## 🧪 Quick Test Flow (5 minutes)

```
1. Login as admin                          (200 OK)
2. Get current user                        (200 OK)
3. Get all users                           (200 OK - 4 users)
4. Get exams                               (200 OK - 1 exam)
5. Logout admin
6. Login as student (ali) with HWID        (200 OK, deviceBound=true)
7. Get exams (student sees assigned)       (200 OK)
8. Get exam details with questions         (200 OK - 2 questions)
9. Start session                           (200 OK - sessionId returned)
10. Save answer to question 1              (200 OK)
11. Record heartbeat monitoring event      (200 OK)
12. Submit exam                            (200 OK)
13. Try submitting again                   (400 Bad Request - session not active)
14. Try login with wrong HWID              (401 Unauthorized - device bound)
✅ All tests pass - API is working!
```

---

## 📊 Expected Response Examples

### Successful Login (200 OK)
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "userId": "a1b2c3d4-e5f6-47a8-9b0c-1d2e3f4a5b6c",
  "name": "Admin User",
  "email": "admin@smartexam.com",
  "role": "Admin",
  "deviceBound": false
}
```

### Get Users List (200 OK)
```json
[
  {
    "userId": "uuid",
    "name": "Admin User",
    "email": "admin@smartexam.com",
    "role": "Admin",
    "isActive": true,
    "deviceBound": false
  },
  // ... more users
]
```

### Get Exams (200 OK)
```json
[
  {
    "examId": "uuid",
    "title": "Mid-Term Lab Exam",
    "courseName": "Data Structures",
    "startTime": "2026-05-19T10:35:00Z",
    "durationMinutes": 60,
    "status": "Scheduled",
    "isEligible": true
  }
]
```

### Error Response (401 Unauthorized)
```json
{
  "message": "This account is bound to a different device. Contact your admin."
}
```

### Error Response (400 Bad Request)
```json
{
  "message": "Session is not active."
}
```

---

## 🎓 Learning Path

**Start Here →** [SmartExam_Testing_Guide.md](SmartExam_Testing_Guide.md)

Choose your testing method:
- **Swagger (Visual, No Setup)** → Section "OPTION 1"
- **Postman (Professional)** → Section "OPTION 2"

---

## ✅ What's Included

| Component | Status | Notes |
|-----------|--------|-------|
| Backend API | ✅ Running | Listening on http://localhost:5050 |
| Database | ✅ Connected | Neon PostgreSQL with 20 tables |
| Authentication | ✅ Implemented | JWT + Device Binding |
| Authorization | ✅ Implemented | Role-based (Admin, Teacher, Student) |
| Test Data | ✅ Seeded | 4 users, 1 exam, 2 questions, 2 test cases |
| Swagger Docs | ✅ Available | http://localhost:5050/swagger |
| 20+ Endpoints | ✅ Functional | All tested and working |

---

## 🔧 Troubleshooting

**API not running?**
```bash
cd c:\Users\DELL\Desktop\AI_Labmonitoring\SmartExam_Root\Backend_API
dotnet run --urls "http://localhost:5050"
```

**Port 5050 in use?**
```bash
dotnet run --urls "http://localhost:5051"  # Use different port
```

**Database connection failed?**
- Check Neon credentials in appsettings.json
- Verify internet connection
- Check firewall settings

**Token expired?**
- Get new token by logging in again
- Or check JWT:ExpiryMinutes in appsettings.json

---

## 📞 Quick Links

- 🔗 **API Base:** http://localhost:5050
- 🔗 **Swagger Docs:** http://localhost:5050/swagger
- 📄 **Build Guide:** SmartExam_Backend_Build_Guide.md
- 📄 **Testing Guide:** SmartExam_Testing_Guide.md
- 📁 **Source Code:** c:\Users\DELL\Desktop\AI_Labmonitoring\SmartExam_Root\Backend_API

---

**Ready to test?** Open [SmartExam_Testing_Guide.md](SmartExam_Testing_Guide.md) now! 🚀
