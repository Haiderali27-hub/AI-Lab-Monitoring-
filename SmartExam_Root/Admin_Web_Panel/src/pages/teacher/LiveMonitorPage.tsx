import { useState, useEffect, useRef, useCallback } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import * as signalR from '@microsoft/signalr';
import { useAuth } from '../../context/AuthContext';
import { examsApi } from '../../api/exams.api';
import { monitoringApi } from '../../api/monitoring.api';
import { getFreshAccessToken } from '../../api/client';
import { SIGNALR_HUB_URL } from '../../utils/constants';
import type { CameraStatus, Exam, Incident, LiveSnapshotStudent, ReadinessResult, StudentLiveStatus, TileStatus } from '../../types';
import { severityOf, violationLabel, worseStatus } from '../../utils/violations';
import { homePathFor } from '../../utils/roles';
import StudentDrawer from './live/StudentDrawer';
import { READINESS_LABEL, readinessState } from './live/timeline';

// Module 6: a session that is no longer in progress (graded, receipt fixed, answers locked).
function isFinalised(status?: string | null): boolean {
  return !!status && status !== 'InProgress';
}

// Module 5: latest screen capture per student, shown on the tile (object URL of an authorised blob fetch).
interface Thumbnail {
  url: string;
  screenshotId: string;
  capturedAt: string;
  trigger: string;
}

function formatIdle(seconds?: number | null): string | null {
  if (seconds == null || seconds < 60) return null;
  const minutes = Math.floor(seconds / 60);
  return minutes >= 60 ? `Idle ${Math.floor(minutes / 60)}h ${minutes % 60}m` : `Idle ${minutes}m`;
}

const CAMERA_LABEL: Partial<Record<CameraStatus, string>> = {
  Covered: 'Camera covered',
  Frozen: 'Camera frozen',
  Unavailable: 'No camera',
};

function sessionStatusLabel(status?: string | null): string {
  switch (status) {
    case 'Submitted': return 'Submitted';
    case 'ForceSubmitted': return 'Force submitted';
    case 'TimedOut': return 'Timed out';
    default: return 'In progress';
  }
}

function isUnlocked(until?: string | null): boolean {
  return !!until && new Date(until).getTime() > Date.now();
}

// Module 10: a student from GET /exams/{id}/live, as a tile (the board a page starts from after a reload).
function fromSnapshot(s: LiveSnapshotStudent): StudentLiveStatus {
  return {
    userId: s.userId,
    studentName: s.studentName,
    workstationNumber: s.workstationNumber,
    machineName: s.machineName ?? null,
    windowsUsername: s.windowsUsername ?? null,
    sessionId: s.sessionId,
    lastHeartbeat: s.lastHeartbeatAt ?? new Date().toISOString(),
    activeWindow: s.activeWindow || 'Unknown',
    answeredCount: s.answeredCount,
    totalQuestions: s.totalQuestions,
    violationCount: s.violationCount,
    tileStatus: s.tileStatus,
    lastViolation: s.lastViolationType ? violationLabel(s.lastViolationType, s.lastViolationPayload) : null,
    sessionStatus: s.sessionStatus,
    extraMinutes: s.extraMinutes,
    isDisconnected: s.isDisconnected,
    disconnectedAt: s.disconnectedAt ?? null,
    idleSeconds: s.idleSeconds ?? null,
    cameraStatus: s.cameraStatus ?? null,
    processes: s.processes ?? null,
    unlockedUntil: s.unlockedUntilUtc ?? null,
  };
}

/**
 * Live proctoring dashboard. Shared by teachers (/teacher/live-monitor) and invigilators
 * (/invigilator/live-monitor, Module 7). The exam list comes from GET /api/exams, which the server
 * scopes: an invigilator only ever receives the exams they are assigned to proctor, and the hub
 * refuses to add them to any other exam's group. Invigilators supervise (watch, warn, mark incidents,
 * unlock a PC, force-submit one student) but cannot extend or end the exam, edit exams or open teacher pages.
 * Module 10: the board starts from the server's snapshot (GET /exams/{id}/live) so a reload keeps every tile,
 * and the student drawer reads the server timeline, captures, apps, readiness and incidents.
 */
