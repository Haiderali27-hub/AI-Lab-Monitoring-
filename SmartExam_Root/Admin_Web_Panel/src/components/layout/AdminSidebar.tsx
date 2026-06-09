import { NavLink } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { notificationsApi } from '../../api/notifications.api';
import { useEffect, useState } from 'react';

export default function AdminSidebar() {
  const { logout, user } = useAuth();
  const [unreadCount, setUnreadCount] = useState(0);

  useEffect(() => {
    notificationsApi.getAll()
      .then(data => setUnreadCount(data.unreadCount))
      .catch(() => {});
  }, []);

  const getLinkClass = ({ isActive }: { isActive: boolean }) => {
    const base = "flex items-center gap-3 font-medium px-4 py-3 transition-all duration-200 ";
    const active = "border-l-4 border-primary-500 text-primary-500 bg-blue-50/50 ";
    const inactive = "text-slate-500 hover:text-slate-800 hover:bg-slate-50 ";
    return base + (isActive ? active : inactive);
  };

  return (
    <aside className="w-[240px] h-screen bg-white border-r border-slate-200 flex flex-col py-8 shrink-0">
      {/* Brand Header */}
      <div className="px-6 mb-10 flex items-center gap-2 text-lg font-bold text-slate-800">
        <svg className="w-6 h-6 text-primary-500" fill="currentColor" viewBox="0 0 20 20">
          <path fillRule="evenodd" d="M10 1.944A11.954 11.954 0 012.166 5C2.056 5.649 2 6.319 2 7c0 5.224 3.08 9.711 7.5 11.77a11.956 11.956 0 007.5-11.77c0-.681-.056-1.351-.166-2A11.954 11.954 0 0110 1.944z" clipRule="evenodd" />
        </svg>
        <span>SmartExam</span>
      </div>

      {/* Navigation */}
      <nav className="flex-1 space-y-1">
        <NavLink to="/admin/dashboard" className={getLinkClass}>
          <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M4 6a2 2 0 012-2h2a2 2 0 012 2v4a2 2 0 01-2 2H6a2 2 0 01-2-2V6zM14 6a2 2 0 012-2h2a2 2 0 012 2v4a2 2 0 01-2 2h-2a2 2 0 01-2-2V6zM4 16a2 2 0 012-2h2a2 2 0 012 2v4a2 2 0 01-2 2H6a2 2 0 01-2-2v-4zM14 16a2 2 0 012-2h2a2 2 0 012 2v4a2 2 0 01-2 2h-2a2 2 0 01-2-2v-4z" />
          </svg>
          <span className="text-xs font-semibold tracking-wider uppercase">Dashboard</span>
        </NavLink>
        
        <NavLink to="/admin/users" className={getLinkClass}>
          <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z" />
          </svg>
          <span className="text-xs font-semibold tracking-wider uppercase">Users</span>
        </NavLink>
        
        <NavLink to="/admin/labs" className={getLinkClass}>
          <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5m0 0V11m0 0h4m-4 0H7m12 0h-3" />
          </svg>
          <span className="text-xs font-semibold tracking-wider uppercase">Labs & Workstations</span>
        </NavLink>
        
        <NavLink to="/admin/device-bindings" className={getLinkClass}>
          <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M13.828 10.172a4 4 0 00-5.656 0l-4 4a4 4 0 105.656 5.656l1.102-1.101m-.758-4.899a4 4 0 005.656 0l4-4a4 4 0 00-5.656-5.656l-1.1 1.1" />
          </svg>
          <span className="text-xs font-semibold tracking-wider uppercase">Device Bindings</span>
        </NavLink>

        <NavLink to="/admin/analytics" className={getLinkClass}>
          <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z" />
          </svg>
          <span className="text-xs font-semibold tracking-wider uppercase">Analytics</span>
        </NavLink>

        <NavLink to="/admin/notifications" className={getLinkClass}>
          <div className="relative flex items-center">
            <svg className="w-5 h-5 mr-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9" />
            </svg>
            {unreadCount > 0 && (
              <span className="absolute left-2.5 -top-1.5 bg-red-500 text-white text-[8px] rounded-full w-3.5 h-3.5 flex items-center justify-center font-bold">
                {unreadCount}
              </span>
            )}
            <span className="text-xs font-semibold tracking-wider uppercase">Notifications</span>
          </div>
        </NavLink>
        
        <NavLink to="/admin/audit-logs" className={getLinkClass}>
          <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          <span className="text-xs font-semibold tracking-wider uppercase">Audit Logs</span>
        </NavLink>
      </nav>

      {/* User Info & Logout Footer */}
      <div className="mt-auto px-4 pt-4 border-t border-slate-100 flex flex-col gap-4">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-full bg-blue-100 text-primary-600 font-bold flex items-center justify-center">
            {user?.name ? user.name.split(' ').map(n => n[0]).join('') : 'AD'}
          </div>
          <div className="overflow-hidden">
            <h4 className="text-sm font-semibold text-slate-800 truncate">{user?.name || 'Admin User'}</h4>
            <p className="text-xs text-slate-400 truncate">{user?.email || 'admin@university.edu'}</p>
          </div>
        </div>
        
        <button 
          onClick={logout} 
          className="flex items-center gap-3 text-red-500 hover:text-red-700 hover:bg-red-50 px-4 py-3 transition-colors rounded-lg font-medium"
        >
          <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M17 16l4-4m0 0l-4-4m4 4H7m6 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1" />
          </svg>
          <span className="text-xs font-semibold tracking-wider uppercase">Logout</span>
        </button>
      </div>
    </aside>
  );
}

