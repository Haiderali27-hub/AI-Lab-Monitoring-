import matplotlib.pyplot as plt

sprints = [
    ('Sprint 1: Requirements and Planning', 1, 4, '#1f77b4'),
    ('Sprint 2: Authentication and Roles', 5, 8, '#2ca02c'),
    ('Sprint 3: Scheduling and Questions', 9, 12, '#ff7f0e'),
    ('Sprint 4: Lab Allocation and Control', 13, 16, '#9467bd'),
    ('Sprint 5: Attendance and Face Recognition', 17, 20, '#8c564b'),
    ('Sprint 6: AI Monitoring and Incident Logs', 21, 24, '#d62728'),
    ('Sprint 7: Results and Reporting', 25, 28, '#17becf'),
    ('Sprint 8: Integration, Testing, Refinement', 29, 32, '#7f7f7f'),
]

fig, ax = plt.subplots(figsize=(14, 8))
for i, (label, start, end, color) in enumerate(sprints):
    ax.barh(i, end - start + 1, left=start, height=0.6, color=color, edgecolor='black')
    ax.text(start + 0.2, i, f'Weeks {start}-{end}', va='center', ha='left', fontsize=9, color='white', fontweight='bold')

ax.set_yticks(range(len(sprints)))
ax.set_yticklabels([s[0] for s in sprints], fontsize=10)
ax.invert_yaxis()
ax.set_xticks(range(1, 33))
ax.set_xlim(1, 33)
ax.set_xlabel('Project Timeline in Weeks', fontsize=11)
ax.set_title('AI-Driven Lab Exam Monitoring and Management System\nGantt Chart Based on Eight Agile Sprints', fontsize=13, pad=12)
ax.grid(axis='x', linestyle='--', alpha=0.4)
plt.tight_layout()
plt.savefig('/home/ubuntu/fyp_edit_work_2/Figures/gantt_chart.png', dpi=300, bbox_inches='tight')
plt.savefig('/home/ubuntu/fyp_edit_work_2/Fig/gantt_chart.png', dpi=300, bbox_inches='tight')
