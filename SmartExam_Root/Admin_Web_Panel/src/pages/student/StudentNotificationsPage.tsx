import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import StudentLayout from '../../components/layout/StudentLayout';
import { studentApi } from '../../api/student.api';
import type { NotificationsResponse } from '../../types';
import { useToast } from '../../context/ToastContext';
import { formatDistanceToNow } from 'date-fns';
import { 
  Bell, 
  Calendar, 
  AlertTriangle, 
  Award, 
  Megaphone,
  CheckCheck,
  ChevronRight
} from 'lucide-react';

export default function StudentNotificationsPage() {
  const navigate = useNavigate();
  const { showToast } = useToast();
  
  const [data, setData] = useState<NotificationsResponse | null>(null);
  const [loading, setLoading] = useState(true);

  const loadNotifications = async () => {
    try {
      const res = await studentApi.getNotifications();
      setData(res);
    } catch {
      showToast('Error', 'Failed to load notifications', 'error');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadNotifications();
  }, []);

  const handleMarkRead = async (id: string) => {
    try {
      await studentApi.markNotificationRead(id);
      // Update local state directly
      setData((prev: any) => prev ? {
        ...prev,
        unreadCount: Math.max(0, prev.unreadCount - 1),
        notifications: prev.notifications.map((n: any) => 
          n.notificationId === id ? { ...n, isRead: true } : n
        )
      } : prev);
    } catch {
      showToast('Error', 'Failed to mark notification as read', 'error');
    }
  };

  const handleMarkAllRead = async () => {
    if (!data || data.unreadCount === 0) return;
    try {
      // Loop and mark all read, or call a read-all student route if backend supports it.
      // For safety, mark all locally via API calls or update local state after bulk trigger.
      const unreadList = data.notifications.filter((n: any) => !n.isRead);
      await Promise.all(unreadList.map((n: any) => studentApi.markNotificationRead(n.notificationId)));
      
      showToast('Success', 'All notifications marked as read', 'success');
      loadNotifications();
    } catch {
      showToast('Error', 'Failed to mark all notifications as read', 'error');
    }
  };

  const getNotificationIcon = (type: string) => {
    switch (type) {
      case 'ViolationWarning':
        return (
          <div className="w-10 h-10 rounded-full bg-red-100 text-red-600 flex items-center justify-center flex-shrink-0">
            <AlertTriangle size={20} />
          </div>
        );
      case 'ExamScheduled':
      case 'ExamReminder':
        return (
          <div className="w-10 h-10 rounded-full bg-blue-100 text-blue-600 flex items-center justify-center flex-shrink-0">
            <Calendar size={20} />
          </div>
        );
      case 'GradeReleased':
        return (
          <div className="w-10 h-10 rounded-full bg-green-100 text-green-600 flex items-center justify-center flex-shrink-0">
            <Award size={20} />
          </div>
        );
      case 'Announcement':
        return (
          <div className="w-10 h-10 rounded-full bg-purple-100 text-purple-600 flex items-center justify-center flex-shrink-0">
            <Megaphone size={20} />
          </div>
        );
      default:
        return (
          <div className="w-10 h-10 rounded-full bg-slate-100 text-slate-600 flex items-center justify-center flex-shrink-0">
            <Bell size={20} />
          </div>
        );
    }
  };

  return (
    <StudentLayout>
      <div className="max-w-[800px] mx-auto w-full px-4 py-6 space-y-6">
        
        {/* Header Section */}
        <div className="flex flex-col sm:flex-row sm:items-end justify-between gap-4">
          <div className="flex items-center gap-3">
            <h1 className="text-2xl font-extrabold text-slate-900 tracking-tight">Notifications</h1>
            {data && data.unreadCount > 0 && (
              <span className="bg-green-100 text-green-800 text-xs font-bold px-3 py-1 rounded-full">
                {data.unreadCount} unread
              </span>
            )}
          </div>
          {data && data.unreadCount > 0 && (
            <button 
              onClick={handleMarkAllRead}
              className="text-primary-600 hover:text-primary-700 font-bold text-xs flex items-center gap-1.5 hover:underline decoration-2 underline-offset-4"
            >
              <CheckCheck size={16} />
              Mark all as read
            </button>
          )}
        </div>

        {/* Notifications Feed */}
        {loading ? (
          <div className="py-24 flex justify-center">
            <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin" />
          </div>
        ) : !data || data.notifications.length === 0 ? (
          <div className="py-20 bg-white border border-slate-200 rounded-xl flex flex-col items-center justify-center text-center p-8 shadow-sm">
            <div className="w-16 h-16 rounded-full bg-slate-50 flex items-center justify-center text-slate-400 mb-4 border border-slate-100">
              <Bell size={28} />
            </div>
            <h3 className="text-lg font-bold text-slate-800 mb-1">You're all caught up!</h3>
            <p className="text-xs text-slate-500 max-w-xs leading-relaxed">
              No notifications or announcements are currently registered for your profile. Keep it up!
            </p>
          </div>
        ) : (
          <div className="space-y-4">
            {data.notifications.map((n: any) => {
              const isUnread = !n.isRead;
              
              return (
                <div
                  key={n.notificationId}
                  onClick={() => isUnread && handleMarkRead(n.notificationId)}
                  className={`rounded-xl p-5 flex gap-4 transition-all duration-300 shadow-sm border
                    ${isUnread 
                      ? 'border-l-[3px] border-l-primary-500 bg-blue-50/10 border-slate-200 hover:translate-y-[-1px] cursor-pointer' 
                      : 'bg-white border-slate-200/80 opacity-80 hover:opacity-100'}`}
                >
                  {getNotificationIcon(n.type)}
                  <div className="flex-1 space-y-1.5">
                    <div className="flex justify-between items-start">
                      <h3 className={`text-base leading-snug ${isUnread ? 'font-bold text-slate-800' : 'font-semibold text-slate-600'}`}>
                        {n.title}
                      </h3>
                      <span className="text-[10px] font-bold text-slate-400 whitespace-nowrap">
                        {formatDistanceToNow(new Date(n.createdAt), { addSuffix: true })}
                      </span>
                    </div>

                    <p className="text-xs text-slate-600 leading-relaxed">
                      {n.body}
                    </p>

                    {/* Conditional Action Triggers */}
                    {n.type === 'GradeReleased' && n.relatedEntityId && (
                      <button
                        onClick={(e) => {
                          e.stopPropagation();
                          navigate(`/student/exams/${n.relatedEntityId}/result`);
                        }}
                        className="inline-flex items-center text-xs font-bold text-primary-600 hover:text-primary-700 mt-2 transition-colors"
                      >
                        <span>View Results</span>
                        <ChevronRight size={14} className="ml-0.5" />
                      </button>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        )}

      </div>
    </StudentLayout>
  );
}
