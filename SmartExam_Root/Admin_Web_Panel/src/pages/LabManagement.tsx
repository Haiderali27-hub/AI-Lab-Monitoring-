import { useEffect, useMemo, useState } from 'react'
import {
  createLab,
  createWorkstation,
  deleteLab,
  deleteWorkstation,
  getLabs,
  getWorkstations,
  updateWorkstation,
} from '../api/institution'
import { AdminLayout } from '../components/AdminLayout'
import { SkeletonStatCard } from '../components/Skeleton'
import { Toaster, useToast } from '../components/Toaster'
import { normalizeRole } from '../roleUtils'
import { useAuth } from '../store/AuthContext'
import type { Lab, Workstation } from '../types'

export default function LabManagement() {
  const { user } = useAuth()
  const canManage = normalizeRole(user?.role) === 'OrganizationAdmin'

  const { toasts, push: toast, dismiss } = useToast()
  const [loading, setLoading] = useState(true)
  const [labs, setLabs] = useState<Lab[]>([])
  const [selectedLabId, setSelectedLabId] = useState('')
  const [workstations, setWorkstations] = useState<Workstation[]>([])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const [labName, setLabName] = useState('')
  const [labTerminals, setLabTerminals] = useState(30)
  const [machineName, setMachineName] = useState('')
  const [machineIp, setMachineIp] = useState('')

  const selectedLab = useMemo(() => labs.find((lab) => lab.id === selectedLabId) ?? null, [labs, selectedLabId])
  const activeMachines = useMemo(() => workstations.filter((machine) => machine.isActive).length, [workstations])

  async function loadLabs() {
    const data = await getLabs()
    setLabs(data)
    if (!selectedLabId && data.length > 0) {
      setSelectedLabId(data[0].id)
    }
  }

  async function loadWorkstations(labId: string) {
    if (!labId) {
      setWorkstations([])
      return
    }
    setWorkstations(await getWorkstations(labId))
  }

  useEffect(() => {
    setLoading(true)
    loadLabs()
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load labs.'))
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    void loadWorkstations(selectedLabId).catch((err) => setError(err instanceof Error ? err.message : 'Failed to load machines.'))
  }, [selectedLabId])

  async function onCreateLab(event: React.FormEvent) {
    event.preventDefault()
    if (!canManage || labName.trim().length < 2) return

    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      const lab = await createLab({ name: labName.trim(), registeredTerminals: labTerminals })
      setLabName('')
      setLabTerminals(30)
      await loadLabs()
      setSelectedLabId(lab.id)
      toast('Lab registered.')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create lab.')
    } finally {
      setBusy(false)
    }
  }

  async function onDeleteLab(labId: string) {
    if (!canManage || !window.confirm('Delete this lab and all of its registered machines?')) return
    setBusy(true)
    setError(null)
    try {
      await deleteLab(labId)
      setSelectedLabId('')
      setWorkstations([])
      await loadLabs()
      toast('Lab deleted.', 'warning')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete lab.')
    } finally {
      setBusy(false)
    }
  }

  async function onCreateWorkstation(event: React.FormEvent) {
    event.preventDefault()
    if (!canManage || !selectedLabId || !machineName.trim()) return

    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      await createWorkstation(selectedLabId, {
        name: machineName.trim(),
        ipAddress: machineIp.trim() || null,
      })
      setMachineName('')
      setMachineIp('')
      await loadWorkstations(selectedLabId)
      toast('Machine registered.')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create machine.')
    } finally {
      setBusy(false)
    }
  }

  async function onToggleWorkstation(machine: Workstation) {
    if (!canManage) return
    setBusy(true)
    setError(null)
    try {
      await updateWorkstation(machine.labId, machine.id, {
        name: machine.name,
        ipAddress: machine.ipAddress,
        isActive: !machine.isActive,
      })
      await loadWorkstations(machine.labId)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update machine.')
    } finally {
      setBusy(false)
    }
  }

  async function onDeleteWorkstation(machine: Workstation) {
    if (!canManage || !window.confirm(`Delete ${machine.name}?`)) return
    setBusy(true)
    setError(null)
    try {
      await deleteWorkstation(machine.labId, machine.id)
      await loadWorkstations(machine.labId)
      toast('Machine deleted.', 'warning')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete machine.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <AdminLayout>
      <div className="dashboard-content">
        <div className="page-header">
          <div className="header-text">
            <p className="eyebrow">Laboratory Allocation</p>
            <h1>Labs & Workstations</h1>
            <p className="subtext">Register labs, add machines, and keep workstation capacity ready for exam allocation.</p>
          </div>
          <span className="status-badge secure">{labs.length} labs</span>
        </div>

        {error && <div className="inline-alert error">{error}</div>}

        <div className="stats-grid">
          {loading ? (
            <>
              <SkeletonStatCard />
              <SkeletonStatCard />
              <SkeletonStatCard />
            </>
          ) : (
            <>
            <div className="card stat-card">
              <div className="stat-icon blue"><span className="material-symbols-outlined">domain</span></div>
              <div><span className="stat-value">{labs.length}</span><span className="stat-label">Registered Labs</span></div>
            </div>
            <div className="card stat-card">
              <div className="stat-icon green"><span className="material-symbols-outlined">desktop_windows</span></div>
              <div><span className="stat-value">{workstations.length}</span><span className="stat-label">Machines in Selected Lab</span></div>
            </div>
            <div className="card stat-card">
              <div className="stat-icon purple"><span className="material-symbols-outlined">verified</span></div>
              <div><span className="stat-value">{activeMachines}</span><span className="stat-label">Active Machines</span></div>
            </div>
          </>
          )}
        </div>

        <div className="exam-grid">
          <section className="glass-card form-section">
            <div className="section-title-row">
              <span className="material-symbols-outlined">domain_add</span>
              <h3>Lab Registry</h3>
            </div>
            {canManage && (
              <form onSubmit={onCreateLab} className="form-grid-2">
                <div className="input-group">
                  <label className="input-label">Lab Name</label>
                  <input className="input-control" value={labName} onChange={(e) => setLabName(e.target.value)} placeholder="Lab Alpha" />
                </div>
                <div className="input-group">
                  <label className="input-label">Terminal Capacity</label>
                  <input className="input-control" type="number" min={0} value={labTerminals} onChange={(e) => setLabTerminals(Number(e.target.value))} />
                </div>
                <button className="primary-btn" type="submit" disabled={busy} style={{ width: 'max-content' }}>
                  <span className="material-symbols-outlined">add</span>
                  Add Lab
                </button>
              </form>
            )}

            <div className="student-list">
              {labs.map((lab) => (
                <div className="student-item" key={lab.id}>
                  <button
                    type="button"
                    className="text-btn primary"
                    onClick={() => setSelectedLabId(lab.id)}
                    style={{ textAlign: 'left' }}
                  >
                    <span className="student-name">{lab.name}</span>
                    <span className="student-email">{lab.registeredTerminals} terminals planned</span>
                  </button>
                  <div>
                    {selectedLabId === lab.id && <span className="status-pill bound">Selected</span>}
                    {canManage && (
                      <button className="icon-action-btn danger" type="button" onClick={() => onDeleteLab(lab.id)} disabled={busy}>
                        <span className="material-symbols-outlined">delete</span>
                      </button>
                    )}
                  </div>
                </div>
              ))}
              {labs.length === 0 && <p className="empty-state">No labs registered yet.</p>}
            </div>
          </section>

          <section className="glass-card form-section">
            <div className="section-title-row">
              <span className="material-symbols-outlined">computer</span>
              <h3>{selectedLab ? `${selectedLab.name} Machines` : 'Machines'}</h3>
            </div>

            {selectedLab && canManage && (
              <form onSubmit={onCreateWorkstation} className="form-grid-2">
                <div className="input-group">
                  <label className="input-label">Machine Name</label>
                  <input className="input-control" value={machineName} onChange={(e) => setMachineName(e.target.value)} placeholder="PC-01" />
                </div>
                <div className="input-group">
                  <label className="input-label">IP Address</label>
                  <input className="input-control" value={machineIp} onChange={(e) => setMachineIp(e.target.value)} placeholder="192.168.1.10" />
                </div>
                <button className="primary-btn" type="submit" disabled={busy} style={{ width: 'max-content' }}>
                  <span className="material-symbols-outlined">add</span>
                  Add Machine
                </button>
              </form>
            )}

            <div className="table-wrapper" style={{ marginTop: '1rem' }}>
              <table className="admin-table">
                <thead>
                  <tr>
                    <th>Machine</th>
                    <th>IP</th>
                    <th>Status</th>
                    {canManage && <th>Actions</th>}
                  </tr>
                </thead>
                <tbody>
                  {workstations.map((machine) => (
                    <tr key={machine.id}>
                      <td>{machine.name}</td>
                      <td>{machine.ipAddress ?? 'Not set'}</td>
                      <td><span className={`status-pill ${machine.isActive ? 'active' : 'unbound'}`}>{machine.isActive ? 'Active' : 'Inactive'}</span></td>
                      {canManage && (
                        <td>
                          <button className="icon-action-btn" type="button" onClick={() => onToggleWorkstation(machine)} disabled={busy}>
                            <span className="material-symbols-outlined">{machine.isActive ? 'pause' : 'play_arrow'}</span>
                          </button>
                          <button className="icon-action-btn danger" type="button" onClick={() => onDeleteWorkstation(machine)} disabled={busy}>
                            <span className="material-symbols-outlined">delete</span>
                          </button>
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
              {selectedLab && workstations.length === 0 && <p className="empty-state">No machines registered for this lab.</p>}
              {!selectedLab && <p className="empty-state">Select a lab to manage machines.</p>}
            </div>
          </section>
        </div>
      </div>
      <Toaster toasts={toasts} onDismiss={dismiss} />
    </AdminLayout>
  )
}
