import { useState, useEffect } from 'react';
import AppLayout from '../../components/layout/AppLayout';
import { usersApi } from '../../api/users.api';
import { examsApi } from '../../api/exams.api';
import type { User, Exam } from '../../types';

export default function DashboardPage() {
  const [users, setUsers] = useState<User[]>([]);
  const [exams, setExams] = useState<Exam[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    Promise.all([usersApi.getAll(), examsApi.getAll()])
      .then(([usersData, examsData]) => {
        setUsers(usersData);
        setExams(examsData);
      })
      .catch(err => console.error('Error fetching dashboard data:', err))
      .finally(() => setLoading(false));
  }, []);

  // Compute stats from real data
  const studentCount = users.filter(u => u.role === 'Student').length;
  const activeExams = exams.filter(e => e.status === 'Active').length;
  const pendingBindingResets = users.filter(u => u.role === 'Student' && !u.deviceBound).length;
  const totalLabs = 4; // Mock value as lab API endpoint isn't fully detailed in dev guide

  const getStatusBadge = (status: Exam['status']) => {
    switch (status) {
      case 'Active':
        return <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-50 border border-green-200 text-green-700">Active</span>;
      case 'Scheduled':
        return <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-blue-50 border border-blue-200 text-primary-600">Scheduled</span>;
      default:
        return <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-slate-50 border border-slate-200 text-slate-500">Ended</span>;
    }
  };

  const todayStr = new Date().toLocaleDateString('en-US', {
    weekday: 'long',
    year: 'numeric',
    month: 'long',
    day: 'numeric'
  });

  return (
    <AppLayout title="Dashboard">
      {/* Header Section */}
      <div className="mb-6 flex justify-between items-end">
        <div>
          <h2 className="text-xl font-bold text-slate-800">Overview</h2>
          <p className="text-sm text-slate-400 mt-1">Today is {todayStr}</p>
        </div>
      </div>

      {loading ? (
        <div className="min-h-[400px] flex items-center justify-center">
          <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin" />
        </div>
      ) : (
        <>
          {/* Stat Cards Grid */}
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6 mb-8">
            {/* Card 1 */}
            <div className="bg-white border border-slate-200 p-5 rounded-xl shadow-sm flex items-center gap-4 hover:border-primary-500/30 transition-colors">
              <div className="w-12 h-12 rounded-full bg-blue-50 flex items-center justify-center text-primary-500">
                <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z" />
                </svg>
              </div>
              <div>
                <p className="text-[10px] font-bold tracking-wider text-slate-400 uppercase">Total Students</p>
                <p className="text-2xl font-bold text-slate-800">{studentCount}</p>
              </div>
            </div>

            {/* Card 2 */}
            <div className="bg-white border border-slate-200 p-5 rounded-xl shadow-sm flex items-center gap-4 hover:border-primary-500/30 transition-colors">
              <div className="w-12 h-12 rounded-full bg-green-50 flex items-center justify-center text-green-600 relative">
                <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2" />
                </svg>
                {activeExams > 0 && (
                  <span className="absolute top-2 right-2 w-2.5 h-2.5 bg-green-500 rounded-full animate-ping"></span>
                )}
              </div>
              <div>
                <p className="text-[10px] font-bold tracking-wider text-slate-400 uppercase">Active Exams</p>
                <p className="text-2xl font-bold text-slate-800">{activeExams}</p>
              </div>
            </div>

            {/* Card 3 */}
            <div className="bg-white border border-slate-200 p-5 rounded-xl shadow-sm flex items-center gap-4 hover:border-primary-500/30 transition-colors">
              <div className="w-12 h-12 rounded-full bg-blue-50 flex items-center justify-center text-primary-500">
                <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5m0 0V11m0 0h4m-4 0H7m12 0h-3" />
                </svg>
              </div>
              <div>
                <p className="text-[10px] font-bold tracking-wider text-slate-400 uppercase">Labs</p>
                <p className="text-2xl font-bold text-slate-800">{totalLabs}</p>
              </div>
            </div>

            {/* Card 4 */}
            <div className="bg-white border border-slate-200 p-5 rounded-xl shadow-sm flex items-center gap-4 hover:border-primary-500/30 transition-colors">
              <div className="w-12 h-12 rounded-full bg-orange-50 flex items-center justify-center text-orange-600">
                <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                </svg>
              </div>
              <div>
                <p className="text-[10px] font-bold tracking-wider text-slate-400 uppercase">Device Resets</p>
                <p className="text-2xl font-bold text-slate-800">{pendingBindingResets}</p>
              </div>
            </div>
          </div>

          {/* Dashboard Main Layout */}
          <div className="grid grid-cols-1 lg:grid-cols-12 gap-8">
            {/* Left Column: Recent Exams */}
            <div className="col-span-1 lg:col-span-8">
              <div className="bg-white border border-slate-200 rounded-xl shadow-sm overflow-hidden">
                <div className="px-6 py-4 border-b border-slate-100 flex justify-between items-center">
                  <h3 className="text-base font-bold text-slate-800">Recent Exams</h3>
                </div>
                <div className="overflow-x-auto">
                  <table className="w-full text-left">
                    <thead>
                      <tr className="bg-slate-50 border-b border-slate-100">
                        <th className="px-6 py-3 text-[10px] font-bold tracking-wider text-slate-400 uppercase">Exam Title</th>
                        <th className="px-6 py-3 text-[10px] font-bold tracking-wider text-slate-400 uppercase">Course</th>
                        <th className="px-6 py-3 text-[10px] font-bold tracking-wider text-slate-400 uppercase">Status</th>
                        <th className="px-6 py-3 text-[10px] font-bold tracking-wider text-slate-400 uppercase">Date</th>
                        <th className="px-6 py-3 text-[10px] font-bold tracking-wider text-slate-400 uppercase">Duration (Min)</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                      {exams.length === 0 ? (
                        <tr>
                          <td colSpan={5} className="px-6 py-8 text-center text-sm text-slate-400">
                            No exams created yet.
                          </td>
                        </tr>
                      ) : (
                        exams.slice(0, 5).map((exam) => (
                          <tr key={exam.examId} className="hover:bg-slate-50/50 transition-colors">
                            <td className="px-6 py-4 text-sm font-semibold text-slate-700">{exam.title}</td>
                            <td className="px-6 py-4 text-sm text-slate-500">{exam.courseName}</td>
                            <td className="px-6 py-4">{getStatusBadge(exam.status)}</td>
                            <td className="px-6 py-4 text-sm text-slate-500">
                              {new Date(exam.startTime).toLocaleDateString('en-US', {
                                month: 'short',
                                day: 'numeric',
                                year: 'numeric'
                              })}
                            </td>
                            <td className="px-6 py-4 text-sm text-slate-500">{exam.durationMinutes}</td>
                          </tr>
                        ))
                      )}
                    </tbody>
                  </table>
                </div>
              </div>
            </div>

            {/* Right Column: Recent Activity / Information */}
            <div className="col-span-1 lg:col-span-4">
              <div className="bg-white border border-slate-200 rounded-xl shadow-sm overflow-hidden">
                <div className="px-6 py-4 border-b border-slate-100">
                  <h3 className="text-base font-bold text-slate-800">Quick Information</h3>
                </div>
                <div className="p-6 space-y-6">
                  {/* Activity Item 1 */}
                  <div className="flex gap-4">
                    <div className="h-10 w-10 shrink-0 rounded-full bg-blue-50 text-primary-500 flex items-center justify-center font-bold text-sm">
                      SYS
                    </div>
                    <div className="flex-1">
                      <p className="text-sm text-slate-700"><span className="font-semibold">SmartExam Live Dashboard</span> connects directly to the C# Backend.</p>
                      <p className="text-xs text-slate-400 mt-0.5">Active Port: 5050</p>
                    </div>
                  </div>
                  {/* Activity Item 2 */}
                  <div className="flex gap-4">
                    <div className="h-10 w-10 shrink-0 rounded-full bg-green-50 text-green-600 flex items-center justify-center font-bold text-sm">
                      HW
                    </div>
                    <div className="flex-1">
                      <p className="text-sm text-slate-700"><span className="font-semibold">Device bindings</span> are active. Students are locked to workstation HWID profiles.</p>
                    </div>
                  </div>
                  {/* Activity Item 3 */}
                  <div className="flex gap-4">
                    <div className="h-10 w-10 shrink-0 rounded-full bg-orange-50 text-orange-600 flex items-center justify-center font-bold text-sm">
                      AI
                    </div>
                    <div className="flex-1">
                      <p className="text-sm text-slate-700"><span className="font-semibold">Plagiarism similarity</span> and AI grading thresholds are active for student C++ code uploads.</p>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </>
      )}
    </AppLayout>
  );
}
