import type { StudentBindingStatus } from '../types'

type UserTableProps = {
  students: StudentBindingStatus[]
  onResetBinding: (studentId: string) => Promise<void>
  onForceLogout: (studentId: string) => Promise<void>
}

export function UserTable({ students, onResetBinding, onForceLogout }: UserTableProps) {
  return (
    <div className="glass-card table-container">
      <div className="table-header-row">
        <h3>Registered Students</h3>
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
              <th className="w-12">
                <input type="checkbox" className="custom-checkbox" />
              </th>
              <th>Student Name</th>
              <th>Status</th>
              <th>Device Status</th>
              <th className="text-right">Actions</th>
            </tr>
          </thead>
          <tbody>
            {students.map((student) => (
              <tr key={student.studentId} className="table-row">
                <td>
                  <input type="checkbox" className="custom-checkbox" />
                </td>
                <td>
                  <div className="student-info">
                    <div className="student-avatar">
                      {student.username.charAt(0).toUpperCase()}
                    </div>
                    <div className="student-details">
                      <span className="student-name">{student.username}</span>
                      <span className="student-email">{student.email}</span>
                    </div>
                  </div>
                </td>
                <td>
                  <span className="status-badge active">
                    <span className="dot"></span>
                    Verified
                  </span>
                </td>
                <td>
                  <span className={`status-pill ${student.hasBinding ? 'bound' : 'unbound'}`}>
                    <span className="pill-dot"></span>
                    {student.hasBinding ? 'Bound' : 'Unbound'}
                  </span>
                </td>
                <td>
                  <div className="action-cell">
                    <button
                      className="icon-action-btn"
                      title="Reset Device Binding"
                      onClick={() => onResetBinding(student.studentId)}
                      disabled={!student.hasBinding}
                    >
                      <span className="material-symbols-outlined">phonelink_erase</span>
                    </button>
                    <button
                      className="icon-action-btn danger"
                      title="Force Logout"
                      onClick={() => onForceLogout(student.studentId)}
                    >
                      <span className="material-symbols-outlined">logout</span>
                    </button>
                  </div>
                </td>
              </tr>
            ))}
            {students.length === 0 && (
              <tr>
                <td colSpan={5} className="empty-state">
                  No students registered yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
      <div className="table-footer">
        <span>Showing {students.length} entries</span>
        <div className="pagination">
          <button className="page-btn" disabled>
            <span className="material-symbols-outlined">chevron_left</span>
          </button>
          <button className="page-btn active">1</button>
          <button className="page-btn">
            <span className="material-symbols-outlined">chevron_right</span>
          </button>
        </div>
      </div>
    </div>
  )
}