# SmartExam API — Testing Guide

**Last Updated:** May 19, 2026  
**API Running On:** `http://localhost:5050`

---

## Quick Comparison: Swagger vs Postman

| Feature | Swagger UI | Postman |
|---------|-----------|---------|
| **Best For** | Quick API exploration & testing | Comprehensive testing, CI/CD, team collaboration |
| **Authentication** | Manual token paste each time | Save token in variables (one-time) |
| **Test Scripts** | No scripting support | Full JavaScript support for validation |
| **Collections** | N/A | Organize tests in folders, reusable |
| **Report/Export** | No | Generate reports, export results |
| **Learning Curve** | Easiest (UI-based) | Medium (need Postman setup) |
| **Recommendation** | ✅ **Start here for quick testing** | ✅ **Use for production testing** |

---

## 🎯 OPTION 1: Testing with Swagger UI (Recommended for Quick Start)

### Why Swagger First?
- ✅ No software installation needed (already in browser)
- ✅ See API docs and test in one place
- ✅ Perfect for learning the API structure
- ✅ Real-time response preview

### Step 1: Open Swagger UI

```
Browser URL: http://localhost:5050/swagger
```

You should see:
- "SmartExam API v1" at the top
- All endpoints organized by controller (auth, users, exams)
- Try it out buttons for each endpoint

**Screenshot Location:**
```
├── GET /api/auth/me
├── POST /api/auth/login
├── POST /api/auth/logout
├── GET /api/users
├── GET /api/users/{id}
├── POST /api/users
├── DELETE /api/users/{id}/device-binding
├── POST /api/users/{id}/force-logout
├── PATCH /api/users/{id}/deactivate
├── GET /api/exams
├── GET /api/exams/{id}
├── POST /api/exams/{id}/start-session
├── POST /api/exams/sessions/{sessionId}/submit
├── POST /api/exams/sessions/{sessionId}/save-answer
└── POST /api/exams/sessions/{sessionId}/monitoring-event
```

---

### Step 2: Test Login Endpoint (Admin)

1. Find endpoint: **POST** `/api/auth/login`
2. Click **"Try it out"** button
3. Clear the example request body and paste:

```json
{
  "email": "admin@smartexam.com",
  "password": "Admin@123"
}
```

4. Click **"Execute"** button
5. Scroll down to see **Response**

