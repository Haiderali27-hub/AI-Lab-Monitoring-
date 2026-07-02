import { useState, useEffect } from 'react';
import AppLayout from '../../components/layout/AppLayout';
import { notificationsApi } from '../../api/notifications.api';
import type { AppNotification, NotificationsResponse } from '../../types';
import { formatDistanceToNow } from 'date-fns';
import { 
  Bell, 
  CheckCircle, 
  AlertTriangle, 
  BookOpen, 
  UserPlus, 
  Search, 
  ChevronLeft, 
  ChevronRight,
  Megaphone,
  CheckCheck
} from 'lucide-react';
import { useToast } from '../../context/ToastContext';

export default function NotificationsPage() {
  const { showToast } = useToast();
  const [data, setData] = useState<NotificationsResponse | null>(null);
  const [filter, setFilter] = useState<'All' | 'Unread' | 'Exam' | 'Violations' | 'System'>('All');
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);
  const itemsPerPage = 8;

  const loadNotifications = async () => {
    setLoading(true);
    try {
      const res = await notificationsApi.getAll();
      setData(res);
    } catch (err) {
      console.error(err);
      showToast('Error', 'Failed to load notifications', 'error');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadNotifications();
  }, []);

  const handleMarkAll = async () => {
    try {
      await notificationsApi.markAllRead();
      showToast('Success', 'All notifications marked as read', 'success');
      loadNotifications();
    } catch {
      showToast('Error', 'Failed to mark all as read', 'error');
    }
  };

  const handleMarkOne = async (id: string) => {
    try {
      await notificationsApi.markRead(id);
      loadNotifications();
    } catch {
      showToast('Error', 'Failed to mark notification as read', 'error');
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
            <BookOpen size={20} />
          </div>
        );
      case 'GradeReleased':
        return (
          <div className="w-10 h-10 rounded-full bg-green-100 text-green-600 flex items-center justify-center flex-shrink-0">
            <CheckCircle size={20} />
          </div>
        );
      case 'EligibilityChanged':
        return (
          <div className="w-10 h-10 rounded-full bg-orange-100 text-orange-600 flex items-center justify-center flex-shrink-0">
            <UserPlus size={20} />
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

  const filtered = (data?.notifications ?? []).filter((n: AppNotification) => {
    // 1. Category Filter
    if (filter === 'Unread' && n.isRead) return false;
    if (filter === 'Violations' && n.type !== 'ViolationWarning') return false;
    if (filter === 'Exam' && !['ExamScheduled', 'GradeReleased', 'ExamReminder'].includes(n.type)) return false;
    if (filter === 'System' && !['EligibilityChanged', 'Announcement'].includes(n.type)) return false;

    // 2. Search Filter
    if (search.trim() !== '') {
      const s = search.toLowerCase();
      return n.title.toLowerCase().includes(s) || n.body.toLowerCase().includes(s);
    }

    return true;
  });

  // Pagination
  const totalItems = filtered.length;
  const totalPages = Math.ceil(totalItems / itemsPerPage) || 1;
  const paginated = filtered.slice((page - 1) * itemsPerPage, page * itemsPerPage);

  const handlePageChange = (newPage: number) => {
    if (newPage >= 1 && newPage <= totalPages) {
      setPage(newPage);
    }
  };

  return (
    <AppLayout title="Notifications">
      <div className="max-w-5xl mx-auto w-full px-4 py-6">
        {/* Filters & Actions */}
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 mb-6 border-b border-slate-200">
          <div className="flex gap-6 overflow-x-auto no-scrollbar pb-3">
            {(['All', 'Unread', 'Exam', 'Violations', 'System'] as const).map(f => (
              <button
                key={f}
                onClick={() => {
                  setFilter(f);
                  setPage(1);
                }}
                className={`pb-3 font-semibold text-sm whitespace-nowrap border-b-2 transition-colors duration-200
                  ${filter === f 
                    ? 'border-primary-600 text-primary-600' 
                    : 'border-transparent text-slate-500 hover:text-slate-700'}`}
              >
                {f}
              </button>
            ))}
          </div>

          <div className="flex items-center gap-4 mb-3">
            {/* Search Input */}
            <div className="relative">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" size={16} />
              <input
                className="pl-9 pr-4 py-1.5 bg-slate-100 rounded-full border-none focus:ring-2 focus:ring-primary-500 text-sm w-60"
                placeholder="Search notifications..."
                type="text"
                value={search}
                onChange={e => {
                  setSearch(e.target.value);
                  setPage(1);
                }}
              />
            </div>
            
            <button 
              onClick={handleMarkAll}
              className="text-primary-600 font-semibold text-sm hover:underline flex items-center gap-1.5"
            >
              <CheckCheck size={16} />
              Mark all read
            </button>
          </div>
        </div>

        {/* List */}
        {loading ? (
          <div className="py-12 flex justify-center">
            <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin" />
          </div>
        ) : paginated.length === 0 ? (
          <div className="py-16 bg-white border border-slate-200 rounded-xl flex flex-col items-center justify-center text-center p-8">
            <div className="w-16 h-16 rounded-full bg-slate-100 flex items-center justify-center text-slate-400 mb-4">
              <Bell size={32} />
            </div>
            <h3 className="text-lg font-bold text-slate-800 mb-1">All Caught Up!</h3>
            <p className="text-sm text-slate-500 max-w-sm">
              No notifications found matching your active filter criteria. Check back later for updates.
            </p>
          </div>
        ) : (
          <div className="space-y-3">
            {paginated.map((n: AppNotification) => (
              <div
                key={n.notificationId}
                onClick={() => !n.isRead && handleMarkOne(n.notificationId)}
                className={`bg-white border border-slate-200 rounded-lg p-4 flex gap-4 relative transition-all hover:shadow-sm cursor-pointer
                  ${!n.isRead 
                    ? 'border-l-[3px] border-l-primary-500 bg-blue-50/10' 
                    : 'hover:bg-slate-50'}`}
              >
                {getNotificationIcon(n.type)}
                <div className="flex-1">
                  <div className="flex justify-between items-start mb-1">
                    <h3 className={`text-base ${!n.isRead ? 'font-bold text-slate-900' : 'font-medium text-slate-700'}`}>
                      {n.title}
                    </h3>
                    <div className="flex items-center gap-2">
                      <span className="text-xs text-slate-400">
                        {formatDistanceToNow(new Date(n.createdAt), { addSuffix: true })}
                      </span>
                      {!n.isRead && (
                        <span className="w-2 h-2 bg-primary-500 rounded-full" />
                      )}
                    </div>
                  </div>
                  <p className="text-sm text-slate-600 leading-relaxed mb-2">
                    {n.body}
                  </p>
                  
                  {n.relatedEntityId && (
                    <span className="text-primary-600 font-semibold text-xs hover:underline flex items-center gap-1">
                      Related Entity ID: {n.relatedEntityId}
                    </span>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}

        {/* Pagination */}
        {!loading && paginated.length > 0 && (
          <div className="mt-10 flex flex-col md:flex-row items-center justify-between gap-4 border-t border-slate-200 pt-6">
            <span className="text-sm text-slate-500">
              Showing {(page - 1) * itemsPerPage + 1}–{Math.min(page * itemsPerPage, totalItems)} of {totalItems}
            </span>
            <div className="flex items-center gap-2">
              <button
                disabled={page === 1}
                onClick={() => handlePageChange(page - 1)}
                className="w-10 h-10 flex items-center justify-center rounded-lg border border-slate-200 text-slate-600 hover:bg-slate-50 transition-colors disabled:opacity-30 disabled:pointer-events-none"
              >
                <ChevronLeft size={18} />
              </button>
              {Array.from({ length: totalPages }, (_, idx) => idx + 1).map(p => (
                <button
                  key={p}
                  onClick={() => handlePageChange(p)}
                  className={`w-10 h-10 flex items-center justify-center rounded-lg font-semibold text-sm transition-all
                    ${page === p 
                      ? 'bg-primary-600 text-white shadow-sm' 
                      : 'border border-slate-200 text-slate-600 hover:bg-slate-50'}`}
                >
                  {p}
                </button>
              ))}
              <button
                disabled={page === totalPages}
                onClick={() => handlePageChange(page + 1)}
                className="w-10 h-10 flex items-center justify-center rounded-lg border border-slate-200 text-slate-600 hover:bg-slate-50 transition-colors disabled:opacity-30 disabled:pointer-events-none"
              >
                <ChevronRight size={18} />
              </button>
            </div>
          </div>
        )}
      </div>
    </AppLayout>
  );
}
