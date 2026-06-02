import { useEffect, useMemo, useState } from 'react'
import { getExamReportDetail, getExamReports } from '../api/reports'
import { AdminLayout } from '../components/AdminLayout'
import type { ExamReportDetail, ExamReportSummary } from '../types'

export default function Reports() {
  const [reports, setReports] = useState<ExamReportSummary[]>([])
  const [selected, setSelected] = useState<ExamReportDetail | null>(null)
  const [error, setError] = useState<string | null>(null)

  const summary = useMemo(() => {
    return reports.reduce(
      (acc, report) => {
        acc.total += report.candidateCount
        acc.attendance += report.attendanceCount
        acc.identity += report.identityVerifiedCount
        acc.incidents += report.incidentCount
        return acc
      },
      { total: 0, attendance: 0, identity: 0, incidents: 0 },
    )
  }, [reports])

  useEffect(() => {
    async function load() {
      try {
        const data = await getExamReports()
        setReports(data)
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load reports.')
      }
    }

    void load()
  }, [])

  async function onSelect(examId: string) {
    try {
      const detail = await getExamReportDetail(examId)
      setSelected(detail)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load report detail.')
    }
  }

  return (
    <AdminLayout>
      <div className="dashboard-content">
        <header className="page-header">
          <div className="header-text">
            <p className="eyebrow">Reporting</p>
            <h1>Exam Reports</h1>
            <p className="subtext">Attendance, identity verification, and incident summaries.</p>
          </div>
        </header>

        {error && <div className="inline-alert error">{error}</div>}

        <section className="stats-grid">
          <div className="card stat-card">
            <div className="stat-info">
              <span className="stat-label">Total Candidates</span>
              <span className="stat-value">{summary.total}</span>
            </div>
          </div>
          <div className="card stat-card">
            <div className="stat-info">
              <span className="stat-label">Attendance Logged</span>
              <span className="stat-value">{summary.attendance}</span>
            </div>
          </div>
          <div className="card stat-card">
            <div className="stat-info">
              <span className="stat-label">Identity Verified</span>
              <span className="stat-value">{summary.identity}</span>
            </div>
          </div>
          <div className="card stat-card">
            <div className="stat-info">
              <span className="stat-label">Incidents</span>
              <span className="stat-value">{summary.incidents}</span>
            </div>
          </div>
        </section>

        <section className="glass-card table-container">
          <div className="table-header-row">
            <h3>Exam Summary</h3>
          </div>
          <div className="table-wrapper">
            <table className="admin-table">
              <thead>
                <tr>
                  <th>Exam</th>
                  <th>Status</th>
                  <th>Attendance</th>
                  <th>Identity</th>
                  <th>Incidents</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {reports.map((report) => (
                  <tr key={report.examId} className="table-row">
                    <td>{report.examName}</td>
                    <td>{report.status}</td>
                    <td>{report.attendanceCount}/{report.candidateCount}</td>
                    <td>{report.identityVerifiedCount}/{report.candidateCount}</td>
                    <td>{report.incidentCount}</td>
                    <td className="action-cell">
                      <button className="secondary-btn" onClick={() => void onSelect(report.examId)}>
                        View Detail
                      </button>
                    </td>
                  </tr>
                ))}
                {reports.length === 0 && (
                  <tr>
                    <td colSpan={6} className="empty-state">No reports available.</td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </section>

        {selected && (
          <section className="glass-card table-container">
            <div className="table-header-row">
              <h3>{selected.examName} Detail</h3>
            </div>
            <div className="table-wrapper">
              <table className="admin-table">
                <thead>
                  <tr>
                    <th>Student</th>
                    <th>Email</th>
                    <th>Attendance</th>
                    <th>Identity</th>
                    <th>Last Heartbeat</th>
                  </tr>
                </thead>
                <tbody>
                  {selected.students.map((student) => (
                    <tr key={student.studentId} className="table-row">
                      <td>{student.username}</td>
                      <td>{student.email}</td>
                      <td>{student.attendanceAtUtc ? 'Yes' : 'No'}</td>
                      <td>{student.identityVerifiedAtUtc ? 'Yes' : 'No'}</td>
                      <td>{student.lastHeartbeatUtc ?? 'N/A'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        )}
      </div>
    </AdminLayout>
  )
}