**Expected Response (200 OK):**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiI...",
  "userId": "a1b2c3d4-e5f6-47a8-9b0c-1d2e3f4a5b6c",
  "name": "Admin User",
  "email": "admin@smartexam.com",
  "role": "Admin",
  "deviceBound": false
}
```

**🎯 ACTION:** Copy the `token` value (the long JWT string). You'll need this for authenticated endpoints.

---

### Step 3: Authorize All Endpoints with Token

1. Look for the green **"Authorize"** button at the top right of Swagger UI
2. Click it
3. Paste your token in the text field (without "Bearer " prefix - Swagger adds it)
4. Click **"Authorize"**
5. Click **"Close"**

Now all locked endpoints will work automatically!

---

### Step 4: Test Protected Endpoints

#### Test: Get Current User (GET /api/auth/me)

1. Find: **GET** `/api/auth/me`
2. Click **"Try it out"**
3. Click **"Execute"**

**Expected Response (200 OK):**
```json
{
  "userId": "a1b2c3d4-e5f6-47a8-9b0c-1d2e3f4a5b6c",
  "name": "Admin User",
  "email": "admin@smartexam.com",
  "role": "Admin"
}
```

#### Test: Get All Users (GET /api/users)

1. Find: **GET** `/api/users`
2. Click **"Try it out"**
3. Leave `role` parameter empty (or select "Student" to filter)
4. Click **"Execute"**

**Expected Response (200 OK):**
```json
[
  {
    "userId": "a1b2c3d4-e5f6-47a8-9b0c-1d2e3f4a5b6c",
    "name": "Admin User",
    "email": "admin@smartexam.com",
    "role": "Admin",
    "isActive": true,
    "createdAt": "2026-05-19T10:30:00Z",
    "deviceBound": false,
    "deviceRegisteredAt": null
  },
  {
    "userId": "teacher-uuid-here",
    "name": "Dr. Ahmed",
    "email": "teacher@smartexam.com",
    "role": "Teacher",
    "isActive": true,
    "createdAt": "2026-05-19T10:30:00Z",
    "deviceBound": false,
    "deviceRegisteredAt": null
  },
  {
    "userId": "student1-uuid-here",
    "name": "Ali Hassan",
    "email": "ali@smartexam.com",
    "role": "Student",
    "isActive": true,
    "createdAt": "2026-05-19T10:30:00Z",
    "deviceBound": true,
    "deviceRegisteredAt": "2026-05-19T10:30:00Z"
  },
  {
    "userId": "student2-uuid-here",
    "name": "Sara Khan",
    "email": "sara@smartexam.com",
    "role": "Student",
    "isActive": true,
    "createdAt": "2026-05-19T10:30:00Z",
    "deviceBound": true,
    "deviceRegisteredAt": "2026-05-19T10:30:00Z"
  }
]
```

#### Test: Get All Exams (GET /api/exams)

1. Find: **GET** `/api/exams`
2. Click **"Try it out"**
3. Click **"Execute"**

**Expected Response (200 OK):**
```json
[
  {
    "examId": "exam-uuid-here",
    "title": "Mid-Term Lab Exam",
    "startTime": "2026-05-19T10:35:00Z",
    "durationMinutes": 60,
    "status": "Scheduled",
    "courseName": "Data Structures",
    "sectionName": "BSCS-6A",
    "questionCount": 2
  }
]
```

---

### Step 5: Test Student Login with Device Binding

1. Logout first: Click **"Authorize"** button, clear the token field, click **"Authorize"**
2. Find: **POST** `/api/auth/login`
3. Click **"Try it out"**
4. Enter:

```json
{
  "email": "ali@smartexam.com",
  "password": "Student@123",
  "hwidHash": "abc123fakeHWIDhashForTesting9999"
}
```

5. Click **"Execute"**

**Expected Response (200 OK):**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "userId": "student1-uuid",
  "name": "Ali Hassan",
  "email": "ali@smartexam.com",
  "role": "Student",
  "deviceBound": true
}
```

**Key Point:** `deviceBound: true` means the device (HWID) was registered/verified.

---

### Step 6: Test Device Binding Violation

1. Update token with student's token from Step 5
2. Try logging in again with **different HWID**:

```json
{
  "email": "ali@smartexam.com",
  "password": "Student@123",
  "hwidHash": "COMPLETELY_DIFFERENT_HWID_XYZ"
}
```

**Expected Response (401 Unauthorized):**
```json
{
  "message": "This account is bound to a different device. Contact your admin."
}
```

✅ **This proves device security is working!**

---

### Step 7: Test Exam Workflow (As Student)

#### 1. Get Assigned Exams

1. Use student token (Ali)
2. **GET** `/api/exams`
3. Click **"Execute"**

**Expected:** Returns only exams assigned to this student

#### 2. Get Exam Details with Questions

1. Copy the `examId` from previous response
2. **GET** `/api/exams/{id}`
3. Paste the exam ID in the `id` parameter
4. Click **"Execute"**

**Expected Response (200 OK):**
```json
{
  "examId": "exam-uuid",
  "sectionId": "section-uuid",
  "title": "Mid-Term Lab Exam",
  "startTime": "2026-05-19T10:35:00Z",
  "durationMinutes": 60,
  "allowedApps": "[\"code.exe\",\"codeblocks.exe\"]",
  "aiEvaluationEnabled": true,
  "plagiarismThreshold": 70,
  "status": "Scheduled",
  "createdAt": "2026-05-19T10:30:00Z",
  "section": { ... },
  "questions": [
    {
      "questionId": "q1-uuid",
      "examId": "exam-uuid",
      "type": "Coding",
      "bodyText": "Write a C++ function that takes an array and returns the maximum element.",
      "marks": 20,
      "expectedOutput": null,
      "orderIndex": 1,
      "testCases": [
        {
          "testCaseId": "tc1-uuid",
          "questionId": "q1-uuid",
          "input": "5\n3 1 4 1 5",
          "expectedOutput": "5",
          "isHidden": false
        }
        // Note: Hidden test cases are NOT shown to students
      ]
    },
    {
      "questionId": "q2-uuid",
      "type": "Theory",
      "bodyText": "Explain the difference between a stack and a queue...",
      "marks": 10,
      "orderIndex": 2,
      "testCases": []
    }
  ]
}
```

