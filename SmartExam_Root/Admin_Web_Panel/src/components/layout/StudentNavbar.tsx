import { NavLink, useNavigate } from 'react-router-dom';
import { Bell, LogOut, Shield } from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import { useState, useEffect } from 'react';
import { studentApi } from '../../api/student.api';

export default function StudentNavbar() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const [unreadCount, setUnreadCount] = useState(0);

  const loadUnread = () => {
    studentApi.getNotifications()
      .then(data => setUnreadCount(data.unreadCount))
      .catch(() => {});
  };

  useEffect(() => {
    loadUnread();
    const interval = setInterval(loadUnread, 30000);
    return () => clearInterval(interval);
  }, []);

  const navLinks = [
    { label: 'Dashboard',    path: '/student/dashboard' },
    { label: 'My Exams',     path: '/student/exams' },
    { label: 'Performance',  path: '/student/performance' },
    { label: 'Violations',   path: '/student/violations' },
    { label: 'Notifications', path: '/student/notifications' },
  ];

  const initials = user?.name ? user.name.split(' ').map(n => n[0]).join('').toUpperCase().slice(0, 2) : 'ST';

  return (
    <nav className="bg-white border-b border-slate-200 sticky top-0 z-40 shadow-sm">
      <div className="max-w-[1100px] mx-auto px-6 h-16 flex items-center justify-between">
        {/* Logo */}
        <div className="flex items-center gap-2 cursor-pointer" onClick={() => navigate('/student/dashboard')}>
          <Shield size={22} className="text-primary-500" fill="currentColor" />
          <span className="font-bold text-slate-900">SmartExam</span>
          <span className="text-xs text-slate-400 font-semibold ml-1 uppercase tracking-wider">Student</span>
        </div>

        {/* Nav links */}
        <div className="flex items-center gap-1">
          {navLinks.map(link => (
            <NavLink
              key={link.path}
              to={link.path}
              className={({ isActive }) =>
                `px-4 py-2 text-sm rounded-md transition-colors relative font-medium
                ${isActive
                  ? 'text-primary-600 bg-primary-50'
                  : 'text-slate-500 hover:text-slate-700 hover:bg-slate-50'
                }`
              }
            >
              {link.label}
              {link.label === 'Notifications' && unreadCount > 0 && (
                <span className="absolute -top-1 -right-1 bg-red-500 text-white text-[9px] rounded-full w-4 h-4 flex items-center justify-center font-bold">
                  {unreadCount}
                </span>
              )}
            </NavLink>
          ))}
        </div>

        {/* Right side */}
        <div className="flex items-center gap-4">
          <button
            onClick={() => navigate('/student/notifications')}
            className="relative p-2 text-slate-400 hover:text-slate-600 focus:outline-none"
          >
            <Bell size={18} />
            {unreadCount > 0 && (
              <span className="absolute top-1 right-1 w-2 h-2 bg-red-500 rounded-full" />
            )}
          </button>
          
          <div className="flex items-center gap-3">
            <div className="w-8 h-8 rounded-full bg-blue-100 text-primary-600 border border-slate-200 text-xs font-bold flex items-center justify-center">
              {initials}
            </div>
            <span className="text-sm font-semibold text-slate-700 hidden sm:inline">{user?.name}</span>
          </div>

          <button 
            onClick={logout} 
            className="p-2 text-slate-400 hover:text-red-500 transition-colors focus:outline-none"
            title="Log Out"
          >
            <LogOut size={16} />
          </button>
        </div>
      </div>
    </nav>
  );
}
