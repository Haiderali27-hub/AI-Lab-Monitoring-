# 🧪 SmartExam — Step-by-Step Manual Testing Guide
This guide provides an end-to-end walkthrough for manually testing the entire SmartExam platform, including the **Admin Portal**, **Teacher Dashboard**, **Student Simulation**, and **Real-Time Proctoring**.

---

## 📋 Prerequisites
Before testing, make sure both systems are running:
* **Backend API:** Running on `http://localhost:5050` (`dotnet run` in `Backend_API`)
* **React Web App:** Running on `http://localhost:5173` (`npm run dev` in `Admin_Web_Panel`)

---

## 👤 Scenario 1: The Administrator Journey (User & Resource Onboarding)
The Administrator sets up the university environment, coordinates devices, and inspects logs.

### 1. Login
1. Open `http://localhost:5173` in your browser.
2. Enter the administrator credentials:
   * **Email:** `admin@smartexam.com`
   * **Password:** `Admin@123`
3. Click **Sign In**.
4. **Expected Result:** You are redirected to `/admin/dashboard`, and you see cards showing stats (Total Students, Active Exams, Labs, and Pending resets).

### 2. User Management
1. Click **Users** in the left sidebar.
2. Observe the list of seeded users. Click on different tabs (*Students*, *Teachers*, *Admins*) to test filtering.
3. Click **Add User** (blue button on the top right).
4. Fill in:
   * **Name:** `Test Student`
   * **Email:** `teststudent@smartexam.com`
   * **Password:** `Student@123`
   * **Role:** `Student`
5. Click **Create User**.
6. **Expected Result:** The slide-over panel closes, and the new student appears in the users table.

### 3. Device Bindings
1. Click **Device Bindings** in the sidebar.
2. You will see students associated with machine HWID hashes.
3. Click **Reset Binding** on a student row.
4. **Expected Result:** The binding resets, changing their status to "Unbound". This allows that student to log in from a new machine next time.

### 4. Labs & Diagnostics
1. Click **Labs & Workstations** in the sidebar.
2. Observe the grid layout representing physical PC structures.
3. Click a workstation grid tile.
4. **Expected Result:** A sidebar expands, displaying the machine's IP, CPU performance graph, and memory diagnostic telemetry.

### 5. Audit logs
1. Click **Audit Logs** in the sidebar.
2. Review the chronological trail of system actions. Use the search input or severity filters (*Info, Success, Warning, Danger*).
3. Click **View** on a log row.
4. **Expected Result:** A code drawer expands displaying the full JSON structure of the recorded system event.
5. Click **Logout** at the bottom-left of the sidebar to end the Admin session.

---

## 🧑‍🏫 Scenario 2: The Teacher Journey (Exam Creation & Setup)
Teachers create exams, set allowed process policies, assign students, and configure eligibility.

### 1. Login
1. Enter the teacher credentials:
   * **Email:** `teacher@smartexam.com`
   * **Password:** `Teacher@123`
2. **Expected Result:** Redirects you to `/teacher/dashboard` showing a welcome banner for Dr. Ahmed.

### 2. Student Eligibility Setup
1. Click **Eligibility** in the sidebar.
2. Set one of the students (e.g. *Sara Khan*) to **Ineligible** by clicking their toggle switch.
3. Type in the Note input: `Attendance shortage (70%)`.
4. Click **Save Changes** (bottom right).
5. **Expected Result:** Status updates are saved. Sara Khan will be blocked from launching exams until this is updated.

### 3. Create a Scheduled Exam
1. Click **Create Exam** in the sidebar. This opens the **4-step creation wizard**:
   * **Step 1 (Basic Info):**
     * Title: `Final Programming Assessment`
     * Course/Section: Select `BSCS-6A`
     * Date: Set today's date
     * Time: Set the start time (e.g., `09:00`)
     * Duration: `120 minutes`
     * Instructions: `Solve both questions. Closed book.`
     * Click **Next: Add Questions**.
   * **Step 2 (Questions):**
     * Click **Add Question**. Set type to `Coding`, input prompt: `Write a program that returns the maximum element in an integer array.`, and set marks to `20`.
     * Click **Add Test Case** inside this question. Enter Input: `5\n3 1 4 1 5`, Expected Output: `5`. Toggle *Is Hidden* to off (visible).
     * Click **Add Question** again. Set type to `Theory`, input prompt: `Differentiate between static and dynamic allocation.`, and set marks to `10`.
     * Click **Next: Settings**.
   * **Step 3 (Rules):**
     * Allowed Applications: Type `code.exe, visualstudio.exe, cl.exe`.
     * Enable **AI Evaluation**.
     * Drag the **Plagiarism Similarity Threshold** slider to `75%`.
     * Click **Next: Assign Students**.
   * **Step 4 (Students):**
     * Check the checkbox next to `Ali Hassan`. (Notice that `Sara Khan` is flagged or ineligible based on your earlier setup).
     * Click **Create Exam**.
