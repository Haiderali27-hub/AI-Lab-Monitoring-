import { NavLink } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';

export default function TeacherSidebar() {
  const { logout, user } = useAuth();

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
        <NavLink to="/teacher/dashboard" className={getLinkClass}>
          <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M4 6a2 2 0 012-2h2a2 2 0 012 2v4a2 2 0 01-2 2H6a2 2 0 01-2-2V6zM14 6a2 2 0 012-2h2a2 2 0 012 2v4a2 2 0 01-2 2h-2a2 2 0 01-2-2V6zM4 16a2 2 0 012-2h2a2 2 0 012 2v4a2 2 0 01-2 2H6a2 2 0 01-2-2v-4zM14 16a2 2 0 012-2h2a2 2 0 012 2v4a2 2 0 01-2 2h-2a2 2 0 01-2-2v-4z" />
          </svg>
          <span className="text-xs font-semibold tracking-wider uppercase">Dashboard</span>
        </NavLink>
        
        <NavLink to="/teacher/create-exam" className={getLinkClass}>
          <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 9v3m0 0v3m0-3h3m-3 0H9m12 0a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          <span className="text-xs font-semibold tracking-wider uppercase">Create Exam</span>
        </NavLink>
        
        <NavLink to="/teacher/live-monitor" className={getLinkClass}>
          <svg className="w-5 h-5 text-slate-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M5.636 18.364a9 9 0 010-12.728m12.728 0a9 9 0 010 12.728m-9.9-2.829a5 5 0 010-7.07m7.07 0a5 5 0 010 7.07M13 12a1 1 0 11-2 0 1 1 0 012 0z" />
          </svg>
          <span className="text-xs font-semibold tracking-wider uppercase">Live Monitor</span>
        </NavLink>
        
        <NavLink to="/teacher/eligibility" className={getLinkClass}>
          <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          <span className="text-xs font-semibold tracking-wider uppercase">Eligibility</span>
        </NavLink>
      </nav>

      {/* User Info & Logout Footer */}
      <div className="mt-auto px-4 pt-4 border-t border-slate-100 flex flex-col gap-4">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-full bg-blue-100 text-primary-600 font-bold flex items-center justify-center">
            {user?.name ? user.name.split(' ').map(n => n[0]).join('') : 'TE'}
          </div>
          <div className="overflow-hidden">
            <h4 className="text-sm font-semibold text-slate-800 truncate">{user?.name || 'Teacher User'}</h4>
            <p className="text-xs text-slate-400 truncate">{user?.email || 'teacher@university.edu'}</p>
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
