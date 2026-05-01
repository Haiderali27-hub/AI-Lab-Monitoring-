import { HubConnectionBuilder } from '@microsoft/signalr'
import { useEffect, useMemo, useState } from 'react'
import { apiClient } from '../api/client'
import { getLiveRoster } from '../api/exams'
import { AdminLayout } from '../components/AdminLayout'
import { formatExamSessionStatus } from '../roleUtils'
import { useAuth } from '../store/AuthContext'
import type { LiveRosterItem } from '../types'

type HeartbeatEvent = {
  studentId: string
  examSessionId: string | null
  isForegroundExamApp: boolean
  activeWindowTitle: string | null
  atUtc: string
}

type MonitoringEvent = {
  studentId: string
  examSessionId: string | null
  eventType: string
  payloadJson: string
  atUtc: string
}

export default function Monitoring() {
  const { user, accessToken } = useAuth()
  const [roster, setRoster] = useState<LiveRosterItem[]>([])
  const [events, setEvents] = useState<Array<HeartbeatEvent | MonitoringEvent>>([])
  const [status, setStatus] = useState('Connecting to monitoring feed...')

  const onlineCount = useMemo(() => roster.filter((item) => item.isOnline).length, [roster])

  useEffect(() => {
    let isMounted = true

    async function loadRoster() {
      try {
        const result = await getLiveRoster()
        if (isMounted) {
          setRoster(result)
        }
      } catch {
        if (isMounted) {
          setStatus('Unable to refresh live roster.')
        }
      }
    }

    void loadRoster()
    const intervalId = window.setInterval(loadRoster, 15000)

    return () => {
      isMounted = false
      if (intervalId) {
        window.clearInterval(intervalId)
      }
    }
  }, [])

  useEffect(() => {
    if (!user?.institutionId || !accessToken) {
      return
    }
    const institutionId = user.institutionId

    const baseUrl = apiClient.defaults.baseURL ?? ''
    const hubUrl = `${baseUrl.replace(/\/$/, '')}/hubs/monitoring`

    const connection = new HubConnectionBuilder()
      .withUrl(hubUrl, { accessTokenFactory: () => accessToken })
      .withAutomaticReconnect()
      .build()

    connection.on('monitoring_heartbeat', (payload: HeartbeatEvent) => {
      setEvents((prev) => [{ ...payload }, ...prev].slice(0, 50))
    })

    connection.on('monitoring_event', (payload: MonitoringEvent) => {
      setEvents((prev) => [{ ...payload }, ...prev].slice(0, 50))
    })

    async function startConnection() {
      try {
        await connection.start()
        await connection.invoke('JoinInstitution', institutionId)
        setStatus('Live monitoring feed connected.')
      } catch {
        setStatus('Monitoring feed connection failed.')
      }
    }

    void startConnection()

    return () => {
      void connection.stop()
    }
  }, [accessToken, user?.institutionId])

  return (
    <AdminLayout>
      <div className="dashboard-content">
        <header className="page-header">
          <div className="header-text">
            <p className="eyebrow">Live Proctoring</p>
            <h1>Monitoring Center</h1>
            <p className="subtext">Track active students, live status, and incoming alerts.</p>
          </div>
          <div className="header-actions">
            <span className="status-badge secure">Online {onlineCount}</span>
          </div>
        </header>

        <section className="glass-card roster-card">
          <div className="card-header">
            <h3>Live Exam Roster</h3>
            <span className="live-indicator">
              <span className="pulse"></span>
              LIVE
            </span>
          </div>
          <p className="subtext" style={{ marginBottom: '1rem' }}>{status}</p>
          <div className="roster-table-wrapper">
            <table className="admin-table roster-table">
              <thead>
                <tr>
                  <th>Student</th>
                  <th>Status</th>
                  <th>Time Remaining</th>
                  <th>Connection</th>
                </tr>
              </thead>
              <tbody>
                {roster.map((item) => (
                  <tr key={item.studentId} className="table-row">
                    <td>
                      <span className="font-semibold">{item.username}</span>
                    </td>
                    <td>
                      <span className={`status-badge ${item.status === 'InProgress' ? 'active' : ''}`}>
                        <span className="dot"></span>
                        {formatExamSessionStatus(item.status)}
                      </span>
                    </td>
                    <td className="font-mono">{item.remainingSeconds}s</td>
                    <td>
                      <span className={`connection-pill ${item.isOnline ? 'online' : 'offline'}`}>
                        {item.isOnline ? 'Stable' : 'Disconnected'}
                      </span>
                    </td>
                  </tr>
                ))}
                {roster.length === 0 && (
                  <tr>
                    <td colSpan={4} className="empty-state">No active exam sessions.</td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </section>

        <section className="glass-card" style={{ padding: '1.5rem' }}>
          <div className="card-header">
            <h3>Latest Monitoring Events</h3>
            <span className="status-badge secure">Last 50</span>
          </div>
          <div style={{ display: 'grid', gap: '0.75rem' }}>
            {events.map((event, index) => (
              <div key={`${event.studentId}-${index}`} className="student-item">
                <div className="student-item-left">
                  <div className="student-details">
                    <span className="student-name">Student {event.studentId.slice(0, 8)}</span>
                    <span className="student-email">{event.atUtc}</span>
                  </div>
                </div>
                <span className="status-pill unbound">
                  {'eventType' in event ? event.eventType : 'Heartbeat'}
                </span>
              </div>
            ))}
            {events.length === 0 && (
              <div className="empty-state">No monitoring events received yet.</div>
            )}
          </div>
        </section>
      </div>
    </AdminLayout>
  )
}
