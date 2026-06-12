import os

target_dir = r"c:\Users\DELL\Desktop\AI_Labmonitoring\SmartExam_Root\Admin_Web_Panel\stitch_exports"
os.makedirs(target_dir, exist_ok=True)

mappings = {
    "1136": "analytics_system.html",
    "1172": "analytics_exam_detail.html",
    "1193": "analytics_reports.html",
    "1164": "student_dashboard.html",
    "1195": "student_my_exams.html",
    "1197": "student_exam_result.html",
    "1199": "student_performance.html",
    "1201": "student_violations.html",
    "1203": "notif_bell_dropdown.html",
    "1205": "notif_admin_page.html",
    "1176": "notif_announcement.html",
    "1207": "notif_student_page.html"
}

for step, name in mappings.items():
    src = rf"C:\Users\DELL\.gemini\antigravity\brain\8aea0c2b-45e8-48a1-bed5-bdf52d3d65d3\.system_generated\steps\{step}\content.md"
    dest = os.path.join(target_dir, name)
    if os.path.exists(src):
        with open(src, "r", encoding="utf-8") as f:
            lines = f.readlines()
        # Skip markdown header (first 8 lines)
        html_content = "".join(lines[8:])
        with open(dest, "w", encoding="utf-8") as f:
            f.write(html_content)
        print(f"Exported {name} from step {step}")
    else:
        print(f"ERROR: {src} not found!")
