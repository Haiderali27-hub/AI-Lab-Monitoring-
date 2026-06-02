import { useState } from 'react';
import AppLayout from '../../components/layout/AppLayout';

interface Workstation {
  id: string;
  machineNumber: string;
  status: 'Online' | 'Offline' | 'InExam' | 'Alerting';
  ipAddress: string;
  macAddress: string;
  currentUser?: string;
  cpuUsage: number;
  ramUsage: number;
}

interface Lab {
  id: string;
  name: string;
  roomNumber: string;
  capacity: number;
  workstations: Workstation[];
}

export default function LabsPage() {
  const [labs] = useState<Lab[]>([
    {
      id: '1',
      name: 'Network Security Lab (Lab-A)',
      roomNumber: 'Block-C, Room 302',
      capacity: 30,
      workstations: [
        { id: '101', machineNumber: 'WS-01', status: 'InExam', ipAddress: '192.168.10.1', macAddress: '00:1A:2B:3C:4D:01', currentUser: 'Alex Sterling', cpuUsage: 14, ramUsage: 45 },
        { id: '102', machineNumber: 'WS-02', status: 'InExam', ipAddress: '192.168.10.2', macAddress: '00:1A:2B:3C:4D:02', currentUser: 'Elena Vance', cpuUsage: 22, ramUsage: 50 },
        { id: '103', machineNumber: 'WS-03', status: 'Online', ipAddress: '192.168.10.3', macAddress: '00:1A:2B:3C:4D:03', cpuUsage: 2, ramUsage: 18 },
        { id: '104', machineNumber: 'WS-04', status: 'Alerting', ipAddress: '192.168.10.4', macAddress: '00:1A:2B:3C:4D:04', currentUser: 'Michael Chen', cpuUsage: 89, ramUsage: 92 },
        { id: '105', machineNumber: 'WS-05', status: 'InExam', ipAddress: '192.168.10.5', macAddress: '00:1A:2B:3C:4D:05', currentUser: 'Jordan Smith', cpuUsage: 18, ramUsage: 42 },
        { id: '106', machineNumber: 'WS-06', status: 'Offline', ipAddress: '192.168.10.6', macAddress: '00:1A:2B:3C:4D:06', cpuUsage: 0, ramUsage: 0 },
        { id: '107', machineNumber: 'WS-07', status: 'InExam', ipAddress: '192.168.10.7', macAddress: '00:1A:2B:3C:4D:07', currentUser: 'David Miller', cpuUsage: 35, ramUsage: 61 },
        { id: '108', machineNumber: 'WS-08', status: 'Online', ipAddress: '192.168.10.8', macAddress: '00:1A:2B:3C:4D:08', cpuUsage: 5, ramUsage: 22 },
        { id: '109', machineNumber: 'WS-09', status: 'Offline', ipAddress: '192.168.10.9', macAddress: '00:1A:2B:3C:4D:09', cpuUsage: 0, ramUsage: 0 },
        { id: '110', machineNumber: 'WS-10', status: 'InExam', ipAddress: '192.168.10.10', macAddress: '00:1A:2B:3C:4D:10', currentUser: 'Iris West', cpuUsage: 12, ramUsage: 38 },
      ]
    },
    {
      id: '2',
      name: 'Systems Engineering Lab (Lab-B)',
      roomNumber: 'Block-A, Room 105',
      capacity: 25,
      workstations: [
        { id: '201', machineNumber: 'WS-21', status: 'Online', ipAddress: '192.168.12.1', macAddress: '00:1A:2B:3C:4E:01', cpuUsage: 4, ramUsage: 25 },
        { id: '202', machineNumber: 'WS-22', status: 'Online', ipAddress: '192.168.12.2', macAddress: '00:1A:2B:3C:4E:02', cpuUsage: 8, ramUsage: 30 },
        { id: '203', machineNumber: 'WS-23', status: 'Offline', ipAddress: '192.168.12.3', macAddress: '00:1A:2B:3C:4E:03', cpuUsage: 0, ramUsage: 0 },
      ]
    }
  ]);

  const [selectedLabId, setSelectedLabId] = useState('1');
  const [selectedWorkstationId, setSelectedWorkstationId] = useState<string | null>(null);
  const [filter, setFilter] = useState<'All' | 'Online' | 'InExam' | 'Alerting' | 'Offline'>('All');

  const selectedLab = labs.find(l => l.id === selectedLabId) || labs[0];

  const filteredWorkstations = selectedLab.workstations.filter(w => {
    if (filter === 'All') return true;
    return w.status === filter;
  });

  const selectedWS = selectedLab.workstations.find(w => w.id === selectedWorkstationId);

  // Compute summary stats
  const totalWS = labs.reduce((sum, l) => sum + l.workstations.length, 0);
  const inExamWS = labs.reduce((sum, l) => sum + l.workstations.filter(w => w.status === 'InExam').length, 0);
  const alertingWS = labs.reduce((sum, l) => sum + l.workstations.filter(w => w.status === 'Alerting').length, 0);
  const onlineWS = labs.reduce((sum, l) => sum + l.workstations.filter(w => w.status === 'Online' || w.status === 'InExam' || w.status === 'Alerting').length, 0);

  return (
    <AppLayout title="Labs & Workstations">
      <div className="max-w-[1280px] mx-auto pb-12">
        
        {/* Lab Stats Overview */}
        <div className="grid grid-cols-1 md:grid-cols-4 gap-6 mb-8">
          <div className="bg-white p-6 rounded-xl border border-slate-200 shadow-sm flex items-center gap-4">
            <div className="w-12 h-12 bg-blue-50 text-primary-500 rounded-xl flex items-center justify-center shrink-0">
              <span className="material-symbols-outlined text-[28px]">computer</span>
            </div>
            <div>
              <p className="text-[10px] font-bold text-slate-400 uppercase tracking-widest leading-none mb-1.5">Total Stations</p>
              <p className="text-2xl font-bold text-slate-800">{totalWS}</p>
            </div>
          </div>
          <div className="bg-white p-6 rounded-xl border border-slate-200 shadow-sm flex items-center gap-4">
            <div className="w-12 h-12 bg-green-50 text-green-500 rounded-xl flex items-center justify-center shrink-0">
              <span className="material-symbols-outlined text-[28px]">sensors</span>
            </div>
            <div>
              <p className="text-[10px] font-bold text-slate-400 uppercase tracking-widest leading-none mb-1.5">Online Stations</p>
              <p className="text-2xl font-bold text-slate-800">{onlineWS}</p>
            </div>
          </div>
          <div className="bg-white p-6 rounded-xl border border-slate-200 shadow-sm flex items-center gap-4">
            <div className="w-12 h-12 bg-purple-50 text-purple-500 rounded-xl flex items-center justify-center shrink-0">
              <span className="material-symbols-outlined text-[28px]">assignment</span>
            </div>
            <div>
              <p className="text-[10px] font-bold text-slate-400 uppercase tracking-widest leading-none mb-1.5">Active in Exam</p>
              <p className="text-2xl font-bold text-slate-800">{inExamWS}</p>
            </div>
          </div>
          <div className={`p-6 rounded-xl border shadow-sm flex items-center gap-4 transition-colors ${alertingWS > 0 ? 'bg-red-50/50 border-red-200' : 'bg-white border-slate-200'}`}>
            <div className={`w-12 h-12 rounded-xl flex items-center justify-center shrink-0 ${alertingWS > 0 ? 'bg-red-100 text-red-600' : 'bg-slate-50 text-slate-400'}`}>
              <span className="material-symbols-outlined text-[28px]">warning</span>
            </div>
            <div>
              <p className={`text-[10px] font-bold uppercase tracking-widest leading-none mb-1.5 ${alertingWS > 0 ? 'text-red-500' : 'text-slate-400'}`}>Alerting Stations</p>
              <p className={`text-2xl font-bold ${alertingWS > 0 ? 'text-red-600' : 'text-slate-800'}`}>{alertingWS}</p>
            </div>
          </div>
        </div>

        {/* Lab Selection and Filtering Controls */}
        <div className="bg-white border border-slate-200 p-5 rounded-xl shadow-sm mb-6 flex flex-col md:flex-row md:items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <label className="text-xs font-bold text-slate-500 uppercase tracking-wider">Select Lab:</label>
            <div className="flex gap-2">
              {labs.map(lab => (
                <button
                  key={lab.id}
                  onClick={() => { setSelectedLabId(lab.id); setSelectedWorkstationId(null); }}
                  className={`px-4 py-2 text-xs font-semibold rounded-lg border transition-all ${
                    selectedLabId === lab.id
                      ? 'bg-primary-500 text-white border-primary-500'
                      : 'bg-white text-slate-600 border-slate-200 hover:bg-slate-50'
                  }`}
                >
                  {lab.name}
                </button>
              ))}
            </div>
          </div>

          <div className="flex items-center gap-2">
            <span className="text-xs font-bold text-slate-500 uppercase tracking-wider mr-2">Filter State:</span>
            {['All', 'Online', 'InExam', 'Alerting', 'Offline'].map(state => (
              <button
                key={state}
                onClick={() => setFilter(state as any)}
                className={`px-3 py-1.5 text-xs font-medium rounded-lg transition-colors ${
                  filter === state
                    ? 'bg-slate-100 text-slate-800 font-bold'
                    : 'text-slate-500 hover:text-slate-800'
                }`}
              >
                {state === 'InExam' ? 'In Exam' : state}
              </button>
            ))}
          </div>
        </div>

        {/* Layout Workspace Grid */}
        <div className="flex flex-col lg:flex-row gap-6 items-start">
          
          {/* Workstation Grid */}
          <div className="flex-grow w-full bg-white border border-slate-200 rounded-xl p-6 shadow-sm">
            <div className="border-b border-slate-100 pb-4 mb-6">
              <h4 className="font-bold text-slate-800 text-sm">Station Layout Diagram</h4>
              <p className="text-xs text-slate-400 mt-0.5">{selectedLab.roomNumber} • Capacity: {selectedLab.capacity} Workstations</p>
            </div>

            {filteredWorkstations.length === 0 ? (
              <div className="text-center py-12 text-slate-400 italic text-sm">
                No workstations match the selected filter.
              </div>
            ) : (
              <div className="grid grid-cols-2 sm:grid-cols-4 md:grid-cols-5 gap-4">
                {filteredWorkstations.map(ws => {
                  let statusBg = 'bg-slate-50 border-slate-200 hover:bg-slate-100/50';
                  let statusBadge = 'bg-slate-100 text-slate-600';
                  
                  if (ws.status === 'Online') {
                    statusBg = 'bg-green-50/20 border-green-200 hover:bg-green-50/50';
                    statusBadge = 'bg-green-100 text-green-700';
                  } else if (ws.status === 'InExam') {
                    statusBg = 'bg-blue-50/20 border-blue-200 hover:bg-blue-50/50';
                    statusBadge = 'bg-blue-100 text-primary-700';
                  } else if (ws.status === 'Alerting') {
                    statusBg = 'bg-red-50/20 border-red-200 hover:bg-red-50/50 animate-pulse';
                    statusBadge = 'bg-red-100 text-red-700';
                  }

                  const isSelected = selectedWorkstationId === ws.id;

                  return (
                    <div
                      key={ws.id}
                      onClick={() => setSelectedWorkstationId(isSelected ? null : ws.id)}
                      className={`border-2 rounded-xl p-4 text-center cursor-pointer transition-all ${statusBg} ${
                        isSelected ? 'border-primary-500 shadow-md ring-2 ring-primary-500/10' : ''
                      }`}
                    >
                      <span className="material-symbols-outlined text-[32px] text-slate-400 block mb-1">desktop_windows</span>
                      <span className="font-bold text-sm text-slate-800 block">{ws.machineNumber}</span>
                      <span className={`text-[9px] font-bold uppercase rounded px-1.5 py-0.5 mt-2 inline-block leading-none border border-slate-150 ${statusBadge}`}>
                        {ws.status === 'InExam' ? 'In Exam' : ws.status}
                      </span>
                    </div>
                  );
                })}
              </div>
            )}
          </div>

          {/* Workstation Details Inspector Sidebar */}
          {selectedWS ? (
            <aside className="w-full lg:w-[320px] bg-white border border-slate-200 rounded-xl p-6 shadow-sm space-y-6 shrink-0">
              <div className="flex justify-between items-start border-b border-slate-100 pb-4">
                <div>
                  <h4 className="font-bold text-slate-800 text-base">{selectedWS.machineNumber} Details</h4>
                  <p className="text-xs text-slate-400 mt-0.5">IP: {selectedWS.ipAddress}</p>
                </div>
                <button 
                  onClick={() => setSelectedWorkstationId(null)}
                  className="text-slate-400 hover:text-slate-600"
                >
                  <span className="material-symbols-outlined text-[20px]">close</span>
                </button>
              </div>

              <div className="space-y-4 text-xs">
                <div className="flex justify-between">
                  <span className="text-slate-400 font-medium">Physical Address:</span>
                  <span className="font-mono font-bold text-slate-700">{selectedWS.macAddress}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-slate-400 font-medium">Status:</span>
                  <span className={`px-2 py-0.5 rounded text-[10px] font-bold uppercase ${
                    selectedWS.status === 'Online' ? 'bg-green-100 text-green-700' :
                    selectedWS.status === 'InExam' ? 'bg-blue-100 text-primary-700' :
                    selectedWS.status === 'Alerting' ? 'bg-red-100 text-red-700' : 'bg-slate-100 text-slate-600'
                  }`}>{selectedWS.status}</span>
                </div>
                {selectedWS.currentUser && (
                  <div className="flex justify-between">
                    <span className="text-slate-400 font-medium">Logged In Student:</span>
                    <span className="font-bold text-slate-800">{selectedWS.currentUser}</span>
                  </div>
                )}
              </div>

              {selectedWS.status !== 'Offline' && (
                <div className="space-y-4 pt-4 border-t border-slate-100">
                  <span className="font-bold text-xs text-slate-500 uppercase tracking-wider block">Diagnostics</span>
                  
                  <div>
                    <div className="flex justify-between text-xs mb-1">
                      <span className="text-slate-500">CPU Usage</span>
                      <span className="font-bold text-slate-700">{selectedWS.cpuUsage}%</span>
                    </div>
                    <div className="w-full bg-slate-100 h-1.5 rounded-full overflow-hidden">
                      <div 
                        className={`h-full transition-all duration-300 ${selectedWS.cpuUsage > 80 ? 'bg-red-500' : 'bg-primary-500'}`}
                        style={{ width: `${selectedWS.cpuUsage}%` }}
                      ></div>
                    </div>
                  </div>

                  <div>
                    <div className="flex justify-between text-xs mb-1">
                      <span className="text-slate-500">RAM Usage</span>
                      <span className="font-bold text-slate-700">{selectedWS.ramUsage}%</span>
                    </div>
                    <div className="w-full bg-slate-100 h-1.5 rounded-full overflow-hidden">
                      <div 
                        className={`h-full transition-all duration-300 ${selectedWS.ramUsage > 85 ? 'bg-red-500' : 'bg-primary-500'}`}
                        style={{ width: `${selectedWS.ramUsage}%` }}
                      ></div>
                    </div>
                  </div>
                </div>
              )}

              {selectedWS.status === 'Alerting' && (
                <div className="p-3 bg-red-50 border border-red-150 rounded-lg text-xs text-red-800 space-y-1">
                  <span className="font-bold block">Incident Warning</span>
                  <span>CPU limit exceeded. High frequency tab switching detected by proctoring daemon.</span>
                </div>
              )}

            </aside>
          ) : (
            <div className="w-full lg:w-[320px] bg-slate-50 border border-slate-200 border-dashed rounded-xl p-8 text-center shrink-0 text-slate-400 text-xs italic">
              <span className="material-symbols-outlined text-[36px] block mb-2">touch_app</span>
              Click a workstation to inspect IP, diagnostic telemetry, and logged-in student details.
            </div>
          )}

        </div>

      </div>
    </AppLayout>
  );
}