2. **Expected Result:** The exam is created. You are redirected to the dashboard, and `Final Programming Assessment` appears under **Upcoming Exams**.

---

## 💻 Scenario 3: The Student Journey (Exam Client Simulation)
Since the student C# WPF client is designed for student terminals, we can simulate a student's active exam session using **Swagger** to see how it connects with our API and React Web panel.

### 1. Log in and Authorize in Swagger
1. Open `http://localhost:5050/swagger` in your browser.
2. Expand **POST** `/api/auth/login`. Click **Try it out**.
3. Replace the body with Ali's credentials and HWID:
   ```json
   {
     "email": "ali@smartexam.com",
     "password": "Student@123",
     "hwidHash": "abc123fakeHWIDhashForTesting9999"
   }
   ```
4. Click **Execute**.
5. Copy the long `"token"` string from the `200 OK` response.
6. Scroll to the top of Swagger, click the green **Authorize** button, paste the token, and click **Authorize**.

### 2. Start the Exam
1. Expand **GET** `/api/exams` and click **Execute**.
2. Copy the `"examId"` of your newly created exam.
3. Expand **POST** `/api/exams/{id}/start-session`. Click **Try it out**.
4. Paste the `"examId"` into the parameter field and click **Execute**.
5. **Expected Result:** Returns a `200 OK` with a `"sessionId"` (e.g. `d4b858ae-63ef...`). **Copy this sessionId**.

### 3. Save Answers
1. Expand **POST** `/api/exams/sessions/{sessionId}/save-answer`. Click **Try it out**.
2. Paste the `"sessionId"`.
3. In the request body, insert the question ID and the answer:
   ```json
   {
     "questionId": "your-coding-question-id",
     "answerText": "int maxElement(int arr[], int n) { int max = arr[0]; for(int i=1; i<n; i++) { if(arr[i] > max) max = arr[i]; } return max; }"
   }
   ```
4. Click **Execute**. (Result: `200 OK - Answer saved.`)

---

## 🚨 Scenario 4: Live Proctoring (SignalR Streams & Events)
See the proctoring dashboard update live in response to student client actions.

1. **Open Live Monitor:**
   * In your React Web App (logged in as the teacher), click **Live Monitor** in the sidebar.
   * You should see the running exam and a tile for **Ali Hassan** showing `Active` status.
2. **Stream a Suspicious Event:**
   * Go back to Swagger and simulate the student opening an unapproved application (like Chrome).
   * Expand **POST** `/api/exams/sessions/{sessionId}/monitoring-event`.
   * Paste the student's `"sessionId"`.
   * Paste the following body:
     ```json
     {
       "eventType": "Violation",
       "payload": "{\"activeWindow\": \"chrome.exe - StackOverflow search\", \"processCount\": 22, \"timestamp\": \"2026-06-02T19:45:00Z\"}"
     }
     ```
   * Click **Execute**.
3. **Verify Live Updates:**
   * Look back at the React web page **Live Monitor** tab.
   * **Expected Result:** **Ali Hassan's tile immediately flashes red/yellow**, displaying the violation count. A red warning banner is appended to the top of the monitor. 
   * Click Ali Hassan's card. The timeline panel on the right slides open, displaying the focus change event dynamically.

---

## 📊 Scenario 5: Submission & AI grading
Verify the exam completion and review grades.

1. **Submit the Exam (via Swagger):**
   * In Swagger, expand **POST** `/api/exams/sessions/{sessionId}/submit`.
   * Paste the `"sessionId"` and click **Execute**. (Result: `200 OK - Exam submitted successfully.`)
2. **Review Results in React:**
   * On your React Web App (logged in as the teacher), click **Results & Grading** in the sidebar.
   * Select your exam.
   * You will see the submissions table with AI-suggested marks and robot icons.
   * Click **Review** on Ali Hassan's row.
   * **Expected Result:** The review drawer expands, showing the question, Ali's code block on the left, and the AI justification box (e.g. *Suggested score: 18/20, High confidence*) on the right.
