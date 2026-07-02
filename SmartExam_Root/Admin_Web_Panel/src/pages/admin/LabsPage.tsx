import { useState, useEffect } from 'react';
import AppLayout from '../../components/layout/AppLayout';
import { labsApi } from '../../api/labs.api';
import type { LabInfo } from '../../types';

/**
 * Module 9 — Lab & Workstation inventory (backs the seating map).
 * Admins register labs and their workstations (machine number + IP);
 * teachers then assign these seats to students on the Eligibility page.
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
  const [wsIp, setWsIp] = useState('');
  const [saving, setSaving] = useState(false);

  const loadLabs = () => {
    setLoading(true);
    labsApi.getAll()
      .then((data) => {
        setLabs(data);
        setSelectedLabId((prev) => prev || (data.length > 0 ? data[0].labId : ''));
      })
      .catch((err) => console.error('Failed to load labs:', err))
      .finally(() => setLoading(false));
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
      await labsApi.createWorkstation(selectedLabId, wsNumber.trim(), wsIp.trim());
      setWsNumber('');
      setWsIp('');
      loadLabs();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to add workstation.');
    } finally {
      setSaving(false);
    }
  };

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

            {/* Workstation grid */}
            {selectedLab.workstations.length === 0 ? (
              <div className="p-10 text-center text-sm text-slate-400 italic">
                No workstations in this lab yet.
              </div>
            ) : (
              <div className="p-6 grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 xl:grid-cols-4 gap-3 stagger">
                {selectedLab.workstations.map((ws) => {
                  const occupied = Boolean(ws.occupantName);
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
                      <p className="text-[11px] text-slate-400 font-mono mb-1.5">{ws.ipAddress || 'No IP set'}</p>
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
