import { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import * as signalR from '@microsoft/signalr';
import { useAuth } from '../../context/AuthContext';
import { examsApi } from '../../api/exams.api';
import { SIGNALR_HUB_URL } from '../../utils/constants';
import type { Exam, StudentLiveStatus } from '../../types';

interface TimelineEvent {
  title: string;
  timestamp: string;
  description?: string;
  isAlert?: boolean;
}

export default function LiveMonitorPage() {
  const navigate = useNavigate();
  const { token, logout, user } = useAuth();
  const [activeExam, setActiveExam] = useState<Exam | null>(null);
  const [loading, setLoading] = useState(true);
  const [studentStatuses, setStudentStatuses] = useState<Map<string, StudentLiveStatus>>(new Map());
  const [timelines, setTimelines] = useState<Map<string, TimelineEvent[]>>(new Map());
  const [selectedStudentId, setSelectedStudentId] = useState<string | null>(null);
  const [messageText, setMessageText] = useState('');
  const [remainingTime, setRemainingTime] = useState('00:00:00');

  const connectionRef = useRef<signalR.HubConnection | null>(null);

  // 1. Fetch active exam
  useEffect(() => {
    examsApi.getAll()
      .then(exams => {
        const active = exams.find(e => e.status === 'Active');
        if (active) {
          setActiveExam(active);
        }
      })
      .catch(err => console.error('Failed to get active exams', err))
      .finally(() => setLoading(false));
  }, []);

  // 2. Timer countdown logic
  useEffect(() => {
    if (!activeExam) return;

    const timer = setInterval(() => {
      const start = new Date(activeExam.startTime).getTime();
      const duration = activeExam.durationMinutes * 60 * 1000;
      const end = start + duration;
      const now = new Date().getTime();
      const diff = end - now;

      if (diff <= 0) {
        setRemainingTime('00:00:00');
        clearInterval(timer);
      } else {
        const hrs = Math.floor(diff / (3600 * 1000)).toString().padStart(2, '0');
        const mins = Math.floor((diff % (3600 * 1000)) / (60 * 1000)).toString().padStart(2, '0');
        const secs = Math.floor((diff % (60 * 1000)) / 1000).toString().padStart(2, '0');
        setRemainingTime(`${hrs}:${mins}:${secs}`);
      }
    }, 1000);

    return () => clearInterval(timer);
  }, [activeExam]);

  // 3. Connect to SignalR Hub
  useEffect(() => {
    if (!activeExam || !token) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(SIGNALR_HUB_URL, {
        accessTokenFactory: () => token
      })
      .withAutomaticReconnect()
      .build();

    connection.on('StudentHeartbeat', (data: any) => {
      // Map properties from SignalR event
      const updatedStatus: StudentLiveStatus = {
        userId: data.userId,
        studentName: data.studentName || 'Student',
        workstationNumber: data.workstationNumber || 'PC-XX',
        sessionId: data.sessionId,
        lastHeartbeat: new Date().toISOString(),
        activeWindow: data.activeWindow || 'Exam Browser',
        answeredCount: data.answeredCount ?? 0,
        totalQuestions: data.totalQuestions ?? activeExam.questionCount,
        violationCount: data.violationCount ?? 0,
        tileStatus: data.tileStatus || 'Normal'
      };

      setStudentStatuses(prev => {
        const next = new Map(prev);
        next.set(data.userId, updatedStatus);
        return next;
      });

      // Append active window switch event to timeline if it is not blank
      if (data.activeWindow && data.activeWindow !== 'Exam Browser') {
        const timeStr = new Date().toLocaleTimeString();
        setTimelines(prev => {
          const next = new Map(prev);
          const currentTimeline = next.get(data.userId) || [];
          // Avoid duplicate windows log consecutively
          if (currentTimeline[0]?.description !== `Window switched to '${data.activeWindow}'`) {
            next.set(data.userId, [
              {
                title: 'Window Focus Lost',
                timestamp: timeStr,
                description: `Window switched to '${data.activeWindow}'`,
                isAlert: true
              },
              ...currentTimeline
            ]);
          }
          return next;
        });
      }
    });

    connection.on('ViolationEvent', (data: any) => {
      const timeStr = new Date().toLocaleTimeString();
      
      setStudentStatuses(prev => {
        const next = new Map(prev);
        const existing = next.get(data.userId);
        if (existing) {
          next.set(data.userId, {
            ...existing,
            violationCount: existing.violationCount + 1,
            tileStatus: 'Violation'
          });
        }
        return next;
      });

      setTimelines(prev => {
        const next = new Map(prev);
        const currentTimeline = next.get(data.userId) || [];
        next.set(data.userId, [
          {
            title: data.eventType || 'Security Violation',
            timestamp: timeStr,
            description: data.payload || 'Unauthorized activity detected.',
            isAlert: true
          },
          ...currentTimeline
        ]);
        return next;
      });
    });

    connection.start()
      .then(() => {
        connectionRef.current = connection;
        console.log('SignalR connected successfully.');
      })
      .catch(err => console.error('SignalR Connection Error: ', err));

    return () => {
      connection.stop().then(() => console.log('SignalR stopped.'));
    };
  }, [activeExam, token]);

  const handleSendMessage = () => {
    if (!messageText.trim() || !selectedStudentId || !connectionRef.current) return;

    // Send Warning/Message via SignalR Hub if there's a connection
    connectionRef.current.invoke('SendWarning', selectedStudentId, messageText)
      .then(() => {
        const timeStr = new Date().toLocaleTimeString();
        setTimelines(prev => {
          const next = new Map(prev);
          const currentTimeline = next.get(selectedStudentId) || [];
          next.set(selectedStudentId, [
            {
              title: 'Warning Message Sent',
              timestamp: timeStr,
              description: `Teacher: "${messageText}"`
            },
            ...currentTimeline
          ]);
          return next;
        });
        setMessageText('');
      })
      .catch(err => {
        console.error('Failed to send SignalR warning', err);
        alert('Could not send warning message. SignalR disconnected.');
      });
  };

  const handleForceSubmitStudent = async (studentId: string) => {
    const student = studentStatuses.get(studentId);
    if (!student) return;

    if (confirm(`Are you sure you want to force-submit ${student.studentName}'s exam session immediately?`)) {
      try {
        if (connectionRef.current) {
          await connectionRef.current.invoke('ForceSubmitSession', student.userId);
        }
        
        const timeStr = new Date().toLocaleTimeString();
        setTimelines(prev => {
          const next = new Map(prev);
          const currentTimeline = next.get(studentId) || [];
          next.set(studentId, [
            {
              title: 'Force Submitted',
              timestamp: timeStr,
              description: 'Instructor terminated student session.'
            },
            ...currentTimeline
          ]);
          return next;
        });

        alert(`Session force-submitted for ${student.studentName}.`);
      } catch (err) {
        console.error('Failed to force submit student session', err);
        alert('Could not force submit. Connection error.');
      }
    }
  };

  const handleForceSubmitAll = async () => {
    if (!activeExam) return;
    if (confirm('Are you sure you want to end this exam and force-submit all student sessions? This cannot be undone.')) {
      try {
        await examsApi.forceSubmitAll(activeExam.examId);
        alert('All active sessions have been force-submitted.');
        navigate('/teacher/dashboard');
      } catch (err) {
        console.error('Failed to force submit all sessions', err);
        alert('An error occurred while ending the exam.');
      }
    }
  };

  const selectedStudent = selectedStudentId ? studentStatuses.get(selectedStudentId) : null;
  const selectedStudentTimeline = selectedStudentId ? (timelines.get(selectedStudentId) || []) : [];

  return (
    <div className="bg-[#F8FAFC] text-slate-800 font-sans min-h-screen flex overflow-hidden">
      {/* 64px Collapsed Sidebar */}
      <aside className="w-[64px] h-screen bg-white border-r border-slate-200 flex flex-col items-center py-6 shrink-0 z-50">
        <div className="mb-8 cursor-pointer" onClick={() => navigate('/teacher/dashboard')}>
          <span className="material-symbols-outlined text-primary-500 text-[32px] font-bold">shield</span>
        </div>
        <nav className="flex flex-col gap-4 flex-grow">
          <button 
            onClick={() => navigate('/teacher/dashboard')} 
            className="w-10 h-10 flex items-center justify-center rounded-lg text-slate-400 hover:text-slate-600 hover:bg-slate-50 transition-colors" 
            title="Dashboard"
          >
            <span className="material-symbols-outlined text-[22px]">grid_view</span>
          </button>
          <button 
            onClick={() => navigate('/teacher/dashboard')} 
            className="w-10 h-10 flex items-center justify-center rounded-lg text-slate-400 hover:text-slate-600 hover:bg-slate-50 transition-colors" 
            title="My Exams"
          >
            <span className="material-symbols-outlined text-[22px]">assignment</span>
          </button>
          <button 
            onClick={() => navigate('/teacher/create-exam')} 
            className="w-10 h-10 flex items-center justify-center rounded-lg text-slate-400 hover:text-slate-600 hover:bg-slate-50 transition-colors" 
            title="Create Exam"
          >
            <span className="material-symbols-outlined text-[22px]">add_circle</span>
          </button>
          <button 
            className="w-10 h-10 flex items-center justify-center rounded-lg text-primary-500 bg-blue-50 border border-blue-100 font-bold shadow-sm" 
            title="Live Monitor"
          >
            <span className="material-symbols-outlined text-[22px]" style={{ fontVariationSettings: "'FILL' 1" }}>sensors</span>
          </button>
          <button 
            onClick={() => navigate('/teacher/eligibility')} 
            className="w-10 h-10 flex items-center justify-center rounded-lg text-slate-400 hover:text-slate-600 hover:bg-slate-50 transition-colors" 
            title="Eligibility"
          >
            <span className="material-symbols-outlined text-[22px]">check_circle</span>
          </button>
        </nav>
        <div className="mt-auto flex flex-col gap-4">
          <div className="w-10 h-10 rounded-full bg-blue-50 text-primary-600 font-bold flex items-center justify-center text-xs" title={user?.name}>
            {user?.name ? user.name.split(' ').map(n => n[0]).join('') : 'TE'}
          </div>
          <button 
            onClick={logout} 
            className="w-10 h-10 flex items-center justify-center rounded-lg text-red-400 hover:bg-red-50 hover:text-red-500 transition-colors"
            title="Logout"
          >
            <span className="material-symbols-outlined text-[22px]">logout</span>
          </button>
        </div>
      </aside>

      {/* Main Content Area */}
      <div className="flex-grow flex flex-col h-screen overflow-hidden">
        {/* Top Navbar */}
        <header className="h-[56px] bg-white border-b border-slate-200 flex justify-between items-center px-6 shrink-0 z-40">
          <div className="flex items-center gap-2">
            <button 
              onClick={() => navigate('/teacher/dashboard')}
              className="text-slate-400 hover:text-slate-600 transition-colors mr-2 flex items-center"
            >
              <span className="material-symbols-outlined">arrow_back</span>
            </button>
            <span className="text-sm font-semibold text-slate-800">
              Live Monitor {activeExam ? `— ${activeExam.title}` : ''}
            </span>
          </div>
          <div className="flex items-center gap-4">
            <button className="p-2 text-slate-400 hover:text-slate-600 rounded-full hover:bg-slate-50 transition-colors relative">
              <span className="material-symbols-outlined text-[20px]">notifications</span>
              <span className="absolute top-2.5 right-2.5 w-1.5 h-1.5 bg-red-500 rounded-full"></span>
            </button>
            <button className="p-2 text-slate-400 hover:text-slate-600 rounded-full hover:bg-slate-50 transition-colors">
              <span className="material-symbols-outlined text-[20px]">help_outline</span>
            </button>
          </div>
        </header>

        {/* Sub-header Bar */}
        <div className="h-[72px] bg-white border-b border-slate-200 flex items-center justify-between px-8 shrink-0 z-30 shadow-sm">
          <div>
            <h2 className="text-[10px] font-bold text-slate-400 uppercase tracking-widest leading-none">CURRENT EXAM</h2>
            <div className="flex items-center gap-2 mt-1">
              <span className="font-bold text-slate-800 text-lg">
                {activeExam ? activeExam.title : 'No active exam running'}
              </span>
              {activeExam && (
                <span className="px-2 py-0.5 bg-blue-50 text-primary-600 border border-blue-100 text-[10px] font-bold rounded uppercase tracking-wider">
                  {activeExam.courseName}
                </span>
              )}
            </div>
          </div>

          {activeExam && (
            <>
              <div className="flex flex-col items-center">
                <span className="text-[10px] font-bold text-slate-400 uppercase tracking-widest leading-none">REMAINING TIME</span>
                <span className="font-mono text-2xl font-bold text-primary-500 tracking-wider mt-1">{remainingTime}</span>
              </div>

              <div className="flex items-center gap-3">
                <div className="flex items-center gap-2 px-3 py-1.5 bg-green-50 text-green-700 border border-green-200 rounded-full text-xs font-semibold">
                  <span className="w-2 h-2 bg-green-500 rounded-full animate-pulse"></span>
                  <span>{studentStatuses.size} Student(s) Active</span>
                </div>
                <button 
                  onClick={handleForceSubmitAll}
                  className="px-4 py-2 bg-red-600 text-white font-bold text-xs rounded-xl hover:bg-red-700 transition-all shadow-sm active:scale-95 flex items-center gap-1.5"
                >
                  <span className="material-symbols-outlined text-[16px]">power_settings_new</span>
                  Force End Exam
                </button>
              </div>
            </>
          )}
        </div>

        {/* Main Content Area */}
        <main className={`flex-grow p-8 overflow-y-auto ${selectedStudentId ? 'mr-[320px]' : ''} transition-all duration-300`}>
          {loading ? (
            <div className="h-full flex items-center justify-center flex-col">
              <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin mb-2" />
              <p className="text-sm text-slate-400">Loading live proctoring hub...</p>
            </div>
          ) : !activeExam ? (
            <div className="h-full flex items-center justify-center flex-col text-center p-12 bg-white rounded-2xl border border-slate-200 border-dashed">
              <span className="material-symbols-outlined text-[64px] text-slate-300 mb-3">sensors_off</span>
              <h3 className="text-lg font-bold text-slate-700">No Exam Currently Active</h3>
              <p className="text-sm text-slate-400 max-w-sm mt-1 mb-6">
                Live proctoring dashboard activates when a scheduled exam starts. Build an exam or start one to begin tracking workstation metrics.
              </p>
              <button 
                onClick={() => navigate('/teacher/create-exam')}
                className="px-6 py-2.5 bg-primary-500 text-white rounded-xl font-semibold text-xs shadow-md shadow-primary-500/20 hover:bg-primary-600 transition-all"
              >
                Create Scheduled Exam
              </button>
            </div>
          ) : studentStatuses.size === 0 ? (
            <div className="h-full flex items-center justify-center flex-col text-center p-12 bg-white rounded-2xl border border-slate-100">
              <span className="material-symbols-outlined text-[64px] text-primary-200 animate-pulse mb-3">sensors</span>
              <h3 className="text-lg font-bold text-slate-700">Waiting for Students to Join...</h3>
              <p className="text-sm text-slate-400 max-w-sm mt-1">
                The desktop examination app on student workstations will connect here upon logging in with their credentials.
              </p>
            </div>
          ) : (
            <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 xl:grid-cols-4 gap-4">
              {Array.from(studentStatuses.values()).map(s => {
                const isSelected = selectedStudentId === s.userId;
                let statusColor = 'border-green-500/30 ring-transparent hover:ring-green-500/10';
                let heartColor = 'text-green-500';
                let statusText = 'Stable';

                if (s.tileStatus === 'Warning') {
                  statusColor = 'border-orange-400 ring-orange-100';
                  heartColor = 'text-orange-400';
                  statusText = 'Suspicious';
                } else if (s.tileStatus === 'Violation' || s.violationCount > 0) {
                  statusColor = 'border-red-500 ring-red-100';
                  heartColor = 'text-red-500';
                  statusText = 'Critical';
                }

                return (
                  <div 
                    key={s.userId}
                    onClick={() => setSelectedStudentId(isSelected ? null : s.userId)}
                    className={`bg-white border-2 rounded-xl p-4 shadow-sm hover:shadow-md transition-all cursor-pointer relative overflow-hidden ring-4 ${statusColor} ${isSelected ? 'ring-primary-500/20 border-primary-500' : ''}`}
                  >
                    {s.violationCount > 0 && (
                      <div className="absolute top-0 right-0 px-2 py-0.5 bg-red-500 text-white text-[9px] font-bold rounded-bl-lg flex items-center gap-1">
                        <span className="material-symbols-outlined text-[10px]">block</span>
                        {s.violationCount} ALERTS
                      </div>
                    )}
                    
                    <div className="flex justify-between items-start mb-3">
                      <div>
                        <h3 className="font-bold text-slate-800 text-sm">{s.studentName}</h3>
                        <p className="text-[10px] text-slate-400 uppercase font-semibold tracking-wider">
                          PC-{s.workstationNumber} • {s.activeWindow}
                        </p>
                      </div>
                      <div className="flex flex-col items-end">
                        <span className={`material-symbols-outlined ${heartColor}`} style={{ fontVariationSettings: "'FILL' 1" }}>favorite</span>
                        <span className={`text-[8px] font-bold uppercase tracking-wider mt-0.5 ${heartColor}`}>{statusText}</span>
                      </div>
                    </div>

                    <div className="bg-slate-50 rounded-lg p-2.5 mb-3 text-xs space-y-1.5">
                      <div className="flex items-center gap-1.5 text-slate-600">
                        <span className="material-symbols-outlined text-[16px] text-slate-400">web</span>
                        <span className="truncate">{s.activeWindow}</span>
                      </div>
                      <div className="flex items-center justify-between text-slate-600">
                        <div className="flex items-center gap-1.5">
                          <span className="material-symbols-outlined text-[16px] text-slate-400">task_alt</span>
                          <span>Progress:</span>
                        </div>
                        <span className="font-bold text-slate-700">{s.answeredCount}/{s.totalQuestions}</span>
                      </div>
                    </div>

                    <div className="flex justify-between items-center">
                      <div className="h-1 flex-grow bg-slate-100 rounded-full overflow-hidden mr-3">
                        <div 
                          className={`h-full ${s.tileStatus === 'Violation' ? 'bg-red-500' : s.tileStatus === 'Warning' ? 'bg-orange-400' : 'bg-primary-500'}`}
                          style={{ width: `${s.totalQuestions > 0 ? (s.answeredCount / s.totalQuestions) * 100 : 0}%` }}
                        ></div>
                      </div>
                      <span className="text-[9px] font-bold text-slate-400 uppercase">
                        {s.totalQuestions > 0 ? Math.round((s.answeredCount / s.totalQuestions) * 100) : 0}%
                      </span>
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </main>
      </div>

      {/* Side Detail Drawer (Michael Chen details mockup transformed to dynamic state) */}
      {selectedStudent && (
        <aside className="fixed right-0 top-[56px] bottom-0 w-[320px] bg-white border-l border-slate-200 shadow-2xl flex flex-col z-50 animate-in slide-in-from-right duration-200">
          
          {/* Drawer Header */}
          <div className="p-6 border-b border-slate-100 shrink-0">
            <div className="flex justify-between items-start mb-4">
              {/* Profile Initials Placeholder */}
              <div className="w-12 h-12 rounded-2xl bg-blue-100 text-primary-600 font-bold flex items-center justify-center text-lg">
                {selectedStudent.studentName.split(' ').map(n => n[0]).join('')}
              </div>
              <button 
                onClick={() => setSelectedStudentId(null)}
                className="p-1 hover:bg-slate-100 rounded-lg transition-colors text-slate-400"
              >
                <span className="material-symbols-outlined">close</span>
              </button>
            </div>
            <h3 className="font-bold text-slate-800 text-lg leading-tight">{selectedStudent.studentName}</h3>
            <p className="text-xs text-slate-400">Workstation PC-{selectedStudent.workstationNumber}</p>
            
            <div className="flex items-center gap-2 mt-4">
              <span className="px-2 py-0.5 bg-slate-100 border border-slate-200 rounded text-[9px] font-bold text-slate-600 uppercase">
                PC-{selectedStudent.workstationNumber}
              </span>
              {selectedStudent.violationCount > 0 ? (
                <span className="px-2 py-0.5 bg-red-100 text-red-700 rounded text-[9px] font-bold uppercase">
                  {selectedStudent.violationCount} Alert(s) Active
                </span>
              ) : (
                <span className="px-2 py-0.5 bg-green-100 text-green-700 rounded text-[9px] font-bold uppercase">
                  Stable Connection
                </span>
              )}
            </div>
          </div>

          {/* Activity Timeline */}
          <div className="flex-grow overflow-y-auto p-6 scrollbar-thin">
            <h4 className="text-[10px] font-bold text-slate-400 uppercase tracking-widest mb-6">Activity Timeline</h4>
            
            {selectedStudentTimeline.length === 0 ? (
              <div className="text-center text-xs text-slate-400 italic py-12">
                No major telemetry alerts or focus switches recorded yet. Session is running normally.
              </div>
            ) : (
              <div className="relative border-l-2 border-slate-200 ml-2 space-y-6">
                {selectedStudentTimeline.map((item, idx) => (
                  <div key={idx} className="relative pl-6">
                    <span className={`absolute -left-[7px] top-1.5 w-3.5 h-3.5 rounded-full border-4 border-white shadow-sm ${item.isAlert ? 'bg-orange-400' : 'bg-green-500'}`}></span>
                    <div className="flex flex-col">
                      <span className="text-xs font-semibold text-slate-800">{item.title}</span>
                      <span className="text-[10px] text-slate-400">{item.timestamp}</span>
                      {item.description && (
                        <div className={`mt-1.5 text-[11px] p-2 rounded border ${item.isAlert ? 'bg-orange-50/50 border-orange-100 text-orange-800' : 'bg-slate-50 border-slate-100 text-slate-600'}`}>
                          {item.description}
                        </div>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Drawer Actions */}
          <div className="p-6 bg-slate-50 border-t border-slate-200 mt-auto shrink-0">
            <div className="relative mb-3 flex items-center gap-1.5">
              <input 
                type="text"
                value={messageText}
                onChange={e => setMessageText(e.target.value)}
                className="w-full px-3 py-2 border border-slate-200 rounded-xl text-xs focus:ring-2 focus:ring-primary-500/10 focus:border-primary-500 outline-none transition-all text-slate-800 bg-white"
                placeholder="Send warning message..."
                onKeyDown={e => { if (e.key === 'Enter') handleSendMessage(); }}
              />
              <button 
                onClick={handleSendMessage}
                className="p-2 bg-primary-500 text-white rounded-lg hover:bg-primary-600 transition-colors flex items-center justify-center shrink-0"
              >
                <span className="material-symbols-outlined text-[16px]">send</span>
              </button>
            </div>
            <button 
              onClick={() => handleForceSubmitStudent(selectedStudent.userId)}
              className="w-full py-2.5 bg-red-600 hover:bg-red-700 text-white text-xs font-bold rounded-xl shadow-sm hover:shadow transition-all active:scale-[0.98] flex items-center justify-center gap-1.5"
            >
              <span className="material-symbols-outlined text-[16px]">output</span>
              Force Submit Student
            </button>
          </div>

        </aside>
      )}

    </div>
  );
}
