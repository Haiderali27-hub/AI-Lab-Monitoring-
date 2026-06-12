import { useState, useEffect } from 'react';
import StudentLayout from '../../components/layout/StudentLayout';
import { studentApi } from '../../api/student.api';
import { examsApi } from '../../api/exams.api';
import { useAuth } from '../../context/AuthContext';
import type { StudentDashboard, StudentExamSummary } from '../../types';
import { useToast } from '../../context/ToastContext';
import { format } from 'date-fns';
import { 
  Shield, 
  Calendar, 
  Clock, 
  MapPin, 
  AlertTriangle, 
  Play, 
  CheckCircle, 
  Award, 
  TrendingUp,
  FileText
} from 'lucide-react';

export default function StudentDashboardPage() {
  const { user } = useAuth();
  const { showToast } = useToast();
  
  const [dashboardData, setDashboardData] = useState<StudentDashboard | null>(null);
  const [exams, setExams] = useState<any[]>([]);
  const [selectedExamId, setSelectedExamId] = useState<string | null>(null);
  const [selectedExamDetails, setSelectedExamDetails] = useState<any | null>(null);
  
  const [loading, setLoading] = useState(true);
  const [countdown, setCountdown] = useState({ hours: 0, minutes: 0, seconds: 0, finished: false });

  // Initials for profile avatar
  const initials = user?.name.split(' ').map((n: string) => n[0]).join('').toUpperCase().slice(0, 2) ?? 'ST';

  useEffect(() => {
    Promise.all([
      studentApi.getDashboard(),
      studentApi.getMyExams()
    ])
      .then(([dash, myExams]) => {
        setDashboardData(dash);
        setExams(myExams);
        
        // Auto select first scheduled or active exam if exists
        const upcoming = myExams.find((e: any) => e.status === 'Scheduled' || e.status === 'Active');
        if (upcoming) {
          setSelectedExamId(upcoming.examId);
        }
      })
      .catch((err) => {
        console.error(err);
        showToast('Error', 'Failed to load dashboard data', 'error');
      })
      .finally(() => setLoading(false));
  }, []);

  // Fetch full details when selectedExamId changes
  useEffect(() => {
    if (!selectedExamId) {
      setSelectedExamDetails(null);
      return;
    }
    
    // Fetch full exam details to get startTime, duration, etc.
    examsApi.getById(selectedExamId)
      .then(details => {
        setSelectedExamDetails(details);
      })
      .catch(() => {
        // Fallback to basic info from list
        const basic = exams.find(e => e.examId === selectedExamId);
        if (basic) {
          setSelectedExamDetails({
            examId: basic.examId,
            title: basic.title,
            courseName: basic.courseName,
            status: basic.status,
            startTime: basic.startedAt || new Date().toISOString(),
            durationMinutes: 60,
            location: 'Lab A — Block 3 (PC-04)'
          });
        }
      });
  }, [selectedExamId, exams]);

  // Countdown timer logic
  useEffect(() => {
    if (!selectedExamDetails || selectedExamDetails.status !== 'Scheduled') {
      setCountdown({ hours: 0, minutes: 0, seconds: 0, finished: true });
      return;
    }

    const timer = setInterval(() => {
      const start = new Date(selectedExamDetails.startTime).getTime();
      const now = new Date().getTime();
      const diff = start - now;

      if (diff <= 0) {
        setCountdown({ hours: 0, minutes: 0, seconds: 0, finished: true });
        clearInterval(timer);
      } else {
        const hours = Math.floor(diff / (1000 * 60 * 60));
        const minutes = Math.floor((diff % (1000 * 60 * 60)) / (1000 * 60));
        const seconds = Math.floor((diff % (1000 * 60)) / 1000);
        setCountdown({ hours, minutes, seconds, finished: false });
      }
    }, 1000);

    return () => clearInterval(timer);
  }, [selectedExamDetails]);

  const handleStartExam = () => {
    showToast('Secure Proctor', 'Launching secure browser environment...', 'info');
    // Simulate launching exam proctoring
    setTimeout(() => {
      showToast('Secure Proctor', 'Secure window initialized. Monitoring active.', 'success');
    }, 2000);
  };

  return (
    <StudentLayout>
      <div className="max-w-6xl mx-auto w-full">
        {loading ? (
          <div className="py-24 flex justify-center">
            <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin" />
          </div>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-[300px_1fr] gap-6 items-start">
            
            {/* Left Sidebar: Profile & Exam List */}
            <aside className="bg-white border border-slate-200 rounded-xl overflow-hidden shadow-sm flex flex-col">
              
              {/* Profile Card */}
              <div className="p-6 flex flex-col items-center text-center border-b border-slate-100">
                <div className="w-16 h-16 bg-primary-500 text-white rounded-full flex items-center justify-center text-xl font-bold mb-3 shadow-md">
                  {initials}
                </div>
                <h2 className="text-lg font-bold text-slate-800 leading-tight">Welcome back, {user?.name}</h2>
                <p className="text-xs text-slate-400 mt-1">{user?.email}</p>
                <div className="mt-3 flex items-center gap-1.5 bg-green-50 text-green-700 px-3 py-1 rounded-full border border-green-200 text-xs font-semibold">
                  <CheckCircle size={14} className="text-green-600" />
                  <span>Device Bound</span>
                </div>
              </div>

              {/* Exam Navigation List */}
              <div className="p-4 flex-1">
                <div className="flex items-center justify-between mb-3 px-2">
                  <span className="text-[11px] font-bold tracking-wider text-slate-400 uppercase">My Exams Roster</span>
                  <span className="bg-primary-50 text-primary-600 text-xs px-2 py-0.5 rounded-full font-bold">
                    {exams.length}
                  </span>
                </div>

                <nav className="space-y-1.5 max-h-[300px] overflow-y-auto custom-scrollbar">
                  {exams.length === 0 ? (
                    <div className="text-xs text-slate-400 text-center py-4">No exams registered.</div>
                  ) : (
                    exams.map(exam => {
                      const isActive = selectedExamId === exam.examId;
                      return (
                        <div
                          key={exam.examId}
                          onClick={() => setSelectedExamId(exam.examId)}
                          className={`px-3 py-3 rounded-lg border-l-4 cursor-pointer transition-all duration-200 flex flex-col gap-1
                            ${isActive 
                              ? 'bg-blue-50/50 border-l-primary-500 text-primary-700' 
                              : 'border-l-transparent text-slate-600 hover:bg-slate-50'}`}
                        >
                          <span className={`text-sm font-bold leading-tight ${isActive ? 'text-primary-700' : 'text-slate-800'}`}>
                            {exam.title}
                          </span>
                          <span className="text-[10px] font-semibold text-slate-400 uppercase">
                            {exam.courseName} • {exam.status}
                          </span>
                        </div>
                      );
                    })
                  )}
                </nav>
              </div>

              {/* Footer Version */}
              <div className="p-4 border-t border-slate-100 text-center bg-slate-50">
                <p className="text-[9px] font-bold text-slate-400 uppercase tracking-widest">
                  System Version 4.2.0-Stable
                </p>
              </div>
            </aside>

            {/* Right Panel: Content Area */}
            <main className="space-y-6">
              
              {/* If an exam is selected, show details panel */}
              {selectedExamDetails ? (
                <div className="bg-white rounded-xl border border-slate-200 shadow-sm p-6">
                  
                  {/* Header Details */}
                  <div className="flex flex-col sm:flex-row justify-between items-start gap-4 mb-6">
                    <div>
                      <h2 className="text-2xl font-extrabold text-slate-800 tracking-tight mb-1">
                        {selectedExamDetails.title}
                      </h2>
                      <p className="text-sm text-slate-500 font-medium">
                        {selectedExamDetails.courseName} • Section {selectedExamDetails.sectionName || 'A'}
                      </p>
                    </div>
                    <span className={`px-3 py-1 rounded-full text-xs font-bold uppercase tracking-wider
                      ${selectedExamDetails.status === 'Active' 
                        ? 'bg-green-100 text-green-700' 
                        : selectedExamDetails.status === 'Ended'
                          ? 'bg-slate-100 text-slate-600'
                          : 'bg-blue-100 text-blue-600'}`}>
                      {selectedExamDetails.status}
                    </span>
                  </div>

                  <hr className="border-slate-100 mb-6" />

                  {/* 2-column info grid */}
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-6 mb-6">
                    <div className="flex items-center gap-3.5">
                      <div className="w-10 h-10 rounded-lg bg-slate-50 border border-slate-100 flex items-center justify-center text-primary-500">
                        <Calendar size={20} />
                      </div>
                      <div>
                        <p className="text-[10px] font-bold text-slate-400 uppercase tracking-wider">Date</p>
                        <p className="text-sm font-semibold text-slate-700">
                          {format(new Date(selectedExamDetails.startTime || selectedExamDetails.startedAt || new Date()), 'MMMM dd, yyyy')}
                        </p>
                      </div>
                    </div>

                    <div className="flex items-center gap-3.5">
                      <div className="w-10 h-10 rounded-lg bg-slate-50 border border-slate-100 flex items-center justify-center text-primary-500">
                        <Clock size={20} />
                      </div>
                      <div>
                        <p className="text-[10px] font-bold text-slate-400 uppercase tracking-wider">Start Time</p>
                        <p className="text-sm font-semibold text-slate-700">
                          {format(new Date(selectedExamDetails.startTime || selectedExamDetails.startedAt || new Date()), 'hh:mm a')}
                        </p>
                      </div>
                    </div>

                    <div className="flex items-center gap-3.5">
                      <div className="w-10 h-10 rounded-lg bg-slate-50 border border-slate-100 flex items-center justify-center text-primary-500">
                        <Shield size={20} />
                      </div>
                      <div>
                        <p className="text-[10px] font-bold text-slate-400 uppercase tracking-wider">Duration</p>
                        <p className="text-sm font-semibold text-slate-700">
                          {selectedExamDetails.durationMinutes} Minutes
                        </p>
                      </div>
                    </div>

                    <div className="flex items-center gap-3.5">
                      <div className="w-10 h-10 rounded-lg bg-slate-50 border border-slate-100 flex items-center justify-center text-primary-500">
                        <MapPin size={20} />
                      </div>
                      <div>
                        <p className="text-[10px] font-bold text-slate-400 uppercase tracking-wider">Location / Desk</p>
                        <p className="text-sm font-semibold text-slate-700">
                          {selectedExamDetails.location || 'Lab A — Block 3 (PC-04)'}
                        </p>
                      </div>
                    </div>
                  </div>

                  <hr className="border-slate-100 mb-6" />

                  {/* Warning Instructions */}
                  <div className="bg-amber-50/50 p-4 rounded-lg mb-6 border-l-4 border-amber-500 flex gap-3.5 shadow-sm">
                    <AlertTriangle size={22} className="text-amber-600 mt-0.5 flex-shrink-0" />
                    <div>
                      <h4 className="text-xs font-bold text-amber-700 tracking-wider uppercase mb-1">Secure Mode Active</h4>
                      <p className="text-xs text-slate-600 leading-relaxed">
                        This exam will launch in a full-screen locked mode. All background applications must be closed.
                        Attempting to switch windows, navigate away, or open external resources will result in an automatic flag and force-submission.
                      </p>
                    </div>
                  </div>

                  {/* Countdown area if scheduled */}
                  {selectedExamDetails.status === 'Scheduled' && (
                    <div className="text-center bg-slate-50 rounded-xl p-5 border border-slate-100 mb-6">
                      <p className="text-[10px] font-bold text-slate-400 uppercase tracking-wider mb-2">Exam Starts In</p>
                      <div className="font-mono text-3xl font-bold text-primary-600 flex justify-center items-center gap-3">
                        <span>{countdown.hours.toString().padStart(2, '0')}</span>
                        <span className="text-slate-300 animate-pulse">:</span>
                        <span>{countdown.minutes.toString().padStart(2, '0')}</span>
                        <span className="text-slate-300 animate-pulse">:</span>
                        <span>{countdown.seconds.toString().padStart(2, '0')}</span>
                      </div>
                    </div>
                  )}

                  {/* Action launch button */}
                  {selectedExamDetails.status === 'Active' || (selectedExamDetails.status === 'Scheduled' && countdown.finished) ? (
                    <button
                      onClick={handleStartExam}
                      className="w-full h-11 bg-primary-600 text-white rounded-lg font-bold hover:bg-primary-700 transition-all active:scale-[0.99] flex items-center justify-center gap-2 shadow-md hover:shadow-lg"
                    >
                      <Play size={18} fill="currentColor" />
                      <span>Start Exam Now</span>
                    </button>
                  ) : selectedExamDetails.status === 'Ended' ? (
                    <button
                      disabled
                      className="w-full h-11 bg-slate-100 text-slate-400 rounded-lg font-bold cursor-not-allowed flex items-center justify-center gap-2"
                    >
                      <span>Exam Completed</span>
                    </button>
                  ) : (
                    <div className="relative group">
                      <button
                        disabled
                        className="w-full h-11 bg-slate-100 text-slate-400 rounded-lg font-bold cursor-not-allowed flex items-center justify-center gap-2"
                      >
                        <Play size={18} />
                        <span>Start Exam</span>
                        <span className="text-xs font-normal opacity-70">
                          (Available at {format(new Date(selectedExamDetails.startTime || selectedExamDetails.startedAt || new Date()), 'hh:mm a')})
                        </span>
                      </button>
                    </div>
                  )}
                </div>
              ) : null}

              {/* Dashboard overview stats */}
              {dashboardData && (
                <div className="bg-white rounded-xl border border-slate-200 shadow-sm p-6 space-y-6">
                  <div>
                    <h2 className="text-xl font-extrabold text-slate-900 tracking-tight">Academic Overview</h2>
                    <p className="text-xs text-slate-500">Your overall performance metrics across all proctored exam sessions.</p>
                  </div>

                  {/* 3-column overview stats */}
                  <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
                    <div className="bg-gradient-to-br from-blue-50/50 to-indigo-50/20 p-4 rounded-xl border border-blue-100/50 flex flex-col justify-between">
                      <FileText className="text-blue-500 mb-2" size={20} />
                      <div>
                        <span className="text-xs font-semibold text-slate-500 uppercase tracking-wider block">Exams Taken</span>
                        <span className="text-2xl font-bold text-slate-800">{dashboardData.totalExamsTaken}</span>
                      </div>
                    </div>
                    
                    <div className="bg-gradient-to-br from-green-50/50 to-emerald-50/20 p-4 rounded-xl border border-green-100/50 flex flex-col justify-between">
                      <Award className="text-green-500 mb-2" size={20} />
                      <div>
                        <span className="text-xs font-semibold text-slate-500 uppercase tracking-wider block">Average Score</span>
                        <span className="text-2xl font-bold text-slate-800">{dashboardData.averageScore}%</span>
                      </div>
                    </div>

                    <div className="bg-gradient-to-br from-orange-50/50 to-amber-50/20 p-4 rounded-xl border border-orange-100/50 flex flex-col justify-between">
                      <AlertTriangle className="text-orange-500 mb-2" size={20} />
                      <div>
                        <span className="text-xs font-semibold text-slate-500 uppercase tracking-wider block">Total Flags</span>
                        <span className="text-2xl font-bold text-slate-800">{dashboardData.totalViolations}</span>
                      </div>
                    </div>
                  </div>

                  {/* Recent exams summary */}
                  {dashboardData.recentExams && dashboardData.recentExams.length > 0 && (
                    <div className="space-y-3">
                      <h3 className="text-xs font-bold text-slate-400 uppercase tracking-wider">Recent Graded Performance</h3>
                      <div className="space-y-2">
                        {dashboardData.recentExams.slice(0, 3).map((exam: StudentExamSummary, i: number) => (
                          <div key={i} className="flex justify-between items-center p-3 bg-slate-50 rounded-lg border border-slate-100">
                            <div>
                              <p className="text-sm font-bold text-slate-800 leading-snug">{exam.title}</p>
                              <p className="text-xs text-slate-400 uppercase font-semibold mt-0.5">
                                {exam.courseName} • {format(new Date(exam.startedAt || new Date().toISOString()), 'MMM dd, yyyy')}
                              </p>
                            </div>
                            <div className="text-right">
                              <span className={`text-sm font-bold block ${exam.scorePercent >= 50 ? 'text-green-600' : 'text-red-500'}`}>
                                {exam.scorePercent}%
                              </span>
                              <span className="text-[10px] font-semibold text-slate-400 uppercase">
                                {exam.earnedMarks}/{exam.totalMarks} marks
                              </span>
                            </div>
                          </div>
                        ))}
                      </div>
                    </div>
                  )}

                  {/* CSS Chart */}
                  {dashboardData.performanceTrend && dashboardData.performanceTrend.length > 0 && (
                    <div className="space-y-3">
                      <div className="flex justify-between items-center">
                        <h3 className="text-xs font-bold text-slate-400 uppercase tracking-wider">Performance Trend</h3>
                        <span className="text-xs font-semibold text-slate-400 flex items-center gap-1">
                          <TrendingUp size={14} className="text-primary-500" />
                          Scores over time
                        </span>
                      </div>

                      <div className="h-24 bg-slate-50 rounded-lg p-3 border border-slate-100 flex items-end justify-between gap-2">
                        {dashboardData.performanceTrend.map((pt: { title: string; scorePercent: number; startedAt: string }, index: number) => {
                          return (
                            <div key={index} className="flex-1 flex flex-col items-center gap-1 group relative">
                              {/* Tooltip */}
                              <div className="absolute bottom-full mb-1 bg-slate-800 text-white text-[10px] px-2 py-0.5 rounded shadow opacity-0 group-hover:opacity-100 transition-opacity whitespace-nowrap z-10 pointer-events-none">
                                {pt.title}: {pt.scorePercent}%
                              </div>
                              <div className="w-full bg-slate-200 rounded-t h-12 flex items-end">
                                <div
                                  style={{ height: `${pt.scorePercent}%` }}
                                  className={`w-full rounded-t transition-all duration-500
                                    ${pt.scorePercent >= 50 ? 'bg-green-500' : 'bg-red-500'}`}
                                />
                              </div>
                              <span className="text-[9px] font-semibold text-slate-400 truncate w-12 text-center">
                                {pt.title}
                              </span>
                            </div>
                          );
                        })}
                      </div>
                    </div>
                  )}
                </div>
              )}
            </main>
          </div>
        )}
      </div>
    </StudentLayout>
  );
}
