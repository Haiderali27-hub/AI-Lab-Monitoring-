import { useState, useEffect } from 'react';
import AppLayout from '../../components/layout/AppLayout';
import { labsApi } from '../../api/labs.api';
import type { LabInfo, LabWorkstation } from '../../types';

/**
 * Module 9 — Lab & Workstation inventory (backs the seating map and seat enforcement).
 * Admins register labs and their workstations (seat label, the PC's Windows name, IP);
 * teachers then assign these seats to students on the Eligibility page. Machines are no
 * longer auto-registered on student login (Labs:AutoRegister=false), so PCs students have
 * logged in from but that no workstation covers are listed here for an admin to register.
 */
export default function LabsPage() {
  const [labs, setLabs] = useState<LabInfo[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedLabId, setSelectedLabId] = useState('');

  // Add-lab form
  const [showLabForm, setShowLabForm] = useState(false);
  const [labName, setLabName] = useState('');
  const [labLocation, setLabLocation] = useState('');

  // Add-workstation form
  const [wsNumber, setWsNumber] = useState('');
  const [wsHost, setWsHost] = useState('');
  const [wsIp, setWsIp] = useState('');
  const [saving, setSaving] = useState(false);

  // Inline edit of one workstation
  const [editing, setEditing] = useState<{ id: string; number: string; host: string; ip: string } | null>(null);
  const [unregistered, setUnregistered] = useState<{ machineName: string; lastStudent: string; lastSeenAt: string }[]>([]);

  const loadLabs = () => {
    setLoading(true);
    labsApi.getAll()
      .then((data) => {
        setLabs(data);
        setSelectedLabId((prev) => prev || (data.length > 0 ? data[0].labId : ''));
      })
      .catch((err) => console.error('Failed to load labs:', err))
      .finally(() => setLoading(false));
    labsApi.unregisteredMachines()
      .then(setUnregistered)
      .catch(() => setUnregistered([]));
  };

  useEffect(() => { loadLabs(); }, []);

  const selectedLab = labs.find((l) => l.labId === selectedLabId);

  const handleCreateLab = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!labName.trim()) return;
    setSaving(true);
    try {
      await labsApi.createLab(labName.trim(), labLocation.trim());
      setLabName('');
      setLabLocation('');
      setShowLabForm(false);
      loadLabs();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to create lab.');
    } finally {
      setSaving(false);
    }
  };

  const handleCreateWorkstation = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedLabId || !wsNumber.trim()) return;
    setSaving(true);
    try {
      await labsApi.createWorkstation(selectedLabId, wsNumber.trim(), wsIp.trim(), wsHost.trim());
      setWsNumber('');
      setWsHost('');
      setWsIp('');
      loadLabs();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to add workstation.');
    } finally {
      setSaving(false);
    }
  };

  const startEdit = (ws: LabWorkstation) =>
    setEditing({ id: ws.workstationId, number: ws.machineNumber, host: ws.hostName || '', ip: ws.ipAddress || '' });

  const handleSaveEdit = async () => {
    if (!editing || !selectedLabId || !editing.number.trim()) return;
    setSaving(true);
    try {
      await labsApi.updateWorkstation(selectedLabId, editing.id, editing.number.trim(), editing.ip.trim(), editing.host.trim());
      setEditing(null);
      loadLabs();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to update workstation.');
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async (ws: LabWorkstation) => {
    if (!window.confirm(`Delete workstation ${ws.machineNumber}?`)) return;
    try {
      await labsApi.deleteWorkstation(selectedLabId, ws.workstationId);
      loadLabs();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to delete workstation.');
    }
  };

  const inputCls = 'h-8 px-2 border border-slate-200 rounded-md focus:ring-2 focus:ring-primary-500 outline-none text-xs font-mono bg-white w-full';

  return (
    <AppLayout title="Labs & Workstations">
      <div className="space-y-6">
        {/* Header row */}
        <div className="flex flex-col md:flex-row md:items-end justify-between gap-4">
          <div className="space-y-2">
            <label className="text-xs font-bold tracking-wider text-slate-400 uppercase block">Select Lab</label>
            <select
              value={selectedLabId}
              onChange={(e) => setSelectedLabId(e.target.value)}
              className="w-full md:w-[320px] h-10 border border-slate-200 bg-white rounded-lg px-4 focus:ring-2 focus:ring-primary-500 outline-none text-sm cursor-pointer shadow-sm"
            >
              {labs.length === 0 && <option value="">No labs registered</option>}
              {labs.map((lab) => (
                <option key={lab.labId} value={lab.labId}>
                  {lab.name} {lab.location ? `— ${lab.location}` : ''}
                </option>
              ))}
            </select>
          </div>
          <button
            onClick={() => setShowLabForm((v) => !v)}
            className="px-4 h-10 bg-primary-500 text-white font-semibold text-sm rounded-lg hover:bg-primary-600 transition-colors shadow-sm flex items-center gap-2"
          >
            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 4v16m8-8H4" />
            </svg>
            New Lab
          </button>
        </div>

        {/* Add lab form */}
        {showLabForm && (
          <form onSubmit={handleCreateLab} className="bg-white border border-slate-200 rounded-xl p-5 shadow-sm flex flex-col md:flex-row gap-3 md:items-end">
            <div className="flex-1 space-y-1.5">
              <label className="text-xs font-semibold tracking-wider text-slate-400 uppercase">Lab Name</label>
              <input
                value={labName}
                onChange={(e) => setLabName(e.target.value)}
                placeholder="e.g. Lab B"
                required
                className="w-full h-10 px-4 border border-slate-200 rounded-lg focus:ring-2 focus:ring-primary-500 outline-none text-sm"
              />
            </div>
            <div className="flex-1 space-y-1.5">
              <label className="text-xs font-semibold tracking-wider text-slate-400 uppercase">Location</label>
              <input
                value={labLocation}
                onChange={(e) => setLabLocation(e.target.value)}
                placeholder="e.g. Block 3, Ground Floor"
                className="w-full h-10 px-4 border border-slate-200 rounded-lg focus:ring-2 focus:ring-primary-500 outline-none text-sm"
              />
            </div>
            <button
              type="submit"
              disabled={saving}
              className="px-6 h-10 bg-primary-500 text-white font-semibold text-sm rounded-lg hover:bg-primary-600 disabled:opacity-50"
            >
              Create Lab
            </button>
          </form>
        )}

        {loading ? (
          <div className="min-h-[300px] flex items-center justify-center">
            <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin" />
          </div>
        ) : !selectedLab ? (
          <div className="text-center p-12 bg-white rounded-xl border border-dashed border-slate-200 text-sm text-slate-400">
            No labs registered yet. Create a lab to start building the seating map.
          </div>
        ) : (
          <div className="bg-white border border-slate-200 rounded-xl overflow-hidden shadow-sm">
            <div className="px-6 py-4 border-b border-slate-200 flex items-center justify-between">
              <div>
                <h4 className="text-base font-bold text-slate-800">{selectedLab.name}</h4>
                <p className="text-xs text-slate-400">{selectedLab.location || 'No location set'} · {selectedLab.workstations.length} workstation(s)</p>
              </div>
            </div>

            {/* Add workstation row */}
            <form onSubmit={handleCreateWorkstation} className="px-6 py-4 border-b border-slate-100 bg-slate-50/60 flex flex-col md:flex-row gap-3 md:items-end">
              <div className="space-y-1.5">
                <label className="text-[10px] font-bold tracking-wider text-slate-400 uppercase">Machine Number</label>
                <input
                  value={wsNumber}
                  onChange={(e) => setWsNumber(e.target.value)}
                  placeholder="e.g. PC-07"
                  required
                  className="h-9 px-3 border border-slate-200 rounded-lg focus:ring-2 focus:ring-primary-500 outline-none text-xs font-mono bg-white"
                />
              </div>
              <div className="space-y-1.5">
                <label htmlFor="ws-host" className="text-[10px] font-bold tracking-wider text-slate-400 uppercase">PC Name (Windows)</label>
                <input
                  id="ws-host"
                  value={wsHost}
                  onChange={(e) => setWsHost(e.target.value)}
                  placeholder="e.g. LAB-PC-07 (optional)"
                  className="h-9 px-3 border border-slate-200 rounded-lg focus:ring-2 focus:ring-primary-500 outline-none text-xs font-mono bg-white"
                />
              </div>
              <div className="space-y-1.5">
                <label className="text-[10px] font-bold tracking-wider text-slate-400 uppercase">IP Address</label>
                <input
                  value={wsIp}
                  onChange={(e) => setWsIp(e.target.value)}
                  placeholder="e.g. 192.168.1.7"
                  className="h-9 px-3 border border-slate-200 rounded-lg focus:ring-2 focus:ring-primary-500 outline-none text-xs font-mono bg-white"
                />
              </div>
              <button
                type="submit"
                disabled={saving}
                className="px-4 h-9 bg-blue-50 border border-blue-200 text-primary-600 font-bold text-xs rounded-lg hover:bg-blue-100 disabled:opacity-50"
              >
                + Add Workstation
              </button>
            </form>

            {/* PCs students used that no workstation covers (seat enforcement matches PC name, else machine number) */}
            {unregistered.length > 0 && (
              <div data-testid="unregistered-machines" className="px-6 py-3 border-b border-slate-100 bg-amber-50/50">
                <p className="text-[10px] font-bold tracking-wider text-amber-700 uppercase mb-2">Unregistered PCs seen at login</p>
                <div className="flex flex-wrap gap-2">
                  {unregistered.map((m) => (
                    <button
                      key={m.machineName}
                      type="button"
                      onClick={() => { setWsHost(m.machineName); setWsNumber((prev) => prev || m.machineName); }}
                      title={`Last used by ${m.lastStudent} — click to fill the form`}
                      className="px-2.5 py-1 rounded-full border border-amber-200 bg-white text-[11px] font-mono text-amber-800 hover:bg-amber-100"
                    >
                      {m.machineName}
                    </button>
                  ))}
                </div>
              </div>
            )}

            {/* Workstation grid */}
            {selectedLab.workstations.length === 0 ? (
              <div className="p-10 text-center text-sm text-slate-400 italic">
                No workstations in this lab yet.
              </div>
            ) : (
              <div className="p-6 grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 xl:grid-cols-4 gap-3 stagger">
                {selectedLab.workstations.map((ws) => {
                  const occupied = Boolean(ws.occupantName);
                  if (editing?.id === ws.workstationId) {
                    return (
                      <div key={ws.workstationId} className="border border-primary-300 rounded-xl p-3.5 bg-white space-y-2">
                        <input aria-label="Machine number" value={editing.number} onChange={(e) => setEditing({ ...editing, number: e.target.value })} className={inputCls} />
                        <input aria-label="PC name" placeholder="PC name (optional)" value={editing.host} onChange={(e) => setEditing({ ...editing, host: e.target.value })} className={inputCls} />
                        <input aria-label="IP address" placeholder="IP address" value={editing.ip} onChange={(e) => setEditing({ ...editing, ip: e.target.value })} className={inputCls} />
                        <div className="flex justify-end gap-2 pt-1">
                          <button type="button" onClick={() => setEditing(null)} className="px-3 h-7 text-xs text-slate-500 hover:text-slate-800">Cancel</button>
                          <button type="button" onClick={handleSaveEdit} disabled={saving} className="px-3 h-7 text-xs font-bold bg-primary-500 text-white rounded-md disabled:opacity-50">Save</button>
                        </div>
                      </div>
                    );
                  }
                  return (
                    <div
                      key={ws.workstationId}
                      className={`border rounded-xl p-3.5 hover:shadow-md transition-all duration-200 ${
                        occupied ? 'border-green-200 bg-green-50/40' : 'border-slate-200 bg-slate-50/50'
                      }`}
                    >
                      <div className="flex items-center justify-between mb-2">
                        <div className="flex items-center gap-2">
                          <svg className={`w-4 h-4 ${occupied ? 'text-green-600' : 'text-slate-400'}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
                          </svg>
                          <span className="font-bold text-slate-700 text-sm font-mono">{ws.machineNumber}</span>
                        </div>
                        {occupied ? (
                          <span className="flex items-center gap-1 text-[9px] font-bold uppercase text-green-700">
                            <span className="w-1.5 h-1.5 rounded-full bg-green-500 animate-pulse"></span>In use
                          </span>
                        ) : (
                          <span className="text-[9px] font-bold uppercase text-slate-400">Free</span>
                        )}
                      </div>
                      <p className="text-[11px] text-slate-400 font-mono">{ws.ipAddress || 'No IP set'}</p>
                      <p className="text-[11px] text-slate-400 font-mono mb-1.5" title="Windows computer name matched by seat enforcement">
                        {ws.hostName ? `PC ${ws.hostName}` : 'PC name = machine number'}
                      </p>
                      {occupied ? (
                        <div className="pt-1.5 border-t border-green-100">
                          <p className="text-xs font-semibold text-slate-800 truncate">{ws.occupantName}</p>
                          <p className="text-[10px] text-slate-500 truncate">
                            Windows: <span className="font-mono">{ws.occupantWindowsUser || '—'}</span>
                          </p>
                          {ws.occupantLastSeen && (
                            <p className="text-[9px] text-slate-400 mt-0.5">
                              Seen {new Date(ws.occupantLastSeen).toLocaleString('en-US', { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })}
                            </p>
                          )}
                        </div>
                      ) : (
                        <p className="text-[10px] text-slate-300 italic pt-1.5 border-t border-slate-100">No student logged in</p>
                      )}
                      <div className="flex justify-end gap-2 mt-2">
                        <button type="button" onClick={() => startEdit(ws)} className="text-[10px] font-bold text-primary-600 hover:underline">Edit</button>
                        <button type="button" onClick={() => handleDelete(ws)} className="text-[10px] font-bold text-red-500 hover:underline">Delete</button>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </div>
        )}
      </div>
    </AppLayout>
  );
}
