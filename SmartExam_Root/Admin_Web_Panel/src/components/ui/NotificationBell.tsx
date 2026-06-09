import { useState, useEffect, useRef } from 'react';
import { Bell } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { notificationsApi } from '../../api/notifications.api';
import type { NotificationsResponse } from '../../types';
import { formatDistanceToNow } from 'date-fns';

const typeColor = (type: string) => ({
  'ExamScheduled':    'bg-blue-500',
  'GradeReleased':    'bg-green-500',
  'ViolationWarning': 'bg-red-500',
  'EligibilityChanged': 'bg-orange-500',
  'Announcement':     'bg-purple-500',
})[type] ?? 'bg-slate-400';

export default function NotificationBell() {
  const [open, setOpen] = useState(false);
  const [data, setData] = useState<NotificationsResponse | null>(null);
  const ref = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();

  const loadNotifications = () => {
    notificationsApi.getAll().then(setData).catch(() => {});
  };

  useEffect(() => {
    loadNotifications();
    // Refresh notifications count periodically
    const interval = setInterval(loadNotifications, 30000);
    return () => clearInterval(interval);
  }, []);

  // Close dropdown when clicking outside
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, []);

  const handleMarkAll = async () => {
    try {
      await notificationsApi.markAllRead();
      loadNotifications();
    } catch (err) {
      console.error(err);
    }
  };

  const handleMarkOne = async (id: string) => {
    try {
      await notificationsApi.markRead(id);
      loadNotifications();
    } catch (err) {
      console.error(err);
    }
  };

  const handleViewAll = () => {
    setOpen(false);
    // Determine target page based on role cached in localstorage
    const u = localStorage.getItem('smartexam_user');
    if (u) {
      const userObj = JSON.parse(u);
      if (userObj.role === 'Student') {
        navigate('/student/notifications');
        return;
      } else if (userObj.role === 'Teacher') {
        navigate('/teacher/notifications');
        return;
      }
    }
    navigate('/admin/notifications');
  };

  return (
    <div className="relative" ref={ref}>
      {/* Bell button */}
      <button
        onClick={() => setOpen(o => !o)}
        className="relative w-10 h-10 flex items-center justify-center text-slate-500 hover:text-primary-500 hover:bg-slate-50 rounded-full transition-colors focus:outline-none"
      >
        <Bell size={20} />
        {data && data.unreadCount > 0 && (
          <span className="absolute top-1 right-1 w-4 h-4 bg-red-500 text-white text-[9px] rounded-full flex items-center justify-center font-bold">
            {data.unreadCount > 9 ? '9+' : data.unreadCount}
          </span>
        )}
      </button>

      {/* Dropdown panel */}
      {open && (
        <div className="absolute right-0 top-full mt-2 w-96 bg-white border border-slate-200 rounded-lg shadow-xl z-50 overflow-hidden">
          {/* Header */}
          <div className="flex items-center justify-between px-4 py-3 border-b border-slate-100 bg-slate-50">
            <span className="font-semibold text-slate-900 text-sm">Notifications</span>
            {data && data.unreadCount > 0 && (
              <button onClick={handleMarkAll} className="text-xs text-primary-600 hover:text-primary-700 font-medium">
                Mark all read
              </button>
            )}
          </div>

          {/* List */}
          <div className="max-h-90 overflow-y-auto divide-y divide-slate-100 max-h-80">
            {!data || data.notifications.length === 0 ? (
              <div className="py-12 text-center text-slate-400 text-sm">No notifications</div>
            ) : (
              data.notifications.slice(0, 8).map(n => (
                <div
                  key={n.notificationId}
                  onClick={() => !n.isRead && handleMarkOne(n.notificationId)}
                  className={`flex gap-3 px-4 py-3 cursor-pointer hover:bg-slate-50/80 transition-colors
                    ${!n.isRead ? 'bg-blue-50/20' : ''}`}
                >
                  <span className={`w-2 h-2 rounded-full mt-1.5 flex-shrink-0 ${typeColor(n.type)}`} />
                  <div className="flex-1 min-w-0">
                    <p className={`text-sm ${!n.isRead ? 'font-semibold text-slate-900' : 'font-medium text-slate-700'}`}>
                      {n.title}
                    </p>
                    <p className="text-xs text-slate-500 mt-0.5 leading-normal">{n.body}</p>
                    <p className="text-[10px] text-slate-400 mt-1">
                      {formatDistanceToNow(new Date(n.createdAt), { addSuffix: true })}
                    </p>
                  </div>
                  {!n.isRead && <span className="w-1.5 h-1.5 bg-primary-500 rounded-full flex-shrink-0 mt-2" />}
                </div>
              ))
            )}
          </div>

          {/* Footer */}
          <div className="px-4 py-3 border-t border-slate-100 text-center bg-slate-50">
            <button onClick={handleViewAll} className="text-xs text-primary-600 hover:text-primary-700 font-semibold">
              View all notifications →
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
