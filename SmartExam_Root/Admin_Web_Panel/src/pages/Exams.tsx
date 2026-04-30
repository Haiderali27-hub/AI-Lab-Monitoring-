import { useState } from 'react'
import { AdminLayout } from '../components/AdminLayout'

export default function Exams() {
  const [isCreating, setIsCreating] = useState(false)

  if (isCreating) {
    return (
      <AdminLayout>
        <div className="dashboard-content">
          <header className="page-header">
            <div className="header-text">
              <p className="eyebrow">Exam Management</p>
              <h1>Create New Exam</h1>
              <p className="subtext">Configure details, proctors, and candidate assignments.</p>
            </div>
            <div className="header-actions">
              <button className="secondary-btn" onClick={() => setIsCreating(false)}>
                Cancel
              </button>
              <button className="primary-btn">
                Publish Exam
              </button>
            </div>
          </header>

          <div className="exam-grid">
            <div className="exam-main">
              <section className="glass-card form-section">
                <div className="section-title-row">
                  <span className="material-symbols-outlined">edit_note</span>
                  <h3>Exam Details</h3>
                </div>
                <div className="form-grid-2">
                  <div className="input-group col-span-2" style={{ gridColumn: 'span 2' }}>
                    <label className="input-label">Exam Title</label>
                    <input className="input-control" placeholder="e.g. Advanced Cybersecurity Finals 2024" />
                  </div>
                  <div className="input-group">
                    <label className="input-label">Date</label>
                    <input className="input-control" type="date" />
                  </div>
                  <div className="input-group">
                    <label className="input-label">Duration (Minutes)</label>
                    <input className="input-control" type="number" placeholder="120" />
                  </div>
                  <div className="input-group">
                    <label className="input-label">Start Time</label>
                    <input className="input-control" type="time" />
                  </div>
                  <div className="input-group">
                    <label className="input-label">End Time</label>
                    <input className="input-control" type="time" />
                  </div>
                </div>
              </section>

              <section className="glass-card form-section">
                <div className="section-title-row">
                  <span className="material-symbols-outlined">settings_input_component</span>
                  <h3>Configuration</h3>
                </div>
                <div className="form-grid-2">
                  <div className="input-group">
                    <label className="input-label">Select Lab/Room</label>
                    <select className="select-control">
                      <option>Main Computer Lab A (Cap: 50)</option>
                      <option>Science Wing Lab 2 (Cap: 30)</option>
                      <option>Remote/Virtual Proctoring</option>
                    </select>
                  </div>
                  <div className="input-group">
                    <label className="input-label">Assign Teacher Proctor</label>
                    <select className="select-control">
                      <option>Dr. Sarah Jenkins</option>
                      <option>Marcus Thorne</option>
                      <option>Elena Rodriguez</option>
                    </select>
                  </div>
                </div>
              </section>

              <section className="glass-card student-assign-card">
                <div className="section-title-row">
                  <span className="material-symbols-outlined">group_add</span>
                  <h3>Student Assignment</h3>
                </div>
                <div className="search-bar-v2">
                  <span className="material-symbols-outlined">search</span>
                  <input placeholder="Search by name or student ID..." />
                </div>
                <div className="student-list">
                  {[
                    { name: 'Alexander Wright', id: '29301', status: 'Eligible' },
                    { name: 'Mia Chen', id: '29442', status: 'Eligible' },
                    { name: 'Julian Vane', id: '29012', status: 'Ineligible' },
                    { name: 'Sophia Sterling', id: '29111', status: 'Eligible' },
                  ].map((s) => (
                    <div key={s.id} className="student-item">
                      <div className="student-item-left">
                        <input type="checkbox" className="custom-checkbox" defaultChecked={s.status === 'Eligible'} />
                        <div className="student-details">
                          <span className="student-name">{s.name}</span>
                          <span className="student-email">ID: {s.id}</span>
                        </div>
                      </div>
                      <span className={`status-pill ${s.status === 'Eligible' ? 'unbound' : 'bound'}`}>
                        {s.status}
                      </span>
                    </div>
                  ))}
                </div>
              </section>
            </div>

            <aside className="exam-sidebar">
              <section className="glass-card assignment-summary-card">
                <h3 className="font-h3 mb-6">Assignment Summary</h3>
                <div className="summary-stat">
                  <p className="summary-stat-label">Total Assigned</p>
                  <p className="summary-stat-value">42</p>
                </div>
                <div className="form-grid-2 mb-6">
                  <div className="bg-surface-alt p-4 rounded-lg">
                    <p className="summary-stat-label" style={{ fontSize: '0.6rem' }}>Eligible</p>
                    <p className="font-bold text-green-600">38</p>
                  </div>
                  <div className="bg-surface-alt p-4 rounded-lg">
                    <p className="summary-stat-label" style={{ fontSize: '0.6rem' }}>Ineligible</p>
                    <p className="font-bold text-red-600">4</p>
                  </div>
                </div>
                <div className="capacity-info">
                  <div className="capacity-header">
                    <span>Room Capacity Status</span>
                    <span>84% Full</span>
                  </div>
                  <div className="capacity-bar">
                    <div className="capacity-fill" style={{ width: '84%' }}></div>
                  </div>
                </div>
                <div className="proctor-preview">
                  <img className="proctor-avatar" src="https://ui-avatars.com/api/?name=Sarah+Jenkins&background=0040a1&color=fff" alt="SJ" />
                  <div className="proctor-info">
                    <span className="proctor-name">Dr. Sarah Jenkins</span>
                    <span className="proctor-role">Head of Science Dept.</span>
                  </div>
                </div>
                <div className="alert-card">
                  <span className="material-symbols-outlined">warning</span>
                  <div className="alert-text">
                    <span className="alert-label">SYSTEM ALERT</span>
                    <p className="alert-message">4 students have unresolved security flags. review recommended.</p>
                  </div>
                </div>
              </section>
            </aside>
          </div>
        </div>
      </AdminLayout>
    )
  }

  return (
    <AdminLayout>
      <div className="dashboard-content">
        <header className="page-header">
          <div className="header-text">
            <p className="eyebrow">Exam Control</p>
            <h1>Exams & Assessments</h1>
            <p className="subtext">Manage your scheduled exams and create new assessment sessions.</p>
          </div>
          <div className="header-actions">
            <button className="primary-btn" onClick={() => setIsCreating(true)}>
              <span className="material-symbols-outlined">add</span>
              Create New Exam
            </button>
          </div>
        </header>

        <section className="glass-card table-container">
          <div className="table-header-row">
            <h3>Active & Scheduled Exams</h3>
            <div className="table-actions">
              <button className="secondary-btn">
                <span className="material-symbols-outlined">filter_list</span>
                Filter
              </button>
            </div>
          </div>
          <div className="table-wrapper">
            <table className="admin-table">
              <thead>
                <tr>
                  <th>Exam Title</th>
                  <th>Date & Time</th>
                  <th>Room / Lab</th>
                  <th>Proctor</th>
                  <th>Candidates</th>
                  <th>Status</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {[
                  { title: 'Cybersecurity Finals', date: '2024-05-15', time: '10:00 AM', room: 'Lab A', proctor: 'Dr. Sarah', students: 42, status: 'Scheduled' },
                  { title: 'Data Structures Quiz', date: '2024-05-16', time: '02:00 PM', room: 'Lab 2', proctor: 'Marcus T.', students: 28, status: 'Draft' },
                  { title: 'Advanced Algorithms', date: '2024-05-14', time: '09:00 AM', room: 'Main Hall', proctor: 'Elena R.', students: 120, status: 'Live' },
                ].map((e, i) => (
                  <tr key={i} className="table-row">
                    <td>
                      <span className="font-semibold">{e.title}</span>
                    </td>
                    <td>
                      <div className="student-details">
                        <span className="student-name" style={{ fontSize: '0.85rem' }}>{e.date}</span>
                        <span className="student-email">{e.time}</span>
                      </div>
                    </td>
                    <td>{e.room}</td>
                    <td>{e.proctor}</td>
                    <td>{e.students}</td>
                    <td>
                      <span className={`status-badge ${e.status === 'Live' ? 'active' : ''}`}>
                        <span className="dot"></span>
                        {e.status}
                      </span>
                    </td>
                    <td className="action-cell">
                      <button className="icon-action-btn">
                        <span className="material-symbols-outlined">edit</span>
                      </button>
                      <button className="icon-action-btn danger">
                        <span className="material-symbols-outlined">delete</span>
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      </div>
    </AdminLayout>
  )
}
