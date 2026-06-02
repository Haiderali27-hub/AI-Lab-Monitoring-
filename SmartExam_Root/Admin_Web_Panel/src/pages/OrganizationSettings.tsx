import { useEffect, useState } from 'react'
import { createLab, deleteLab, getInstitutionSettings, getLabs, updateInstitutionSettings } from '../api/institution'
import { AdminLayout } from '../components/AdminLayout'
import type { InstitutionSettings, Lab } from '../types'

const timeoutOptions = [15, 30, 60]

export default function OrganizationSettings() {
  const [settings, setSettings] = useState<InstitutionSettings | null>(null)
  const [labs, setLabs] = useState<Lab[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const [newLabName, setNewLabName] = useState('')
  const [newLabTerminals, setNewLabTerminals] = useState(0)

  useEffect(() => {
    async function load() {
      try {
        const [settingsResult, labsResult] = await Promise.all([
          getInstitutionSettings(),
          getLabs(),
        ])
        setSettings(settingsResult)
        setLabs(labsResult)
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load settings.')
      }
    }

    void load()
  }, [])

  async function onSave() {
    if (!settings) {
      return
    }

    setBusy(true)
    setError(null)
    setNotice(null)

    try {
      const updated = await updateInstitutionSettings({
        name: settings.name,
        contactEmail: settings.contactEmail,
        logoUrl: settings.logoUrl,
        allowedIpRanges: settings.allowedIpRanges,
        enforceSingleDeviceBinding: settings.enforceSingleDeviceBinding,
        allowTeacherResetBinding: settings.allowTeacherResetBinding,
        sessionTimeoutMinutes: settings.sessionTimeoutMinutes,
      })
      setSettings(updated)
      setNotice('Settings saved successfully.')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save settings.')
    } finally {
      setBusy(false)
    }
  }

  async function onAddLab() {
    if (!newLabName.trim()) {
      setError('Lab name is required.')
      return
    }

    setBusy(true)
    setError(null)

    try {
      const created = await createLab({ name: newLabName.trim(), registeredTerminals: newLabTerminals })
      setLabs((prev) => [...prev, created])
      setNewLabName('')
      setNewLabTerminals(0)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create lab.')
    } finally {
      setBusy(false)
    }
  }

  async function onDeleteLab(labId: string) {
    setBusy(true)
    setError(null)

    try {
      await deleteLab(labId)
      setLabs((prev) => prev.filter((lab) => lab.id !== labId))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete lab.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <AdminLayout>
      <div className="dashboard-content">
        <header className="page-header">
          <div className="header-text">
            <p className="eyebrow">Platform Administration</p>
            <h1>Organization Settings</h1>
            <p className="subtext">Manage your institution's global identity, infrastructure, and security posture.</p>
          </div>
          <div className="header-actions">
            <button className="primary-btn" onClick={() => void onSave()} disabled={busy || !settings}>
              {busy ? 'Saving...' : 'Save Global Configuration'}
            </button>
          </div>
        </header>

        {error && <div className="inline-alert error">{error}</div>}
        {notice && <div className="inline-alert">{notice}</div>}

        <div className="settings-grid" style={{ display: 'grid', gridTemplateColumns: '1fr', gap: '2rem', maxWidth: '1000px' }}>
          <section className="glass-card form-section">
            <div className="section-title-row">
              <span className="material-symbols-outlined">domain</span>
              <h3>Institution Profile</h3>
            </div>
            <div className="form-grid-2">
              <div className="input-group">
                <label className="input-label">Institution Name</label>
                <input
                  className="input-control"
                  value={settings?.name ?? ''}
                  onChange={(event) => settings && setSettings({ ...settings, name: event.target.value })}
                />
              </div>
              <div className="input-group">
                <label className="input-label">Contact Email</label>
                <input
                  className="input-control"
                  type="email"
                  value={settings?.contactEmail ?? ''}
                  onChange={(event) => settings && setSettings({ ...settings, contactEmail: event.target.value })}
                />
              </div>
              <div className="input-group col-span-2" style={{ gridColumn: 'span 2' }}>
                <label className="input-label">Institution Logo URL</label>
                <input
                  className="input-control"
                  placeholder="https://..."
                  value={settings?.logoUrl ?? ''}
                  onChange={(event) => settings && setSettings({ ...settings, logoUrl: event.target.value || null })}
                />
              </div>
            </div>
          </section>

          <section className="glass-card form-section">
            <div className="section-title-row" style={{ justifyContent: 'space-between' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                <span className="material-symbols-outlined">location_on</span>
                <h3>Lab Locations</h3>
              </div>
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 140px 140px', gap: '0.75rem', marginTop: '1rem' }}>
              <input
                className="input-control"
                placeholder="Lab name"
                value={newLabName}
                onChange={(event) => setNewLabName(event.target.value)}
              />
              <input
                className="input-control"
                type="number"
                min={0}
                placeholder="Terminals"
                value={newLabTerminals}
                onChange={(event) => setNewLabTerminals(Number(event.target.value))}
              />
              <button className="secondary-btn" onClick={() => void onAddLab()} disabled={busy}>
                Add Lab
              </button>
            </div>
            <div className="lab-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))', gap: '1rem', marginTop: '1rem' }}>
              {labs.map((lab) => (
                <div key={lab.id} className="glass-card" style={{ padding: '1rem', borderStyle: 'solid', background: 'rgba(255,255,255,0.4)' }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '0.5rem' }}>
                    <h4 style={{ margin: 0 }}>{lab.name}</h4>
                    <span className="status-pill active">{lab.isActive ? 'Active' : 'Inactive'}</span>
                  </div>
                  <p style={{ fontSize: '0.8rem', color: 'var(--on-surface-variant)', display: 'flex', alignItems: 'center', gap: '4px' }}>
                    <span className="material-symbols-outlined" style={{ fontSize: '1rem' }}>sensors</span>
                    {lab.registeredTerminals} Registered Terminals
                  </p>
                  <div style={{ marginTop: '1rem', display: 'flex', gap: '1rem' }}>
                    <button className="text-btn secondary" style={{ fontSize: '0.8rem' }} onClick={() => void onDeleteLab(lab.id)}>
                      Remove
                    </button>
                  </div>
                </div>
              ))}
              {labs.length === 0 && (
                <div className="glass-card" style={{ padding: '1rem', borderStyle: 'dashed', background: 'transparent', display: 'flex', alignItems: 'center', justifyContent: 'center', minHeight: '120px', opacity: 0.6 }}>
                  <p style={{ fontStyle: 'italic', fontSize: '0.8rem' }}>Assign new physical location...</p>
                </div>
              )}
            </div>
          </section>

          <section className="glass-card form-section" style={{ background: 'linear-gradient(135deg, rgba(255,255,255,0.8) 0%, rgba(218,226,255,0.4) 100%)' }}>
            <div className="section-title-row">
              <span className="material-symbols-outlined">verified_user</span>
              <h3>Security & Compliance</h3>
            </div>
            <div className="input-group">
              <label className="input-label" style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                Allowed IP Ranges
                <span className="material-symbols-outlined" style={{ fontSize: '14px', cursor: 'help' }} title="Restricts admin and proctor access to specific network infrastructures.">info</span>
              </label>
              <textarea
                className="input-control"
                style={{ height: '80px', fontFamily: 'monospace' }}
                placeholder="192.168.1.0/24, 10.0.5.0/24"
                value={settings?.allowedIpRanges ?? ''}
                onChange={(event) => settings && setSettings({ ...settings, allowedIpRanges: event.target.value || null })}
              />
            </div>

            <div className="form-grid-2" style={{ marginTop: '1.5rem' }}>
              <div className="security-toggle-card" style={{ padding: '1rem', background: 'rgba(255,255,255,0.5)', borderRadius: '12px', border: '1px solid rgba(255,255,255,0.3)' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <div>
                    <h4 style={{ margin: 0, fontSize: '0.9rem' }}>Enforce Single Device Binding</h4>
                    <p style={{ margin: 0, fontSize: '0.7rem', color: 'var(--on-surface-variant)' }}>Prevents account sharing during sessions</p>
                  </div>
                  <input
                    type="checkbox"
                    checked={settings?.enforceSingleDeviceBinding ?? false}
                    onChange={(event) => settings && setSettings({ ...settings, enforceSingleDeviceBinding: event.target.checked })}
                  />
                </div>
              </div>
              <div className="security-toggle-card" style={{ padding: '1rem', background: 'rgba(255,255,255,0.5)', borderRadius: '12px', border: '1px solid rgba(255,255,255,0.3)' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <div>
                    <h4 style={{ margin: 0, fontSize: '0.9rem' }}>Allow Teachers to Reset Binding</h4>
                    <p style={{ margin: 0, fontSize: '0.7rem', color: 'var(--on-surface-variant)' }}>Grant localized reset permissions</p>
                  </div>
                  <input
                    type="checkbox"
                    checked={settings?.allowTeacherResetBinding ?? false}
                    onChange={(event) => settings && setSettings({ ...settings, allowTeacherResetBinding: event.target.checked })}
                  />
                </div>
              </div>
              <div className="input-group" style={{ gridColumn: 'span 2' }}>
                <label className="input-label">Session Timeout</label>
                <select
                  className="input-control"
                  value={settings?.sessionTimeoutMinutes ?? 30}
                  onChange={(event) => settings && setSettings({ ...settings, sessionTimeoutMinutes: Number(event.target.value) })}
                >
                  {timeoutOptions.map((minutes) => (
                    <option key={minutes} value={minutes}>Auto-logout after {minutes} minutes of inactivity</option>
                  ))}
                </select>
                <p style={{ fontSize: '0.7rem', color: 'var(--tertiary)', marginTop: '4px', display: 'flex', alignItems: 'center', gap: '4px' }}>
                  <span className="material-symbols-outlined" style={{ fontSize: '12px' }}>warning</span>
                  Shorter timeouts increase security audit scores.
                </p>
              </div>
            </div>
          </section>
        </div>
      </div>
    </AdminLayout>
  )
}