#### 3. Start Exam Session

1. **POST** `/api/exams/{id}/start-session`
2. Paste exam ID
3. Click **"Execute"**

**Expected Response (200 OK):**
```json
{
  "sessionId": "session-uuid-here"
}
```

✅ **Save this sessionId - you need it for the next steps**

#### 4. Save an Answer

1. **POST** `/api/exams/sessions/{sessionId}/save-answer`
2. Paste session ID from previous step
3. Enter request body:

```json
{
  "questionId": "q1-uuid",
  "answerText": "int maxElement(int arr[], int n) { int max = arr[0]; for(int i=1;i<n;i++) if(arr[i]>max) max=arr[i]; return max; }"
}
```

4. Click **"Execute"**

**Expected Response (200 OK):**
```json
{
  "message": "Answer saved.",
  "lastSavedAt": "2026-05-19T10:45:30.123Z"
}
```

#### 5. Record Monitoring Event (Heartbeat)

1. **POST** `/api/exams/sessions/{sessionId}/monitoring-event`
2. Paste session ID
3. Enter request body:

```json
{
  "eventType": "Heartbeat",
  "payload": "{\"activeWindow\": \"Visual Studio Code\", \"processCount\": 5, \"timestamp\": \"2026-05-19T10:45:30Z\"}"
}
```

4. Click **"Execute"**

**Expected Response (200 OK):**
```json
{
  "message": "Event recorded."
}
```

#### 6. Submit Exam

1. **POST** `/api/exams/sessions/{sessionId}/submit`
2. Paste session ID
3. Click **"Execute"**

**Expected Response (200 OK):**
```json
{
  "message": "Exam submitted successfully.",
  "submittedAt": "2026-05-19T10:50:00Z"
}
```

#### 7. Try Submitting Again (Should Fail)

1. **POST** `/api/exams/sessions/{sessionId}/submit` (same session)
2. Click **"Execute"**

**Expected Response (400 Bad Request):**
```json
{
  "message": "Session is not active."
}
```

✅ **Session protection working!**

---

## 🔧 OPTION 2: Testing with Postman (Recommended for Production)

### Why Postman?
- ✅ Professional testing tool
- ✅ Save requests in collections for reuse
- ✅ Automatic token management with variables
- ✅ Test scripts for validation
- ✅ Easy team collaboration
- ✅ Generate reports

### Installation

1. Download from: https://www.postman.com/downloads/
2. Install and open Postman
3. Sign in or create account (optional but recommended)

---

### Step 1: Create Collection & Variables

1. **Create New Collection:**
   - Click **"+"** or **"Collections"** tab
   - Click **"Create new collection"**
   - Name: `SmartExam API`
   - Click **"Create"**

2. **Set Collection Variables:**
   - Right-click the collection name
   - Select **"Edit"**
   - Go to **"Variables"** tab
   - Add these variables:

| Variable | Initial Value | Current Value |
|----------|---------------|---------------|
| `baseUrl` | `http://localhost:5050` | `http://localhost:5050` |
| `token` | *(leave empty)* | *(will be set after login)* |
| `adminId` | *(leave empty)* | *(will be set after login)* |
| `studentId` | *(leave empty)* | *(will be set after student login)* |
| `examId` | *(leave empty)* | *(will be set after getting exams)* |
| `sessionId` | *(leave empty)* | *(will be set after starting session)* |

3. **Set Collection Auth:**
   - Still in collection edit view
   - Go to **"Authorization"** tab
   - Type: Select **"Bearer Token"**
   - Token: `{{token}}`
   - Click **"Save"**

✅ Now every request will automatically use the token!

---

### Step 2: Create Login Request

1. **New Request:**
   - Click **"+"** button in tabs
   - Name: `Admin Login`
   - Select collection: `SmartExam API`
   - Click **"Save to SmartExam API"**

2. **Configure Request:**
   - **Method:** POST
   - **URL:** `{{baseUrl}}/api/auth/login`
   - **Headers:**
     - Content-Type: `application/json` (auto-added by Postman)

