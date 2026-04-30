import { useState, useEffect } from 'react'
import { AdminLayout } from '../components/AdminLayout'

export default function OrganizationSettings() {
  const [loading, setLoading] = useState(false)
  
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
            <button className="primary-btn">
              Save Global Configuration
            </button>
          </div>
        </header>

        <div className="settings-grid" style={{ display: 'grid', gridTemplateColumns: '1fr', gap: '2rem', maxWidth: '1000px' }}>
          {/* Section 1: Institution Profile */}
          <section className="glass-card form-section">
            <div className="section-title-row">
              <span className="material-symbols-outlined">domain</span>
              <h3>Institution Profile</h3>
            </div>
            <div className="form-grid-2">
              <div className="input-group">
                <label className="input-label">Institution Name</label>
                <input className="input-control" defaultValue="ExamGuard Global Academy" />
              </div>
              <div className="input-group">
                <label className="input-label">Contact Email</label>
                <input className="input-control" type="email" defaultValue="admin@examguard-academy.edu" />
              </div>
              <div className="input-group col-span-2" style={{ gridColumn: 'span 2' }}>
                <label className="input-label">Institution Logo URL</label>
                <input className="input-control" placeholder="https://..." />
              </div>
            </div>
          </section>

          {/* Section 2: Lab Locations */}
          <section className="glass-card form-section">
            <div className="section-title-row" style={{ justifyContent: 'space-between' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                <span className="material-symbols-outlined">location_on</span>
                <h3>Lab Locations</h3>
              </div>
              <button className="secondary-btn" style={{ padding: '0.5rem 1rem', fontSize: '0.8rem' }}>
                + Add Lab
              </button>
            </div>
            <div className="lab-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))', gap: '1rem', marginTop: '1rem' }}>
              {[
                { name: 'Main Computer Lab A', terminals: 42, status: 'Active' },
                { name: 'Science Wing B', terminals: 28, status: 'Active' },
              ].map((lab, i) => (
                <div key={i} className="glass-card" style={{ padding: '1rem', borderStyle: 'solid', background: 'rgba(255,255,255,0.4)' }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '0.5rem' }}>
                    <h4 style={{ margin: 0 }}>{lab.name}</h4>
                    <span className="status-pill active">{lab.status}</span>
                  </div>
                  <p style={{ fontSize: '0.8rem', color: 'var(--on-surface-variant)', display: 'flex', alignItems: 'center', gap: '4px' }}>
                    <span className="material-symbols-outlined" style={{ fontSize: '1rem' }}>sensors</span>
                    {lab.terminals} Registered Terminals
                  </p>
                  <div style={{ marginTop: '1rem', display: 'flex', gap: '1rem' }}>
                    <button className="text-btn primary" style={{ fontSize: '0.8rem' }}>Edit</button>
                    <button className="text-btn secondary" style={{ fontSize: '0.8rem' }}>Config</button>
                  </div>
                </div>
              ))}
              <div className="glass-card" style={{ padding: '1rem', borderStyle: 'dashed', background: 'transparent', display: 'flex', alignItems: 'center', justifyContent: 'center', minHeight: '120px', opacity: 0.6 }}>
                <p style={{ fontStyle: 'italic', fontSize: '0.8rem' }}>Assign new physical location...</p>
              </div>
            </div>
          </section>

          {/* Section 3: Security & Compliance */}
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
              <textarea className="input-control" style={{ height: '80px', fontFamily: 'monospace' }} placeholder="192.168.1.0/24, 10.0.5.0/24"></textarea>
            </div>
            
            <div className="form-grid-2" style={{ marginTop: '1.5rem' }}>
              <div className="security-toggle-card" style={{ padding: '1rem', background: 'rgba(255,255,255,0.5)', borderRadius: '12px', border: '1px solid rgba(255,255,255,0.3)' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <div>
                    <h4 style={{ margin: 0, fontSize: '0.9rem' }}>Enforce Single Device Binding</h4>
                    <p style={{ margin: 0, fontSize: '0.7rem', color: 'var(--on-surface-variant)' }}>Prevents account sharing during sessions</p>
                  </div>
                  <input type="checkbox" defaultChecked />
                </div>
              </div>
              <div className="security-toggle-card" style={{ padding: '1rem', background: 'rgba(255,255,255,0.5)', borderRadius: '12px', border: '1px solid rgba(255,255,255,0.3)' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <div>
                    <h4 style={{ margin: 0, fontSize: '0.9rem' }}>Allow Teachers to Reset Binding</h4>
                    <p style={{ margin: 0, fontSize: '0.7rem', color: 'var(--on-surface-variant)' }}>Grant localized reset permissions</p>
                  </div>
                  <input type="checkbox" />
                </div>
              </div>
              <div className="input-group" style={{ gridColumn: 'span 2' }}>
                <label className="input-label">Session Timeout</label>
                <select className="input-control">
                  <option>Auto-logout after 15 minutes of inactivity</option>
                  <option selected>Auto-logout after 30 minutes of inactivity</option>
                  <option>Auto-logout after 60 minutes of inactivity</option>
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
