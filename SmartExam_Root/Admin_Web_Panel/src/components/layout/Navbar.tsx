import { useAuth } from '../../context/AuthContext';
import NotificationBell from '../ui/NotificationBell';

export default function Navbar({ title }: { title: string }) {
  const { user } = useAuth();
  
  return (
    <header className="h-[56px] w-full bg-white border-b border-slate-200 flex justify-between items-center px-8 z-40 shrink-0">
      <div className="flex items-center">
        <h1 className="text-lg font-bold text-slate-800">{title}</h1>
      </div>
      
      <div className="flex items-center gap-4">
        {/* Notification Bell */}
        <NotificationBell />


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