3. **Override Score:**
   * Change the score to `19` in the **Override Mark** input, add a note: `Good optimized loop`, and click **Apply Grade**.
   * **Expected Result:** The score is saved, and the dashboard statistics reload, re-averaging the class scores.

---

## 📊 Scenario 6: Reporting & Analytics (Teacher & Admin)
Teachers and Admins can view aggregate statistics for individual exams and the entire system, as well as generate exportable PDF reports.

### 1. Retrieve Exam Summary Analytics (Teacher/Admin)
1. Open Swagger at `http://localhost:5050/swagger` and authorize using the **Teacher** token.
2. Expand **GET** `/api/analytics/exams/{examId}/summary`. Click **Try it out**.
3. Paste the `"examId"` from your exam and click **Execute**.
4. **Expected Result:** Returns a `200 OK` response containing:
   - Aggregated exam details (Total Students, Pass Rate, Average Score, High/Low Scores).
   - Score distribution buckets (0-20, 21-40, 41-60, 61-80, 81-100).
   - Per-question statistics (average marks earned, difficulty percentage).
   - Total monitoring violations and count of students with violations.

### 2. Generate and Download PDF Exam Report (Teacher/Admin)
1. Expand **POST** `/api/analytics/exams/{examId}/report`. Click **Try it out**.
2. Paste the `"examId"` and click **Execute**.
3. **Expected Result:** Returns a `200 OK` with a download URL (e.g. `"/api/analytics/reports/Exam_Report_....pdf"`).
4. Copy the filename from the response (e.g. `Exam_Report_....pdf`).
5. Expand **GET** `/api/analytics/reports/{fileName}`. Click **Try it out**.
6. Paste the filename in the parameter field and click **Execute**.
7. **Expected Result:** Returns a `200 OK` with `Content-Type: application/pdf` containing the rendered document.

### 3. Retrieve System-Wide Analytics (Admin Only)
1. Authorize in Swagger using the **Admin** token.
2. Expand **GET** `/api/analytics/system`. Click **Try it out** and click **Execute**.
3. **Expected Result:** Returns a `200 OK` response containing:
   - System user totals (students, teachers).
   - Exam count metrics (total exams, last 30 days).
   - Last 30 days violation trend counts by day.
   - Month-by-month historical exam counts.
   - Top courses list by exam volume.
4. *Security Check:* Try hitting this endpoint with a **Student** or **Teacher** token. **Expected Result:** `403 Forbidden`.

---

## 🧑‍🎓 Scenario 7: Student Portal & Notifications (Student)
Students can log in to a dedicated portal on their own device to view performance trends, review graded exams (after completion), check violation logs, and receive in-app and email notifications.

### 1. Student Dashboard & Trends
1. Authorize in Swagger using the **Student** (Ali) token.
2. Expand **GET** `/api/student/dashboard`. Click **Try it out** and **Execute**.
3. **Expected Result:** Returns a `200 OK` containing Ali's total exams taken, average score, exams passed, total violations, recent exams list, and chronological performance trend array.
4. Expand **GET** `/api/student/performance-trend`. Click **Try it out** and **Execute**.
5. **Expected Result:** Returns a `200 OK` array of exam titles, dates, and score percentages (used to plot the student's performance line chart).

### 2. Detailed Graded Exam Review (With AI Feedback)
1. Expand **GET** `/api/student/exams/{examId}/result`. Click **Try it out**.
2. Paste the `"examId"` (ensure the exam status is set to `Ended` in the database, otherwise it returns a `400 Bad Request` saying results are not available yet).
3. Click **Execute**.
4. **Expected Result:** Returns a `200 OK` showing:
   - Total marks, earned marks, score percent, and pass status.
   - List of student answers containing the question text, student's answer text, earned marks, and the **AI grading result** (suggested marks, justification, and confidence level).

### 3. Notification Hub & Announcements
1. In Swagger (with the **Student** token authorized), expand **GET** `/api/student/notifications` and click **Execute**.
2. **Expected Result:** Returns a list of notifications and `unreadCount` (including scheduling, reminders, grades released, or eligibility updates).
3. Copy a `"notificationId"` from the list.
4. Expand **PATCH** `/api/student/notifications/{notificationId}/read`. Click **Try it out**, paste the ID, and click **Execute**.
5. **Expected Result:** Returns `200 OK` marking the notification as read. Re-run `GET /api/student/notifications` to verify `unreadCount` decremented.

