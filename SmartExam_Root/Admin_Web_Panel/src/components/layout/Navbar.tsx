import { useState, useRef, useEffect } from 'react';
import { useAuth } from '../../context/AuthContext';
import { authApi } from '../../api/auth.api';
import NotificationBell from '../ui/NotificationBell';

export default function Navbar({ title }: { title: string }) {
  const { user, logout } = useAuth();
  const [menuOpen, setMenuOpen] = useState(false);
  const [showPwModal, setShowPwModal] = useState(false);
  const menuRef = useRef<HTMLDivElement | null>(null);

  // Change-password modal state
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [pwError, setPwError] = useState('');
  const [pwSuccess, setPwSuccess] = useState('');
  const [pwLoading, setPwLoading] = useState(false);

  useEffect(() => {
    const onClick = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false);
    };
    document.addEventListener('mousedown', onClick);
    return () => document.removeEventListener('mousedown', onClick);
  }, []);

  const openPwModal = () => {
    setMenuOpen(false);
    setCurrent(''); setNext(''); setConfirm(''); setPwError(''); setPwSuccess('');
    setShowPwModal(true);
  };

  const submitPassword = async (e: React.FormEvent) => {
    e.preventDefault();
    setPwError(''); setPwSuccess('');
    if (next.length < 8) { setPwError('New password must be at least 8 characters.'); return; }
    if (next !== confirm) { setPwError('New passwords do not match.'); return; }
    setPwLoading(true);
    try {
      await authApi.changePassword(current, next);
      setPwSuccess('Password updated successfully.');
      setCurrent(''); setNext(''); setConfirm('');
    } catch (err: any) {
      setPwError(err.response?.data?.message || 'Failed to change password.');
    } finally {
      setPwLoading(false);
    }
  };

  return (
    <header className="h-[56px] w-full bg-white/80 backdrop-blur-md border-b border-slate-200/70 shadow-sm flex justify-between items-center px-8 z-40 shrink-0">
      <div className="flex items-center">
        <h1 className="text-lg font-bold text-slate-800 tracking-tight">{title}</h1>
      </div>

      <div className="flex items-center gap-4">
        <NotificationBell />

        {/* User avatar dropdown */}
        <div className="relative" ref={menuRef}>
          <button
            onClick={() => setMenuOpen(o => !o)}
            className="flex items-center gap-3 rounded-full hover:bg-slate-100 pl-2 pr-1 py-1 transition-colors"
          >
            <div className="text-right hidden sm:block">
              <p className="text-xs font-semibold text-slate-700 leading-none">{user?.name}</p>
              <p className="text-[10px] text-slate-400 leading-none mt-0.5">{user?.role}</p>
            </div>
            <div className="w-9 h-9 rounded-full bg-gradient-to-br from-primary-500 to-primary-700 text-white overflow-hidden flex items-center justify-center text-sm font-bold shadow-sm">
              {user?.name ? user.name[0].toUpperCase() : 'U'}
            </div>
          </button>

          {menuOpen && (
            <div className="absolute right-0 top-12 w-48 bg-white border border-slate-200 rounded-lg shadow-lg py-1 z-50">
              <div className="px-4 py-2 border-b border-slate-100">
                <p className="text-xs font-semibold text-slate-700 truncate">{user?.email}</p>
              </div>
              <button
                onClick={openPwModal}
                className="w-full text-left px-4 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-50"
              >
                Change Password
              </button>
              <button
                onClick={logout}
                className="w-full text-left px-4 py-2 text-xs font-semibold text-red-600 hover:bg-red-50"
              >
                Sign Out
              </button>
            </div>
          )}
        </div>
      </div>

      {/* Change Password Modal */}
      {showPwModal && (
        <div className="fixed inset-0 bg-slate-900/40 backdrop-blur-sm z-[80] flex items-center justify-center p-4"
             onClick={() => setShowPwModal(false)}>
          <div className="bg-white rounded-2xl shadow-2xl w-full max-w-md" onClick={e => e.stopPropagation()}>
            <div className="p-6 border-b border-slate-200 flex items-center justify-between">
              <h2 className="text-lg font-bold text-slate-800">Change Password</h2>
              <button onClick={() => setShowPwModal(false)}
                      className="w-9 h-9 flex items-center justify-center rounded-full hover:bg-slate-100 text-slate-500">
                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            </div>
            <form onSubmit={submitPassword} className="p-6 space-y-4">
              {pwError && <div className="p-3 bg-red-50 border border-red-200 text-red-600 text-xs rounded-lg">{pwError}</div>}
              {pwSuccess && <div className="p-3 bg-green-50 border border-green-200 text-green-700 text-xs rounded-lg">{pwSuccess}</div>}
              <div className="space-y-1.5">
                <label className="text-xs font-semibold tracking-wider text-slate-400 uppercase">Current Password</label>
                <input type="password" value={current} onChange={e => setCurrent(e.target.value)} required
                       className="w-full h-10 px-4 border border-slate-200 rounded-lg focus:ring-2 focus:ring-primary-500 focus:border-transparent outline-none text-sm" />
              </div>
              <div className="space-y-1.5">
                <label className="text-xs font-semibold tracking-wider text-slate-400 uppercase">New Password</label>
                <input type="password" value={next} onChange={e => setNext(e.target.value)} required
                       placeholder="Min 8 characters"
                       className="w-full h-10 px-4 border border-slate-200 rounded-lg focus:ring-2 focus:ring-primary-500 focus:border-transparent outline-none text-sm" />
              </div>
              <div className="space-y-1.5">
                <label className="text-xs font-semibold tracking-wider text-slate-400 uppercase">Confirm New Password</label>
                <input type="password" value={confirm} onChange={e => setConfirm(e.target.value)} required
                       className="w-full h-10 px-4 border border-slate-200 rounded-lg focus:ring-2 focus:ring-primary-500 focus:border-transparent outline-none text-sm" />
              </div>
              <div className="flex justify-end gap-3 pt-2">
                <button type="button" onClick={() => setShowPwModal(false)}
                        className="px-5 h-10 border border-slate-200 text-slate-500 font-semibold text-sm rounded-lg hover:bg-slate-100">
                  Close
                </button>
                <button type="submit" disabled={pwLoading}
                        className="px-5 h-10 bg-primary-500 text-white font-semibold text-sm rounded-lg hover:bg-primary-600 shadow-sm disabled:opacity-50 flex items-center gap-2">
                  {pwLoading && <div className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />}
                  Update Password
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </header>
  );
}
