import { useAuth } from '../../context/AuthContext';

export default function Navbar({ title }: { title: string }) {
  const { user } = useAuth();
  
  return (
    <header className="h-[56px] w-full bg-white border-b border-slate-200 flex justify-between items-center px-8 z-40 shrink-0">
      <div className="flex items-center">
        <h1 className="text-lg font-bold text-slate-800">{title}</h1>
      </div>
      
      <div className="flex items-center gap-4">
        {/* Notification Bell */}
        <button className="w-10 h-10 flex items-center justify-center text-slate-500 hover:text-primary-500 hover:bg-slate-50 rounded-full transition-colors">
          <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9" />
          </svg>
        </button>

        {/* User avatar and name info */}
        <div className="flex items-center gap-3">
          <div className="text-right hidden sm:block">
            <p className="text-xs font-semibold text-slate-700 leading-none">{user?.name}</p>
            <p className="text-[10px] text-slate-400 leading-none mt-0.5">{user?.role}</p>
          </div>
          <div className="w-8 h-8 rounded-full bg-slate-100 border border-slate-200 overflow-hidden flex items-center justify-center font-bold text-slate-600">
            {user?.name ? user.name[0].toUpperCase() : 'U'}
          </div>
        </div>
      </div>
    </header>
  );
}