export default function LiveMonitorPage() {
  const navigate = useNavigate();
  const { token, logout, user } = useAuth();
  const isInvigilator = user?.role === 'Invigilator';
  const homePath = user ? homePathFor(user.role) : '/login';
  const [searchParams, setSearchParams] = useSearchParams();
  const [runningExams, setRunningExams] = useState<Exam[]>([]);
  const [activeExam, setActiveExam] = useState<Exam | null>(null);
  const [loading, setLoading] = useState(true);
  const [studentStatuses, setStudentStatuses] = useState<Map<string, StudentLiveStatus>>(new Map());
  const [thumbnails, setThumbnails] = useState<Map<string, Thumbnail>>(new Map());
  const thumbnailUrls = useRef<Set<string>>(new Set());
  // Module 10: incidents of the exam, latest readiness result per student, and a per-student counter bumped on every
  // live event about them (the open drawer re-reads its server data when it changes).
  const [incidents, setIncidents] = useState<Incident[]>([]);
  const [readiness, setReadiness] = useState<Map<string, ReadinessResult>>(new Map());
  const [activity, setActivity] = useState<Map<string, number>>(new Map());
  const [selectedStudentId, setSelectedStudentId] = useState<string | null>(null);
  const [remainingTime, setRemainingTime] = useState('00:00:00');

  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const activeExamRef = useRef<string | undefined>(undefined);

  // 1. Fetch running exams (server-scoped per role) and pick ?examId= or the first one
  useEffect(() => {
    examsApi.getAll()
      .then(exams => {
        // Exams the caller may supervise first (a teacher lists every exam but the hub only admits
        // them to their own sections' and assigned ones), so the default is one that shows live data.
        const running = exams
          .filter(e => e.status === 'Active')
          .sort((a, b) => Number(b.canSupervise !== false) - Number(a.canSupervise !== false));
        setRunningExams(running);
        const requested = searchParams.get('examId');
        const initial = running.find(e => e.examId === requested) ?? running[0];
        if (initial) {
          setActiveExam(initial);
        }
      })
      .catch(err => console.error('Failed to get active exams', err))
      .finally(() => setLoading(false));
    // Loaded once; switching exams below updates ?examId= without refetching.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const bump = useCallback((userId?: string) => {
    if (!userId) return;
    setActivity(prev => new Map(prev).set(userId, (prev.get(userId) ?? 0) + 1));
  }, []);

  // Shows a capture on the student's tile (replacing and releasing the previous one).
  const showThumbnail = useCallback((examId: string, userId: string, shot: { screenshotId: string; capturedAt: string; trigger: string }) => {
    monitoringApi.screenshotBlob(shot.screenshotId)
      .then(blob => {
        if (activeExamRef.current !== examId) return;   // the proctor switched exams meanwhile
        const url = URL.createObjectURL(blob);
        thumbnailUrls.current.add(url);
        setThumbnails(prev => {
          const old = prev.get(userId);
          // An older capture that arrives late never replaces a newer one.
          if (old && new Date(old.capturedAt).getTime() > new Date(shot.capturedAt).getTime()) {
            URL.revokeObjectURL(url);
            thumbnailUrls.current.delete(url);
            return prev;
          }
          if (old) {
            URL.revokeObjectURL(old.url);
            thumbnailUrls.current.delete(old.url);
          }
          return new Map(prev).set(userId, { url, ...shot });
        });
      })
      .catch(err => console.error('Failed to load screen capture', err));
  }, []);

  const loadReadiness = useCallback((examId: string) => {
    examsApi.getReadiness(examId)
      .then(list => {
        if (activeExamRef.current !== examId) return;
        setReadiness(new Map(list.map(r => [r.userId, r])));
      })
      .catch(() => { /* 403 when not supervising: the join banner already says so */ });
  }, []);

  // A different exam starts from an empty board (tiles belong to one exam's group), then loads the server's
  // snapshot: every student who has heartbeated, incidents and readiness. Live events win over the snapshot.
  const activeExamId = activeExam?.examId;
  const [joinError, setJoinError] = useState<string | null>(null);
  useEffect(() => {
    activeExamRef.current = activeExamId;
    setStudentStatuses(new Map());
    setThumbnails(prev => {
      prev.forEach(t => { URL.revokeObjectURL(t.url); thumbnailUrls.current.delete(t.url); });
      return new Map();
    });
    setIncidents([]);
    setReadiness(new Map());
    setActivity(new Map());
    setSelectedStudentId(null);
    setJoinError(null);
    if (!activeExamId) return;

    monitoringApi.live(activeExamId)
      .then(snapshot => {
        if (activeExamRef.current !== activeExamId) return;
        setStudentStatuses(prev => {
          const next = new Map(prev);
          for (const s of snapshot.students) {
            if (!next.has(s.userId)) next.set(s.userId, fromSnapshot(s));
          }
          return next;
        });
        for (const s of snapshot.students) {
          if (s.latestScreenshot) showThumbnail(activeExamId, s.userId, s.latestScreenshot);
        }
      })
      .catch(() => { /* 403 when not supervising: the join banner already says so */ });
    monitoringApi.incidents(activeExamId)
      .then(list => {
        if (activeExamRef.current !== activeExamId) return;
        setIncidents(prev => mergeIncidents(list, prev));
      })
      .catch(() => { /* as above */ });
    loadReadiness(activeExamId);
  }, [activeExamId, showThumbnail, loadReadiness]);

  // Object URLs hold the image in memory until revoked: release them all when the page goes away.
  useEffect(() => {
    const urls = thumbnailUrls.current;
    return () => {
      urls.forEach(url => URL.revokeObjectURL(url));
      urls.clear();
    };
  }, []);

  const handleSelectExam = (examId: string) => {
    const next = runningExams.find(e => e.examId === examId);
    if (!next || next.examId === activeExam?.examId) return;
    setActiveExam(next);
    setSearchParams({ examId }, { replace: true });
  };

  const upsertIncident = useCallback((incident: Incident) => {
    if (incident.examId !== activeExamRef.current) return;
    setIncidents(prev => mergeIncidents([incident], prev));
    bump(incident.userId);
  }, [bump]);

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

    let disposed = false; // a stopped connection's cancelled join is not a refusal
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(SIGNALR_HUB_URL, {
        // Read at (re)connect time and refreshed when near expiry: access tokens live 30 minutes.
        accessTokenFactory: () => getFreshAccessToken()
      })
      .withAutomaticReconnect()
      .build();

    const forThisExam = (data: any) => !data?.examId || data.examId === activeExam.examId;
    const patchStudent = (userId: string, patch: Partial<StudentLiveStatus>) =>
      setStudentStatuses(prev => {
        const existing = prev.get(userId);
        if (!existing) return prev;
        return new Map(prev).set(userId, { ...existing, ...patch });
      });

    connection.on('StudentHeartbeat', (data: any) => {
      // Map properties from SignalR event
      const updatedStatus: StudentLiveStatus = {
        userId: data.userId,
        studentName: data.studentName || 'Student',
        workstationNumber: data.workstationNumber || 'Unassigned',
        machineName: data.machineName || null,
        windowsUsername: data.windowsUsername || null,
        sessionId: data.sessionId,
        lastHeartbeat: new Date().toISOString(),
        activeWindow: data.activeWindow || 'Exam Browser',
        answeredCount: data.answeredCount ?? 0,
        totalQuestions: data.totalQuestions ?? activeExam.questionCount,
        violationCount: data.violationCount ?? 0,
        tileStatus: (data.tileStatus || 'Normal') as TileStatus,
        seatStatus: data.seatStatus ?? null,
        seatDetail: data.seatDetail ?? null,
        sessionStatus: data.sessionStatus ?? null,
        // Module 5: a heartbeat means the PC is connected again; idle time, webcam state and visible apps
        isDisconnected: false,
        disconnectedAt: null,
        idleSeconds: data.idleSeconds ?? null,
        cameraStatus: data.cameraStatus ?? null,
        processes: data.processes ?? null
      };

      setStudentStatuses(prev => {
        const next = new Map(prev);
        const previous = prev.get(data.userId);
        // Keep the last violation label across heartbeats (heartbeats don't carry it) — except a
        // wrong seat (Module 9), which the server re-checks on every heartbeat and reports itself.
        const lastViolation = data.seatStatus === 'Mismatch'
          ? `Seat mismatch: ${data.seatDetail || 'not at the assigned workstation'}`
          : previous?.lastViolation ?? null;
        next.set(data.userId, {
          ...updatedStatus,
          lastViolation,
          sessionStatus: updatedStatus.sessionStatus ?? previous?.sessionStatus ?? null,
          // The server reports both on every beat; older servers did not, so keep what we knew.
          extraMinutes: data.extraMinutes ?? previous?.extraMinutes ?? null,
          unlockedUntil: data.unlockedUntilUtc !== undefined ? data.unlockedUntilUtc : previous?.unlockedUntil ?? null
        });
        return next;
      });
    });

    connection.on('ViolationEvent', (data: any) => {
      const label = violationLabel(data.violationType, data.payload);

      // Show the new severity immediately; the next heartbeat brings the server's
      // authoritative status (which also escalates repeated offences).
      setStudentStatuses(prev => {
        const next = new Map(prev);
        const existing = next.get(data.userId);
        if (existing) {
          next.set(data.userId, {
            ...existing,
            violationCount: existing.violationCount + 1,
            tileStatus: worseStatus(existing.tileStatus, severityOf(data.violationType)),
            lastViolation: label
          });
        }
        return next;
      });
      bump(data.userId);
    });

    // Module 5: the server's watchdog saw no heartbeat for 30 s — the PC is off the network, crashed or was closed.
    connection.on('StudentDisconnected', (data: any) => {
      if (!forThisExam(data)) return;
      patchStudent(data.userId, { isDisconnected: true, disconnectedAt: data.disconnectedAt ?? new Date().toISOString() });
      bump(data.userId);
    });

    connection.on('StudentReconnected', (data: any) => {
      if (!forThisExam(data)) return;
      patchStudent(data.userId, { isDisconnected: false, disconnectedAt: null });
      bump(data.userId);
    });

    // Module 5: a new capture was stored — show the latest screen capture on the student's tile.
    connection.on('ScreenshotUploaded', (data: any) => {
      if (!forThisExam(data)) return;
      if (data.kind === 'Screen') {
        showThumbnail(activeExam.examId, data.userId, { screenshotId: data.screenshotId, capturedAt: data.capturedAt, trigger: data.trigger });
      }
      bump(data.userId);
    });

    // Teacher extended time (possibly from another proctor's session) — resync countdown
    connection.on('TimeExtended', (data: any) => {
      setActiveExam(prev => prev && prev.examId === data.examId
        ? { ...prev, durationMinutes: data.newDurationMinutes }
        : prev);
    });

    // Module 6: a session was finalised (student submit, force-submit, server timeout) — the tile says so.
    connection.on('SessionFinalized', (data: any) => {
      if (!forThisExam(data)) return;
      patchStudent(data.userId, { sessionStatus: data.status, unlockedUntil: null });
      bump(data.userId);
    });

    // Module 6: a proctor gave one student extra minutes (possibly from another proctor's session).
    connection.on('StudentTimeExtended', (data: any) => {
      if (!forThisExam(data)) return;
      patchStudent(data.userId, { extraMinutes: data.extraMinutes });
      bump(data.userId);
    });

    // Module 10: incidents, manual unlock, warnings and readiness — from this or another proctor's screen.
    connection.on('IncidentChanged', (data: any) => {
      if (data?.incident) upsertIncident(data.incident as Incident);
    });
    connection.on('SessionUnlocked', (data: any) => {
      if (!forThisExam(data)) return;
      patchStudent(data.userId, { unlockedUntil: data.unlockedUntilUtc });
      bump(data.userId);
    });
    connection.on('SessionRelocked', (data: any) => {
      if (!forThisExam(data)) return;
      patchStudent(data.userId, { unlockedUntil: null });
      bump(data.userId);
    });
    connection.on('ProctorWarningSent', (data: any) => {
      if (!forThisExam(data)) return;
      bump(data.userId);
    });
    connection.on('ReadinessUpdated', (data: any) => {
      if (!forThisExam(data)) return;
      loadReadiness(activeExam.examId);
    });

    // The server took this connection out of the exam's group (proctor unassigned, account changed):
    // no more live data will arrive, so say so instead of showing a silently frozen board.
    connection.on('ExamAccessRevoked', (data: any) => {
      if (data?.examId !== activeExam.examId) return;
      setStudentStatuses(new Map());
      setIncidents([]);
      setReadiness(new Map());
      setSelectedStudentId(null);
      setJoinError('Your access to this exam was withdrawn, so live data has stopped. Ask the exam\'s teacher if this is unexpected.');
    });

    connection.start()
      .then(async () => {
        connectionRef.current = connection;
        // Broadcasts are scoped per exam — join this exam's proctoring group
        try {
          await connection.invoke('JoinExamGroup', activeExam.examId);
          setJoinError(null);
        } catch (err) {
          // The hub only admits the section's teacher, an assigned proctor or an admin (Module 7).
          console.error('Failed to join exam group', err);
          if (!disposed) setJoinError('You are not assigned to supervise this exam, so no live data will appear. Ask the exam\'s teacher to assign you.');
        }
        console.log('SignalR connected successfully.');
      })
      .catch(err => console.error('SignalR Connection Error: ', err));

    return () => {
      disposed = true;
      connection.stop().then(() => console.log('SignalR stopped.'));
    };
  }, [activeExam, token, bump, showThumbnail, upsertIncident, loadReadiness]);

  const invoke = async (method: string, ...args: unknown[]) => {
    if (!connectionRef.current) throw new Error('The live connection is not ready yet.');
    await connectionRef.current.invoke(method, ...args);
  };

  const handleSendWarning = async (studentId: string, message: string) => {
    await invoke('SendWarning', studentId, message);
    bump(studentId);
  };

  const handleUnlock = async (studentId: string, minutes: number, reason: string) => {
    const student = studentStatuses.get(studentId);
    if (!student?.sessionId) return;
    await invoke('UnlockSession', student.sessionId, minutes, reason);
  };

  const handleRelock = async (studentId: string) => {
    const student = studentStatuses.get(studentId);
    if (!student?.sessionId) return;
    await invoke('RelockSession', student.sessionId);
  };

  const handleForceSubmitStudent = async (studentId: string) => {
    const student = studentStatuses.get(studentId);
    if (!student) return;

    if (confirm(`Are you sure you want to force-submit ${student.studentName}'s exam session immediately?`)) {
      try {
        if (connectionRef.current) {
          // By session id (unambiguous when the student also has a practice session open, D-20).
          if (student.sessionId) {
            await connectionRef.current.invoke('ForceSubmitSessionById', student.sessionId);
          } else {
            await connectionRef.current.invoke('ForceSubmitSession', student.userId);
          }
        }
        // SessionFinalized normally arrives first; set it here too so the tile never lags.
        setStudentStatuses(prev => {
          const existing = prev.get(studentId);
          if (!existing) return prev;
          const next = new Map(prev);
          next.set(studentId, { ...existing, sessionStatus: 'ForceSubmitted' });
          return next;
        });
        bump(studentId);

        alert(`Session force-submitted for ${student.studentName}.`);
      } catch (err) {
        console.error('Failed to force submit student session', err);
        alert('Could not force submit. Connection error.');
      }
    }
  };

  // Module 6/10: extra minutes for ONE student (their deadline only; the exam's duration is unchanged).
  const handleExtendStudentTime = async (studentId: string) => {
    const student = studentStatuses.get(studentId);
    if (!student?.sessionId || !connectionRef.current) return;
    const input = prompt(`Give ${student.studentName} how many extra minutes? (1–180)`, '10');
    if (!input) return;
    const minutes = parseInt(input, 10);
    if (isNaN(minutes) || minutes <= 0 || minutes > 180) {
      alert('Enter a number of minutes between 1 and 180.');
      return;
    }
    try {
      await connectionRef.current.invoke('ExtendStudentTime', student.sessionId, minutes);
    } catch (err: any) {
      console.error('Failed to extend student time', err);
      alert(err?.message?.replace(/^.*HubException: /, '') || 'Could not extend the student\'s time.');
    }
  };

  const handleExtendTime = async () => {
    if (!activeExam) return;
    const input = prompt('Extend exam time by how many minutes? (1–180)', '15');
    if (!input) return;
    const minutes = parseInt(input, 10);
    if (isNaN(minutes) || minutes <= 0 || minutes > 180) {
      alert('Enter a number of minutes between 1 and 180.');
      return;
    }
    try {
      const result: any = await examsApi.extendTime(activeExam.examId, minutes);
      setActiveExam(prev => prev ? { ...prev, durationMinutes: result.durationMinutes } : prev);
      alert(`Exam extended by ${minutes} minutes. Student clients will resync on their next heartbeat.`);
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to extend exam time.');
    }
  };

  const handleForceSubmitAll = async () => {
    if (!activeExam) return;
    if (confirm('Are you sure you want to end this exam and force-submit all student sessions? This cannot be undone.')) {
      try {
        await examsApi.forceSubmitAll(activeExam.examId);
        alert('All active sessions have been force-submitted.');
        navigate(homePath);
      } catch (err) {
        console.error('Failed to force submit all sessions', err);
        alert('An error occurred while ending the exam.');
      }
    }
  };

  const selectedStudent = selectedStudentId ? studentStatuses.get(selectedStudentId) : null;
  const openIncidentCount = (userId: string) =>
    incidents.filter(i => i.userId === userId && i.status !== 'Resolved').length;

  return (
    <div className="bg-[#F8FAFC] text-slate-800 font-sans min-h-screen flex overflow-hidden">
      {/* 64px Collapsed Sidebar */}
      <aside className="w-[64px] h-screen bg-white border-r border-slate-200 flex flex-col items-center py-6 shrink-0 z-50">
        <div className="mb-8 cursor-pointer" onClick={() => navigate(homePath)}>
          <span className="material-symbols-outlined text-primary-500 text-[32px] font-bold">shield</span>
        </div>
        <nav data-testid="live-monitor-nav" className="flex flex-col gap-4 flex-grow">
          {!isInvigilator && (<>
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
          </>)}
          <button 
            className="w-10 h-10 flex items-center justify-center rounded-lg text-primary-500 bg-blue-50 border border-blue-100 font-bold shadow-sm" 
            title="Live Monitor"
          >
            <span className="material-symbols-outlined text-[22px]" style={{ fontVariationSettings: "'FILL' 1" }}>sensors</span>
          </button>
          {!isInvigilator && (
          <button 
            onClick={() => navigate('/teacher/eligibility')} 
            className="w-10 h-10 flex items-center justify-center rounded-lg text-slate-400 hover:text-slate-600 hover:bg-slate-50 transition-colors" 
            title="Eligibility"
          >
            <span className="material-symbols-outlined text-[22px]">check_circle</span>
          </button>
          )}
        </nav>
        <div className="mt-auto flex flex-col gap-4">
          <div className="w-10 h-10 rounded-full bg-blue-50 text-primary-600 font-bold flex items-center justify-center text-xs" title={user?.name}>
            {user?.name ? user.name.split(' ').map((n: string) => n[0]).join('') : 'TE'}
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
            {!isInvigilator && (
            <button 
              onClick={() => navigate('/teacher/dashboard')}
              className="text-slate-400 hover:text-slate-600 transition-colors mr-2 flex items-center"
            >
              <span className="material-symbols-outlined">arrow_back</span>
            </button>
            )}
            <span className="text-sm font-semibold text-slate-800">
              Live Monitor {activeExam ? `— ${activeExam.title}` : ''}
            </span>
            {isInvigilator && (
              <span data-testid="proctor-mode-badge" className="ml-2 px-2 py-0.5 bg-amber-50 text-amber-700 border border-amber-200 text-[10px] font-bold rounded uppercase tracking-wider">
                Invigilator · assigned exams only
              </span>
            )}
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
              {runningExams.length > 0 && (
                <select
                  data-testid="exam-picker"
                  aria-label="Exam"
                  value={activeExam?.examId ?? ''}
                  onChange={e => handleSelectExam(e.target.value)}
                  className="ml-2 h-8 max-w-[260px] border border-slate-200 bg-white rounded-lg px-2 text-xs text-slate-700 focus:ring-2 focus:ring-primary-500 outline-none cursor-pointer"
                  title={isInvigilator ? 'Exams you are assigned to supervise' : 'Running exams'}
                >
                  {runningExams.map(e => (
                    <option key={e.examId} value={e.examId}>{e.title} ({e.courseName})</option>
                  ))}
                </select>
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
                {/* Extending or ending the whole exam is the teacher's / admin's call, not a proctor's. */}
                {!isInvigilator && (<>
                <button
                  onClick={handleExtendTime}
                  className="px-4 py-2 bg-blue-50 text-primary-600 border border-blue-200 font-bold text-xs rounded-xl hover:bg-blue-100 transition-all active:scale-95 flex items-center gap-1.5"
                  title="Grant extra minutes to all students in this exam"
                >
                  <span className="material-symbols-outlined text-[16px]">more_time</span>
                  Extend Time
                </button>
                <button
                  onClick={handleForceSubmitAll}
                  className="px-4 py-2 bg-red-600 text-white font-bold text-xs rounded-xl hover:bg-red-700 transition-all shadow-sm active:scale-95 flex items-center gap-1.5"
                >
                  <span className="material-symbols-outlined text-[16px]">power_settings_new</span>
                  Force End Exam
                </button>
                </>)}
              </div>
            </>
          )}
        </div>

        {/* Main Content Area */}
        <main className={`flex-grow p-8 overflow-y-auto ${selectedStudentId ? 'mr-[360px]' : ''} transition-all duration-300`}>
          {joinError && activeExam && (
            <div role="alert" data-testid="join-denied" className="mb-4 px-4 py-3 rounded-xl border border-amber-200 bg-amber-50 text-amber-800 text-sm">
              {joinError}
            </div>
          )}
          {loading ? (
            <div className="h-full flex items-center justify-center flex-col">
              <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin mb-2" />
              <p className="text-sm text-slate-400">Loading live proctoring hub...</p>
            </div>
          ) : !activeExam ? (
            <div className="h-full flex items-center justify-center flex-col text-center p-12 bg-white rounded-2xl border border-slate-200 border-dashed">
              <span className="material-symbols-outlined text-[64px] text-slate-300 mb-3">sensors_off</span>
              <h3 className="text-lg font-bold text-slate-700">No Exam Currently Active</h3>
              {isInvigilator ? (
                <p className="text-sm text-slate-400 max-w-sm mt-1">
                  None of the exams you are assigned to supervise is running. Exams appear here once a teacher assigns you and the first student starts.
                </p>
              ) : (<>
              <p className="text-sm text-slate-400 max-w-sm mt-1 mb-6">
                Live proctoring dashboard activates when a scheduled exam starts. Build an exam or start one to begin tracking workstation metrics.
              </p>
              <button 
                onClick={() => navigate('/teacher/create-exam')}
                className="px-6 py-2.5 bg-primary-500 text-white rounded-xl font-semibold text-xs shadow-md shadow-primary-500/20 hover:bg-primary-600 transition-all"
              >
                Create Scheduled Exam
              </button>
              </>)}
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
            <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 xl:grid-cols-4 gap-4 stagger">
              {Array.from(studentStatuses.values()).map(s => {
                const isSelected = selectedStudentId === s.userId;
                let statusColor = 'border-green-500/30 ring-transparent hover:ring-green-500/10';
                let heartColor = 'text-green-500';
                let statusText = 'Stable';

                if (s.tileStatus === 'Warning') {
                  statusColor = 'border-amber-300 ring-amber-50';
                  heartColor = 'text-amber-500';
                  statusText = 'Warning';
                } else if (s.tileStatus === 'Suspicious') {
                  statusColor = 'border-orange-500 ring-orange-100';
                  heartColor = 'text-orange-500';
                  statusText = 'Suspicious';
                } else if (s.tileStatus === 'Violation') {
                  statusColor = 'border-red-500 ring-red-100';
                  heartColor = 'text-red-500';
                  statusText = 'Critical';
                }
                const finalised = isFinalised(s.sessionStatus);
                if (finalised) {
                  statusColor = 'border-slate-300 ring-transparent opacity-80';
                  heartColor = 'text-slate-400';
                  statusText = sessionStatusLabel(s.sessionStatus);
                } else if (s.isDisconnected) {
                  // Module 5: silent for 30 s. Violations still count, but the first thing to do is go and look.
                  statusColor = 'border-slate-400 border-dashed ring-slate-100';
                  heartColor = 'text-slate-500';
                  statusText = 'Disconnected';
                }
                const thumbnail = thumbnails.get(s.userId);
                const idle = formatIdle(s.idleSeconds);
                const rState = readinessState(readiness.get(s.userId));
                const openCount = openIncidentCount(s.userId);
                const unlocked = !finalised && isUnlocked(s.unlockedUntil);
                const cameraLabel = s.cameraStatus ? CAMERA_LABEL[s.cameraStatus] : undefined;

                return (
                  <div 
                    key={s.userId}
                    data-testid="student-tile"
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
                          Seat {s.workstationNumber}{s.machineName ? ` • ${s.machineName}` : ''}{s.windowsUsername ? ` (${s.windowsUsername})` : ''}
                        </p>
                        {s.seatStatus === 'Mismatch' && (
                          <span data-testid="seat-mismatch" className="inline-block mt-1 px-1.5 py-0.5 rounded bg-red-100 text-red-700 text-[9px] font-bold uppercase tracking-wider">
                            Wrong seat
                          </span>
                        )}
                        {/* Module 10: pre-exam readiness result, open incidents, proctor unlock */}
                        <div className="flex flex-wrap gap-1 mt-1">
                          <span
                            data-testid="tile-readiness"
                            data-state={rState}
                            title="Pre-exam system check (Module 2)"
                            className={`px-1.5 py-0.5 rounded text-[9px] font-bold uppercase tracking-wider ${rState === 'passed' ? 'bg-green-50 text-green-700' : rState === 'failed' ? 'bg-red-50 text-red-700' : 'bg-slate-50 text-slate-400'}`}
                          >
                            {READINESS_LABEL[rState]}
                          </span>
                          {openCount > 0 && (
                            <span data-testid="incident-badge" className="px-1.5 py-0.5 rounded bg-orange-100 text-orange-700 text-[9px] font-bold uppercase tracking-wider">
                              {openCount} incident{openCount === 1 ? '' : 's'}
                            </span>
                          )}
                          {unlocked && (
                            <span data-testid="tile-unlocked" className="px-1.5 py-0.5 rounded bg-blue-100 text-blue-700 text-[9px] font-bold uppercase tracking-wider"
                              title={`Lockdown suspended until ${new Date(s.unlockedUntil!).toLocaleTimeString()}`}>
                              Unlocked
                            </span>
                          )}
                        </div>
                      </div>
                      <div className="flex flex-col items-end">
                        <span className={`material-symbols-outlined ${heartColor}`} style={{ fontVariationSettings: "'FILL' 1" }}>favorite</span>
                        <span data-testid="tile-status" className={`text-[8px] font-bold uppercase tracking-wider mt-0.5 ${heartColor}`}>{statusText}</span>
                        {!finalised && (s.extraMinutes ?? 0) > 0 && (
                          <span data-testid="tile-extra-time" className="text-[8px] font-bold text-primary-600 mt-0.5">+{s.extraMinutes} min</span>
                        )}
                      </div>
                    </div>

                    {thumbnail && (
                      <img
                        data-testid="tile-thumbnail"
                        src={thumbnail.url}
                        alt={`Latest screen of ${s.studentName}`}
                        title={`Captured ${new Date(thumbnail.capturedAt).toLocaleTimeString()}${thumbnail.trigger === 'Violation' ? ' (violation)' : ''}`}
                        className="w-full aspect-video object-cover rounded-lg border border-slate-200 mb-2 bg-slate-100"
                      />
                    )}

                    {(idle || cameraLabel) && !finalised && (
                      <div className="flex flex-wrap gap-1 mb-2">
                        {cameraLabel && (
                          <span data-testid="tile-camera" className="px-1.5 py-0.5 rounded bg-orange-100 text-orange-700 text-[9px] font-bold uppercase tracking-wider">
                            {cameraLabel}
                          </span>
                        )}
                        {idle && (
                          <span data-testid="tile-idle" className="px-1.5 py-0.5 rounded bg-slate-100 text-slate-600 text-[9px] font-bold uppercase tracking-wider" title="No keyboard or mouse input on the PC">
                            {idle}
                          </span>
                        )}
                      </div>
                    )}

                    {s.lastViolation && (
                      <p data-testid="tile-last-violation" className={`text-[10px] font-semibold mb-2 truncate ${heartColor}`} title={s.lastViolation}>
                        {s.lastViolation}
                      </p>
                    )}

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

      {/* Student detail drawer (Module 10) */}
      {selectedStudent && activeExam && (
        <StudentDrawer
          key={selectedStudent.sessionId}
          exam={activeExam}
          student={selectedStudent}
          readiness={readiness.get(selectedStudent.userId)}
          incidents={incidents.filter(i => i.userId === selectedStudent.userId)}
          refreshKey={activity.get(selectedStudent.userId) ?? 0}
          isInvigilator={isInvigilator}
          onClose={() => setSelectedStudentId(null)}
          onSendWarning={message => handleSendWarning(selectedStudent.userId, message)}
          onForceSubmit={() => handleForceSubmitStudent(selectedStudent.userId)}
          onExtendTime={() => handleExtendStudentTime(selectedStudent.userId)}
          onUnlock={(minutes, reason) => handleUnlock(selectedStudent.userId, minutes, reason)}
          onRelock={() => handleRelock(selectedStudent.userId)}
          onIncidentSaved={upsertIncident}
        />
      )}

    </div>
  );
}

// Incidents by id, the most recently updated copy wins (a PATCH response and its IncidentChanged echo can arrive in
// either order); newest first.
function mergeIncidents(incoming: Incident[], current: Incident[]): Incident[] {
  const byId = new Map(current.map(i => [i.incidentId, i]));
  for (const i of incoming) {
    const known = byId.get(i.incidentId);
    if (!known || new Date(i.updatedAt).getTime() >= new Date(known.updatedAt).getTime()) byId.set(i.incidentId, i);
  }
  return Array.from(byId.values()).sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
}
