import { useState, useEffect } from 'react';
import AppLayout from '../../components/layout/AppLayout';
import { usersApi } from '../../api/users.api';
import type { User } from '../../types';

export default function DeviceBindingsPage() {
  const [students, setStudents] = useState<User[]>([]);
  const [search, setSearch] = useState('');
  const [showUnboundOnly, setShowUnboundOnly] = useState(false);
  const [confirmStudent, setConfirmStudent] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);

  const loadStudents = async () => {
    setLoading(true);
    try {
      const data = await usersApi.getAll('Student');
      setStudents(data);
    } catch (err) {
      console.error('Failed to load students:', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadStudents();
  }, []);

  const handleResetBinding = async () => {
    if (!confirmStudent) return;
    try {
      await usersApi.resetDeviceBinding(confirmStudent.userId);
      setConfirmStudent(null);
      alert('Binding reset successfully.');
      loadStudents();
    } catch (err) {
      console.error(err);
      alert('Failed to reset binding.');
    }
  };

  const handleForceLogout = async (userId: string) => {
    try {
      await usersApi.forceLogout(userId);
      alert('Active sessions closed successfully.');
      loadStudents();
    } catch (err) {
      console.error(err);
      alert('Failed to close active sessions.');
    }
  };

  const filteredStudents = students.filter((s) => {
    const matchesSearch = 
      s.name.toLowerCase().includes(search.toLowerCase()) ||
      s.email.toLowerCase().includes(search.toLowerCase());
    const matchesToggle = !showUnboundOnly || !s.deviceBound;
    return matchesSearch && matchesToggle;
  });

  return (
    <AppLayout title="Device Bindings">
      <div className="space-y-6">
        {/* Info Banner */}
        <div className="bg-[#EFF6FF] border-l-4 border-primary-500 p-4 rounded-r-lg flex items-start gap-4">
          <svg className="w-5 h-5 text-primary-500 flex-shrink-0 mt-0.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          <div>
            <p className="text-sm text-primary-700 leading-relaxed">
              Device binding locks each student account to a unique hardware ID upon their first login. This prevents unauthorized account sharing and ensures exam integrity. Admins can reset bindings for students who have genuine hardware changes or login issues.
            </p>
          </div>
        </div>

        {/* Controls Bar */}
        <div className="flex flex-col sm:flex-row items-center justify-between gap-4">
          <div className="relative w-full max-w-md">
            <svg className="w-5 h-5 absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
            </svg>
            <input
              className="w-full h-10 pl-10 pr-4 rounded-lg border border-slate-200 bg-white focus:ring-2 focus:ring-primary-500 focus:border-transparent text-sm outline-none transition-all"
              placeholder="Search by student name or email..."
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>
          <div className="flex items-center gap-3">
            <span className="text-sm text-slate-500 font-medium">Show unbound only</span>
            <button
              onClick={() => setShowUnboundOnly(!showUnboundOnly)}
              className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none ${
                showUnboundOnly ? 'bg-primary-500' : 'bg-slate-200'
              }`}
            >
              <span
                className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
                  showUnboundOnly ? 'translate-x-6' : 'translate-x-1'
                }`}
              />
            </button>
          </div>
        </div>

        {/* Table Card */}
        {loading ? (
          <div className="min-h-[300px] flex items-center justify-center">
            <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin" />
          </div>
        ) : (
          <div className="bg-white border border-slate-200 rounded-xl overflow-hidden shadow-sm">
            <div className="overflow-x-auto">
              <table className="w-full text-left border-collapse">
                <thead>
                  <tr className="bg-slate-50 border-b border-slate-200">
                    <th className="px-6 py-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Student Name</th>
                    <th className="px-6 py-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Email</th>
                    <th className="px-6 py-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Binding Status</th>
                    <th className="px-6 py-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Registered Date</th>
                    <th className="px-6 py-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {filteredStudents.length === 0 ? (
                    <tr>
                      <td colSpan={5} className="px-6 py-12 text-center text-sm text-slate-400">
                        No student bindings match the current parameters.
                      </td>
                    </tr>
                  ) : (
                    filteredStudents.map((student) => (
                      <tr key={student.userId} className="hover:bg-slate-50/50 transition-colors">
                        <td className="px-6 py-4">
                          <div className="flex items-center gap-3">
                            <div className="w-8 h-8 rounded-full bg-blue-50 flex items-center justify-center text-primary-500 font-bold text-xs">
                              {student.name.split(' ').map((n) => n[0]).join('')}
                            </div>
                            <span className="text-sm font-semibold text-slate-700">{student.name}</span>
                          </div>
                        </td>
                        <td className="px-6 py-4 text-sm text-slate-500">{student.email}</td>
                        <td className="px-6 py-4">
                          {student.deviceBound ? (
                            <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold bg-green-50 border border-green-200 text-green-700">
                              <span className="w-1.5 h-1.5 rounded-full bg-green-500 mr-1.5"></span>
                              Bound
                            </span>
                          ) : (
                            <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-slate-50 border border-slate-200 text-slate-400">
                              <span className="w-1.5 h-1.5 rounded-full bg-slate-300 mr-1.5"></span>
                              Unbound
                            </span>
                          )}
                        </td>
                        <td className="px-6 py-4 text-sm text-slate-500">
                          {student.deviceRegisteredAt ? (
                            new Date(student.deviceRegisteredAt).toLocaleDateString('en-US', {
                              month: 'short',
                              day: 'numeric',
                              year: 'numeric'
                            })
                          ) : (
                            'N/A'
                          )}
                        </td>
                        <td className="px-6 py-4">
                          <div className="flex items-center gap-2">
                            {student.deviceBound && (
                              <>
                                <button
                                  onClick={() => setConfirmStudent(student)}
                                  className="px-3 py-1.5 text-xs font-bold text-red-600 border border-red-200 rounded hover:bg-red-50 transition-all"
                                >
                                  Reset Binding
                                </button>
                                <button
                                  onClick={() => handleForceLogout(student.userId)}
                                  className="px-3 py-1.5 text-xs font-semibold text-slate-600 border border-slate-200 rounded hover:bg-slate-50 transition-all"
                                >
                                  Force Logout
                                </button>
                              </>
                            )}
                          </div>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>

      {/* Confirmation Modal */}
      {confirmStudent && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-sm">
          <div className="bg-white w-[420px] rounded-xl shadow-xl overflow-hidden p-8 flex flex-col items-center text-center">
            <div className="w-16 h-16 bg-orange-50 rounded-full flex items-center justify-center mb-6 text-orange-500">
              <svg className="w-10 h-10" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
              </svg>
            </div>
            <h2 className="text-lg font-bold text-slate-800 mb-2">Reset Device Binding?</h2>
            <p className="text-sm text-slate-500 mb-8 leading-relaxed">
              This will remove <span className="font-semibold text-slate-700">{confirmStudent.name}</span>'s unique device identification lock. The student will be prompted to bind their primary device upon their next login. This action is logged.
            </p>
            <div className="flex w-full gap-3">
              <button
                className="flex-1 h-10 border border-slate-200 text-slate-500 font-semibold text-sm rounded-lg hover:bg-slate-50 transition-all"
                onClick={() => setConfirmStudent(null)}
              >
                Cancel
              </button>
              <button
                className="flex-1 h-10 bg-red-600 text-white font-semibold text-sm rounded-lg hover:bg-red-700 transition-all shadow-sm"
                onClick={handleResetBinding}
              >
                Reset Binding
              </button>
            </div>
          </div>
        </div>
      )}
    </AppLayout>
  );
}