3. **Body:**
   - Select **"raw"** → **"JSON"**
   - Paste:
   ```json
   {
     "email": "admin@smartexam.com",
     "password": "Admin@123"
   }
   ```

4. **Test Script** (go to **"Tests"** tab):
   ```javascript
   pm.test("Status is 200", () => pm.response.to.have.status(200));

   pm.test("Token is returned", () => {
       const body = pm.response.json();
       pm.expect(body.token).to.be.a("string").and.not.empty;
       pm.expect(body.role).to.equal("Admin");
       
       // Save token for subsequent requests
       pm.collectionVariables.set("token", body.token);
       pm.collectionVariables.set("adminId", body.userId);
       console.log("✅ Token saved");
   });

   pm.test("Response has all required fields", () => {
       const body = pm.response.json();
       pm.expect(body).to.have.all.keys("token", "userId", "name", "email", "role", "deviceBound");
   });
   ```

5. **Send Request**
   - Click **"Send"**
   - You should see **Status 200 OK**
   - View response in lower panel

**Expected Response:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "userId": "admin-uuid",
  "name": "Admin User",
  "email": "admin@smartexam.com",
  "role": "Admin",
  "deviceBound": false
}
```

✅ **Token automatically saved to `{{token}}` variable!**

---

### Step 3: Get Current User (Test Auth)

1. **New Request:**
   - Name: `Get Me`
   - Method: **GET**
   - URL: `{{baseUrl}}/api/auth/me`

2. **Send**

**Expected Response (200):**
```json
{
  "userId": "admin-uuid",
  "name": "Admin User",
  "email": "admin@smartexam.com",
  "role": "Admin"
}
```

✅ **Authorization working! Token is automatically included.**

---

### Step 4: Get All Users

1. **New Request:**
   - Name: `Get All Users`
   - Method: **GET**
   - URL: `{{baseUrl}}/api/users`

2. **Params (optional):**
   - Key: `role` | Value: (leave empty or select "Student")

3. **Tests Tab:**
   ```javascript
   pm.test("Status is 200", () => pm.response.to.have.status(200));
   
   pm.test("Returns array of users", () => {
       const body = pm.response.json();
       pm.expect(body).to.be.an("array");
       pm.expect(body.length).to.be.greaterThan(0);
       console.log("Total users:", body.length);
   });
   ```

4. **Send**

**Expected Response (200):**
```json
[
  {
    "userId": "...",
    "name": "Admin User",
    "email": "admin@smartexam.com",
    "role": "Admin",
    "isActive": true,
    "createdAt": "2026-05-19T10:30:00Z",
    "deviceBound": false,
    "deviceRegisteredAt": null
  },
  // ... more users
]
```

---

### Step 5: Create New User (Admin Only)

1. **New Request:**
   - Name: `Create New Student`
   - Method: **POST**
   - URL: `{{baseUrl}}/api/users`

2. **Body (raw JSON):**
   ```json
   {
     "name": "Test Student",
     "email": "test@smartexam.com",
     "password": "Test@1234",
     "role": "Student"
   }
   ```

3. **Tests:**
   ```javascript
   pm.test("Status is 201 Created", () => pm.response.to.have.status(201));
   
   pm.test("User created successfully", () => {
       const body = pm.response.json();
       pm.expect(body.role).to.equal("Student");
       pm.expect(body.email).to.equal("test@smartexam.com");
       pm.collectionVariables.set("newStudentId", body.userId);
   });
   ```

4. **Send**

**Expected Response (201):**
```json
{
  "userId": "new-student-uuid",
  "name": "Test Student",
  "email": "test@smartexam.com",
  "role": "Student"
}
```

---

### Step 6: Test Device Binding (Student Login)

1. **New Request:**
   - Name: `Student Login (First Time)`
   - Method: **POST**
   - URL: `{{baseUrl}}/api/auth/login`

2. **Body:**
   ```json
   {
     "email": "ali@smartexam.com",
     "password": "Student@123",
     "hwidHash": "abc123fakeHWIDhashForTesting9999"
   }
   ```

3. **Tests:**
   ```javascript
   pm.test("Status is 200", () => pm.response.to.have.status(200));
   
   pm.test("Device is bound", () => {
       const body = pm.response.json();
       pm.expect(body.deviceBound).to.be.true;
       pm.collectionVariables.set("studentId", body.userId);
       console.log("✅ Student device bound and ID saved");
   });
   ```

4. **Send**

**Expected Response (200):**
```json
{
  "token": "...",
  "userId": "ali-uuid",
  "name": "Ali Hassan",
  "email": "ali@smartexam.com",
  "role": "Student",
  "deviceBound": true
}
```

---

### Step 7: Test Device Binding Violation

1. **New Request:**
   - Name: `Student Login (Wrong Device)`
   - Method: **POST**
   - URL: `{{baseUrl}}/api/auth/login`

2. **Body:**
   ```json
   {
     "email": "ali@smartexam.com",
     "password": "Student@123",
     "hwidHash": "COMPLETELY_DIFFERENT_HWID"
   }
   ```

3. **Tests:**
   ```javascript
   pm.test("Status is 401 Unauthorized", () => pm.response.to.have.status(401));
   
   pm.test("Error message is correct", () => {
       const body = pm.response.json();
       pm.expect(body.message).to.include("different device");
   });
   ```

4. **Send**

**Expected Response (401):**
```json
{
  "message": "This account is bound to a different device. Contact your admin."
}
```

✅ **Device security verified!**

---

### Step 8: Get Exams

1. **New Request:**
   - Name: `Get Exams`
   - Method: **GET**
   - URL: `{{baseUrl}}/api/exams`

2. **Tests:**
   ```javascript
   pm.test("Status is 200", () => pm.response.to.have.status(200));
   
   pm.test("Exams returned", () => {
       const body = pm.response.json();
       pm.expect(body).to.be.an("array");
       if (body.length > 0) {
           pm.collectionVariables.set("examId", body[0].examId);
           console.log("✅ Exam ID saved:", body[0].examId);
       }
   });
   ```

3. **Send**

**Expected Response (200):**
```json
[
  {
    "examId": "exam-uuid",
    "title": "Mid-Term Lab Exam",
    "courseName": "Data Structures",
    "startTime": "2026-05-19T10:35:00Z",
    "durationMinutes": 60,
    "status": "Scheduled",
    "isEligible": true,
    "workstationNumber": "PC-01"
  }
]
```

---

### Step 9: Start Exam Session

1. **New Request:**
   - Name: `Start Exam Session`
   - Method: **POST**
   - URL: `{{baseUrl}}/api/exams/{{examId}}/start-session`

2. **Tests:**
   ```javascript
   pm.test("Status is 200", () => pm.response.to.have.status(200));
   
   pm.test("Session ID returned", () => {
       const body = pm.response.json();
       pm.expect(body.sessionId).to.be.a("string");
       pm.collectionVariables.set("sessionId", body.sessionId);
       console.log("✅ Session ID saved:", body.sessionId);
   });
   ```

3. **Send**

**Expected Response (200):**
```json
{
  "sessionId": "session-uuid-here"
}
```

---

### Step 10: Save Answer

1. **New Request:**
   - Name: `Save Answer`
   - Method: **POST**
   - URL: `{{baseUrl}}/api/exams/sessions/{{sessionId}}/save-answer`

2. **Body:**
   ```json
   {
     "questionId": "q1-uuid",
     "answerText": "int maxElement(int arr[], int n) { int max = arr[0]; for(int i=1;i<n;i++) if(arr[i]>max) max=arr[i]; return max; }"
   }
   ```

3. **Tests:**
   ```javascript
   pm.test("Status is 200", () => pm.response.to.have.status(200));
   
   pm.test("Answer saved", () => {
       const body = pm.response.json();
       pm.expect(body.message).to.equal("Answer saved.");
       pm.expect(body.lastSavedAt).to.be.a("string");
   });
   ```

4. **Send**

**Expected Response (200):**
```json
{
  "message": "Answer saved.",
  "lastSavedAt": "2026-05-19T10:45:30.123Z"
}
```

---

### Step 11: Record Monitoring Event

1. **New Request:**
   - Name: `Record Heartbeat`
   - Method: **POST**
   - URL: `{{baseUrl}}/api/exams/sessions/{{sessionId}}/monitoring-event`

2. **Body:**
   ```json
   {
     "eventType": "Heartbeat",
     "payload": "{\"activeWindow\": \"VS Code\", \"timestamp\": \"2026-05-19T10:45:00Z\"}"
   }
   ```

3. **Tests:**
   ```javascript
   pm.test("Status is 200", () => pm.response.to.have.status(200));
   
   pm.test("Event recorded", () => {
       const body = pm.response.json();
       pm.expect(body.message).to.equal("Event recorded.");
   });
   ```

4. **Send**

**Expected Response (200):**
```json
{
  "message": "Event recorded."
}
```

---

### Step 12: Submit Exam

1. **New Request:**
   - Name: `Submit Exam`
   - Method: **POST**
   - URL: `{{baseUrl}}/api/exams/sessions/{{sessionId}}/submit`

2. **Tests:**
   ```javascript
   pm.test("Status is 200", () => pm.response.to.have.status(200));
   
   pm.test("Exam submitted", () => {
       const body = pm.response.json();
       pm.expect(body.message).to.include("submitted");
       pm.expect(body.submittedAt).to.be.a("string");
   });
   ```

3. **Send**

**Expected Response (200):**
```json
{
  "message": "Exam submitted successfully.",
  "submittedAt": "2026-05-19T10:50:00Z"
}
```

---

### Step 13: Double Submit (Should Fail)

1. **New Request:**
   - Name: `Double Submit (Should Fail)`
   - Method: **POST**
   - URL: `{{baseUrl}}/api/exams/sessions/{{sessionId}}/submit`

2. **Tests:**
   ```javascript
   pm.test("Status is 400 Bad Request", () => pm.response.to.have.status(400));
   
   pm.test("Session protection working", () => {
       const body = pm.response.json();
       pm.expect(body.message).to.include("not active");
   });
   ```

3. **Send**

**Expected Response (400):**
```json
{
  "message": "Session is not active."
}
```

✅ **Session protection verified!**

---

## 📊 Full Test Execution Order

### Quick Reference Checklist

**For Swagger (Simplest):**
```
1. ✅ Open http://localhost:5050/swagger
2. ✅ Test: POST /api/auth/login (Admin)
3. ✅ Click Authorize, paste token
4. ✅ Test: GET /api/auth/me
5. ✅ Test: GET /api/users
6. ✅ Test: GET /api/exams
7. ✅ Logout and test student flow
8. ✅ Done!
```

**For Postman (Professional):**
```
1. ✅ Create collection "SmartExam API"
2. ✅ Add variables (baseUrl, token, etc.)
3. ✅ Create request: Admin Login
4. ✅ Create request: Get Me
5. ✅ Create request: Get All Users
6. ✅ Create request: Create New Student
7. ✅ Create request: Student Login
8. ✅ Create request: Student Login Wrong Device
9. ✅ Create request: Get Exams
10. ✅ Create request: Start Session
11. ✅ Create request: Save Answer
12. ✅ Create request: Record Heartbeat
13. ✅ Create request: Submit Exam
14. ✅ Create request: Double Submit (should fail)
15. ✅ Run all tests in order
16. ✅ Export collection for backup
```

---

## 🐛 Troubleshooting

### Issue: 401 Unauthorized on protected endpoints

**Cause:** Token not set or expired
**Solution:**
- Swagger: Click "Authorize" button and paste fresh token
- Postman: Re-run login request to get new token

### Issue: 403 Forbidden when creating user

**Cause:** Only Admin/SuperAdmin can create users
**Solution:**
- Use admin token, not student token
- Check your collection variables

### Issue: "Invalid email or password"

**Cause:** Wrong credentials
**Solution:**
- Verify you're using correct test credentials
- Check email spelling (case-insensitive)

### Issue: "This account is bound to a different device"

**Cause:** Student login with wrong HWID
**Solution:**
- Use the same HWID as first login: `abc123fakeHWIDhashForTesting9999`
- Or reset binding via admin API

### Issue: "Session is not active"

**Cause:** Trying to save answer after submitting
**Solution:**
- Don't reuse sessionId after submission
- Start a new session first

---

## 🎯 Recommendation

**For Testing Now:** ✅ **Use Swagger** - Quick, no setup
**For Production:** ✅ **Use Postman** - Professional, automatable

Both approaches verify the API is working correctly!

---

**Questions?** Check the logs in the terminal where API is running for detailed error messages.
