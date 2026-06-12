import { useState, useEffect } from 'react';
import AppLayout from '../../components/layout/AppLayout';
import { examsApi } from '../../api/exams.api';
import { notificationsApi } from '../../api/notifications.api';
import type { Exam } from '../../types';
import { useToast } from '../../context/ToastContext';
import { Info, Send, Users, Sparkles, CheckCircle, History } from 'lucide-react';

export default function AnnouncementPage() {
  const { showToast } = useToast();
  const [exams, setExams] = useState<Exam[]>([]);
  const [selectedExamId, setSelectedExamId] = useState('');
  const [title, setTitle] = useState('');
  const [message, setMessage] = useState('');
  const [sendEmail, setSendEmail] = useState(false);
  const [loading, setLoading] = useState(false);
  const [recipientCount, setRecipientCount] = useState(0);
  const [history, setHistory] = useState<{ id: string; title: string; examTitle: string; time: string; count: number }[]>([
    { id: '1', title: 'Room Change: LAB 04', examTitle: 'Mid-Term Lab Exam — CS301', time: 'Sent 2 days ago', count: 32 },
    { id: '2', title: 'Material Uploaded', examTitle: 'Final Practical — BIO202', time: 'Sent 1 week ago', count: 28 }
  ]);

  useEffect(() => {
    examsApi.getAll()
      .then(res => {
        setExams(res);
        if (res.length > 0) {
          setSelectedExamId(res[0].examId);
        }
      })
      .catch(() => showToast('Error', 'Failed to load exams', 'error'));
  }, []);

  useEffect(() => {
    if (!selectedExamId) {
      setRecipientCount(0);
      return;
    }
    examsApi.getAssignments(selectedExamId)
      .then(assignments => {
        // Count eligible students
        const eligible = assignments.filter(a => a.isEligible).length;
        setRecipientCount(eligible);
      })
      .catch(() => setRecipientCount(0));
  }, [selectedExamId]);

  const handleSend = async () => {
    if (!selectedExamId || !title.trim() || !message.trim()) return;
    setLoading(true);
    try {
      const selectedExam = exams.find(e => e.examId === selectedExamId);
      const examTitle = selectedExam ? `${selectedExam.title} — ${selectedExam.courseName}` : 'Exam';
      
      const result = await notificationsApi.sendAnnouncement(selectedExamId, {
        title,
        message,
        sendEmail
      });
      showToast('Announcement Sent', result?.message || 'The announcement has been broadcast successfully.', 'success');
      
      // Add to local history list
      setHistory(prev => [
        {
          id: Date.now().toString(),
          title,
          examTitle,
          time: 'Just now',
          count: recipientCount
        },
        ...prev
      ]);

      // Reset form fields
      setTitle('');
      setMessage('');
      setSendEmail(false);
    } catch (err) {
      console.error(err);
      showToast('Failed', 'Could not send announcement.', 'error');
    } finally {
      setLoading(false);
    }
  };

  return (
    <AppLayout title="Send Announcement">
      <div className="max-w-6xl mx-auto w-full px-4 py-6">
        <div className="mb-8 animate-fade-in">
          <h2 className="text-3xl font-extrabold text-slate-900 tracking-tight mb-2">Send Announcement</h2>
          <p className="text-base text-slate-500 max-w-2xl">Send a message to all eligible students assigned to an exam.</p>
        </div>

        {/* Bento grid layout */}
        <div className="grid grid-cols-12 gap-6 items-start">
          <div className="col-span-12 lg:col-span-8">
            <div className="bg-white rounded-xl border border-slate-200 overflow-hidden shadow-sm flex flex-col">
              <div className="p-6 space-y-6">
                {/* Exam Selection */}
                <div className="space-y-2">
                  <label htmlFor="exam-select" className="text-xs font-semibold tracking-wider text-slate-500 block ml-1 uppercase">Select Exam</label>
                  <div className="relative">
                    <select
                      id="exam-select"
                      value={selectedExamId}
                      onChange={e => setSelectedExamId(e.target.value)}
                      className="w-full bg-slate-50 border border-slate-200 rounded-lg px-4 py-3.5 focus:ring-2 focus:ring-primary-500 focus:border-transparent transition-all text-sm font-medium text-slate-800 cursor-pointer"
                    >
                      {exams.length === 0 ? (
                        <option value="">No exams available</option>
                      ) : (
                        exams.map(exam => (
                          <option key={exam.examId} value={exam.examId}>
                            {exam.title} — {exam.courseName} ({exam.sectionName})
                          </option>
                        ))
                      )}
                    </select>
                  </div>
                </div>

                {/* Recipient Preview */}
                <div className="bg-blue-50/50 rounded-lg p-5 flex items-center justify-between border-l-4 border-primary-500 shadow-sm">
                  <div className="flex items-center space-x-4">
                    <div className="w-10 h-10 rounded-full bg-primary-100 flex items-center justify-center text-primary-600">
                      <Users size={20} />
                    </div>
                    <span className="text-sm font-medium text-slate-600">
                      This announcement will be sent to {recipientCount} eligible student{recipientCount !== 1 ? 's' : ''}
                    </span>
                  </div>
                  <div className="bg-primary-600 text-white px-3 py-1 rounded-full text-xs font-bold shadow-sm">
                    {recipientCount}
                  </div>
                </div>

                {/* Notification Details */}
                <div className="space-y-4">
                  <div className="space-y-2">
                    <label htmlFor="announce-title" className="text-xs font-semibold tracking-wider text-slate-500 block ml-1 uppercase">Notification Title</label>
                    <input
                      id="announce-title"
                      className="w-full bg-white border border-slate-200 rounded-lg px-4 py-3 shadow-sm focus:ring-2 focus:ring-primary-500 focus:border-transparent transition-all text-sm"
                      placeholder="e.g., Change of Venue for Lab Exam"
                      type="text"
                      value={title}
                      onChange={e => setTitle(e.target.value)}
                      required
                    />
                  </div>
                  
                  <div className="space-y-2">
                    <label htmlFor="announce-message" className="text-xs font-semibold tracking-wider text-slate-500 block ml-1 uppercase">Message</label>
                    <textarea
                      id="announce-message"
                      className="w-full bg-white border border-slate-200 rounded-lg px-4 py-3 shadow-sm focus:ring-2 focus:ring-primary-500 focus:border-transparent transition-all text-sm resize-none"
                      placeholder="Write your announcement message here..."
                      rows={6}
                      value={message}
                      onChange={e => setMessage(e.target.value)}
                      required
                    />
                  </div>
                </div>

                {/* Email Toggle */}
                <div className="flex items-center justify-between pt-4 border-t border-slate-100">
                  <div className="flex flex-col">
                    <span className="text-sm font-bold text-slate-800">Also send via email</span>
                    <span className="text-xs text-slate-500">Students will receive a copy in their university inbox</span>
                  </div>
                  <button
                    type="button"
                    onClick={() => setSendEmail(!sendEmail)}
                    className={`w-12 h-6 rounded-full p-1 transition-colors duration-200 relative focus:outline-none
                      ${sendEmail ? 'bg-primary-600' : 'bg-slate-300'}`}
                  >
                    <div
                      className={`w-4 h-4 bg-white rounded-full shadow transition-transform duration-200
                        ${sendEmail ? 'translate-x-6' : 'translate-x-0'}`}
                    />
                  </button>
                </div>
              </div>

              {/* Action Buttons */}
              <div className="bg-slate-50 px-6 py-4 flex items-center justify-between border-t border-slate-200">
                <div className="flex items-center text-slate-500 space-x-1.5">
                  <Info size={16} />
                  <span className="text-xs font-medium">{recipientCount} students will be notified</span>
                </div>
                <div className="flex items-center space-x-3">
                  <button
                    type="button"
                    onClick={() => {
                      setTitle('');
                      setMessage('');
                      setSendEmail(false);
                    }}
                    className="px-4 py-2 rounded-lg border border-slate-200 text-slate-700 font-semibold text-xs hover:bg-slate-100 transition-colors active:scale-95"
                  >
                    Cancel
                  </button>
                  <button
                    type="button"
                    onClick={handleSend}
                    disabled={loading || !selectedExamId || !title.trim() || !message.trim()}
                    className="px-5 py-2 rounded-lg bg-primary-600 text-white font-semibold text-xs shadow-md hover:bg-primary-700 transition-all disabled:opacity-50 active:scale-95 flex items-center space-x-1.5"
                  >
                    {loading ? (
                      <div className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
                    ) : (
                      <>
                        <span>Send Announcement</span>
                        <Send size={14} />
                      </>
                    )}
                  </button>
                </div>
              </div>
            </div>
          </div>

          {/* Sidebar Tips & History */}
          <div className="col-span-12 lg:col-span-4 space-y-6">
            {/* Writing Tips */}
            <div className="bg-primary-50/30 rounded-2xl border border-primary-100 p-6 shadow-sm">
              <h3 className="text-base font-bold text-primary-700 mb-4 flex items-center gap-2">
                <Sparkles size={18} />
                Writing Tips
              </h3>
              <ul className="space-y-4 text-xs font-medium text-slate-600">
                <li className="flex items-start gap-2.5">
                  <CheckCircle size={16} className="text-primary-600 mt-0.5 flex-shrink-0" />
                  <span>Keep titles brief and action-oriented (e.g. "Venue Shift: Room 102").</span>
                </li>
                <li className="flex items-start gap-2.5">
                  <CheckCircle size={16} className="text-primary-600 mt-0.5 flex-shrink-0" />
                  <span>Include important dates, timing offsets, or lab details explicitly.</span>
                </li>
                <li className="flex items-start gap-2.5">
                  <CheckCircle size={16} className="text-primary-600 mt-0.5 flex-shrink-0" />
                  <span>Only toggle email notifications for high-priority or urgent exam changes.</span>
                </li>
              </ul>
            </div>

            {/* Past Announcements */}
            <div className="bg-white rounded-2xl border border-slate-200 p-6 shadow-sm">
              <h3 className="text-xs font-bold text-slate-400 uppercase tracking-widest mb-4 flex items-center gap-2">
                <History size={14} />
                Recent Announcements
              </h3>
              <div className="space-y-3">
                {history.map(item => (
                  <div key={item.id} className="p-3 bg-slate-50 rounded-xl border border-slate-200">
                    <p className="text-sm font-bold text-slate-800 mb-0.5">{item.title}</p>
                    <p className="text-xs text-slate-500 mb-1 leading-snug">{item.examTitle}</p>
                    <p className="text-[10px] text-slate-400 font-semibold flex items-center justify-between">
                      <span>{item.time}</span>
                      <span className="bg-slate-200 text-slate-600 px-1.5 py-0.5 rounded-full font-bold">
                        {item.count} students
                      </span>
                    </p>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      </div>
    </AppLayout>
  );
}
