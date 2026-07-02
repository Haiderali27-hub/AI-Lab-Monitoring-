import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import AppLayout from '../../components/layout/AppLayout';
import { useAuth } from '../../context/AuthContext';
import { examsApi } from '../../api/exams.api';
import type { Exam } from '../../types';

export default function TeacherDashboardPage() {
  const { user } = useAuth();
  const [exams, setExams] = useState<Exam[]>([]);
  const [loading, setLoading] = useState(true);
  const navigate = useNavigate();

  useEffect(() => {
    examsApi.getAll()
      .then(setExams)
      .catch(err => console.error('Failed to load exams:', err))
      .finally(() => setLoading(false));
  }, []);

  const upcoming = exams.filter(e => e.status === 'Scheduled');
  const active = exams.filter(e => e.status === 'Active');
  const ended = exams.filter(e => e.status === 'Ended');

  const todayStr = new Date().toLocaleDateString('en-US', {
    weekday: 'long',
    year: 'numeric',
    month: 'long',
    day: 'numeric'
  });

  return (
    <AppLayout title="Dashboard">
      <div className="max-w-7xl mx-auto space-y-8">
        {/* Greeting & Header */}
        <div>
          <h2 className="text-2xl font-bold text-slate-800 mb-1">Good morning, Dr. {user?.name || 'Ahmed'}</h2>
          <p className="text-sm text-slate-500">{todayStr}</p>
        </div>

        {loading ? (
          <div className="min-h-[300px] flex items-center justify-center">
            <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin" />
          </div>
        ) : (
          <>
            {/* Stats Grid */}
            <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
              {/* Total Exams */}
              <div className="bg-white border border-slate-200 p-6 rounded-xl flex items-center justify-between shadow-sm">
                <div>
                  <p className="text-xs font-bold tracking-wider text-slate-400 uppercase mb-1">Total Exams</p>
                  <h3 className="text-3xl font-bold text-slate-800">{exams.length}</h3>
                </div>
                <div className="w-12 h-12 bg-slate-50 rounded-lg flex items-center justify-center text-primary-500">
                  <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" />
                  </svg>
                </div>
              </div>

              {/* Active Now */}
              <div className="bg-white border border-green-100 p-6 rounded-xl flex items-center justify-between shadow-sm bg-green-50/10">
                <div>
                  <div className="flex items-center gap-2 mb-1">
                    <span className="text-xs font-bold tracking-wider text-green-700 uppercase">Active Now</span>
                    {active.length > 0 && (
                      <span className="w-2.5 h-2.5 bg-green-500 rounded-full animate-ping"></span>
                    )}
                  </div>
                  <h3 className="text-3xl font-bold text-green-800">{active.length}</h3>
                </div>
                <div className="w-12 h-12 bg-green-50 rounded-lg flex items-center justify-center text-green-600">
                  <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M5.636 18.364a9 9 0 010-12.728m12.728 0a9 9 0 010 12.728m-9.9-2.829a5 5 0 010-7.07m7.07 0a5 5 0 010 7.07M13 12a1 1 0 11-2 0 1 1 0 012 0z" />
                  </svg>
                </div>
              </div>

              {/* Pending Reviews */}
              <div className="bg-white border border-orange-100 p-6 rounded-xl flex items-center justify-between shadow-sm">
                <div>
                  <p className="text-xs font-bold tracking-wider text-orange-700 uppercase mb-1">Pending Reviews</p>
                  <h3 className="text-3xl font-bold text-orange-800">{ended.length}</h3>
                </div>
                <div className="w-12 h-12 bg-orange-50 rounded-lg flex items-center justify-center text-orange-600">
                  <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                  </svg>
                </div>
              </div>
            </div>

            {/* Main Content Layout */}
            <div className="grid grid-cols-1 lg:grid-cols-12 gap-8">
              {/* Upcoming Exams (Wide Column) */}
              <div className="lg:col-span-8">
                <div className="bg-white border border-slate-200 rounded-xl shadow-sm overflow-hidden">
                  <div className="px-6 py-4 border-b border-slate-200 flex justify-between items-center">
                    <h4 className="text-base font-bold text-slate-800">Scheduled Exams</h4>
                  </div>
                  <div className="divide-y divide-slate-100">
                    {upcoming.length === 0 ? (
                      <div className="p-8 text-center text-sm text-slate-400">
                        No upcoming exams scheduled.
                      </div>
                    ) : (
                      upcoming.map((exam) => (
                        <div key={exam.examId} className="p-6 flex items-center justify-between hover:bg-slate-50/50 transition-all border-l-4 border-primary-500">
                          <div className="flex-1">
                            <div className="flex items-center gap-3 mb-2">
                              <h5 className="text-sm font-bold text-slate-800">{exam.title}</h5>
                              <span className="px-2 py-0.5 rounded-full bg-slate-100 text-slate-600 text-[10px] font-bold uppercase tracking-wider">{exam.sectionName}</span>
                            </div>
                            <div className="grid grid-cols-1 sm:grid-cols-3 gap-2 text-slate-400">
                              <div className="flex items-center gap-1.5 text-xs">
                                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
                                </svg>
                                <span>{new Date(exam.startTime).toLocaleString('en-US', { dateStyle: 'short', timeStyle: 'short' })}</span>
                              </div>
                              <div className="flex items-center gap-1.5 text-xs">
                                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
                                </svg>
                                <span>{exam.durationMinutes} Mins</span>
                              </div>
                              <div className="flex items-center gap-1.5 text-xs">
                                <span className="px-1.5 py-0.5 rounded text-[10px] font-bold bg-blue-100 text-blue-700 uppercase">Ready</span>
                              </div>
                            </div>
                          </div>
                          <button 
                            onClick={() => navigate(`/teacher/eligibility?examId=${exam.examId}`)}
                            className="ml-4 px-4 py-2 bg-blue-50 hover:bg-blue-100 text-primary-600 text-xs font-bold rounded-lg transition-all"
                          >
                            Manage
                          </button>
                        </div>
                      ))
                    )}
                  </div>
                </div>
              </div>

              {/* Needs Review / Completed Exams (Side Column) */}
              <div className="lg:col-span-4">
                <div className="bg-white border border-slate-200 rounded-xl shadow-sm overflow-hidden h-full">
                  <div className="px-6 py-4 border-b border-slate-200 bg-orange-50/30">
                    <h4 className="text-sm font-bold text-orange-900 flex items-center gap-2">
                      <svg className="w-5 h-5 text-orange-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-6 9l2 2 4-4" />
                      </svg>
                      Needs Your Review
                    </h4>
                  </div>
                  <div className="p-6 space-y-6">
                    {ended.length === 0 ? (
                      <div className="text-center text-xs text-slate-400 py-6 italic">
                        No ended exams pending grade review.
                      </div>
                    ) : (
                      ended.map((exam) => (
                        <div key={exam.examId} className="relative space-y-3">
                          <div>
                            <p className="text-[10px] font-bold tracking-wider text-slate-400 uppercase">{exam.courseName}</p>
                            <h5 className="text-sm font-bold text-slate-700 mt-0.5">{exam.title}</h5>
                          </div>
                          <button
                            onClick={() => navigate(`/teacher/results/${exam.examId}`)}
                            className="w-full py-2 bg-orange-500 text-white text-xs font-bold rounded-lg hover:bg-orange-600 shadow-sm transition-all"
                          >
                            Review Grades
                          </button>
                        </div>
                      ))
                    )}
                  </div>
                </div>
              </div>
            </div>
          </>
        )}
      </div>
    </AppLayout>
  );
}
