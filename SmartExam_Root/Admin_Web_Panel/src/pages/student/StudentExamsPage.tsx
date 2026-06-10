import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import StudentLayout from '../../components/layout/StudentLayout';
import { studentApi } from '../../api/student.api';
import { useToast } from '../../context/ToastContext';
import { format } from 'date-fns';
import { 
  Award,
  Clock,
  AlertTriangle,
  Hourglass
} from 'lucide-react';

export default function StudentExamsPage() {
  const navigate = useNavigate();
  const { showToast } = useToast();
  
  const [exams, setExams] = useState<any[]>([]);
  const [filter, setFilter] = useState<'All' | 'Passed' | 'Failed' | 'Upcoming'>('All');
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    studentApi.getMyExams()
      .then(res => {
        setExams(res);
      })
      .catch(() => {
        showToast('Error', 'Failed to load exams list', 'error');
      })
      .finally(() => setLoading(false));
  }, []);

  const filtered = exams.filter(e => {
    if (filter === 'All') return true;
    if (filter === 'Passed') return e.status === 'Ended' && e.scorePercent >= 50;
    if (filter === 'Failed') return e.status === 'Ended' && e.scorePercent < 50;
    if (filter === 'Upcoming') return e.status === 'Scheduled' || e.status === 'Active';
    return true;
  });

  return (
    <StudentLayout>
      <div className="max-w-6xl mx-auto w-full px-4 py-6">
        {/* Header Section */}
        <header className="mb-8">
          <h1 className="text-3xl font-extrabold text-slate-900 tracking-tight mb-1">My Exams</h1>
          <p className="text-sm text-slate-500 font-medium">Your complete exam history and scheduled roster</p>
        </header>

        {/* Filter Bar */}
        <div className="flex flex-wrap gap-2 mb-8">
          {(['All', 'Passed', 'Failed', 'Upcoming'] as const).map(f => (
            <button
              key={f}
              onClick={() => setFilter(f)}
              className={`px-5 py-1.5 rounded-full text-xs font-semibold tracking-wider transition-all duration-200 shadow-sm
                ${filter === f 
                  ? 'bg-primary-600 text-white shadow' 
                  : 'bg-white border border-slate-200 text-slate-600 hover:bg-slate-50'}`}
            >
              {f}
            </button>
          ))}
        </div>

        {/* Loading Indicator */}
        {loading ? (
          <div className="py-24 flex justify-center">
            <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin" />
          </div>
        ) : filtered.length === 0 ? (
          <div className="py-16 bg-white border border-slate-200 rounded-xl flex flex-col items-center justify-center text-center p-8">
            <div className="w-16 h-16 rounded-full bg-slate-100 flex items-center justify-center text-slate-400 mb-4">
              <Hourglass size={30} />
            </div>
            <h3 className="text-lg font-bold text-slate-800 mb-1">No Exams Found</h3>
            <p className="text-sm text-slate-500 max-w-sm">
              We couldn't find any exams matching your selected filter tab. Check back later.
            </p>
          </div>
        ) : (
          /* Exams Grid */
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            {filtered.map(exam => {
              const isPassed = exam.status === 'Ended' && exam.scorePercent >= 50;
              const isFailed = exam.status === 'Ended' && exam.scorePercent < 50;
              const isUpcoming = exam.status === 'Scheduled';
              const isActive = exam.status === 'Active';
              const isGrading = exam.status === 'Ended' && exam.scorePercent === null; // In progress grading

              return (
                <div 
                  key={exam.examId}
                  className="bg-white rounded-xl border border-slate-200 p-6 flex flex-col gap-4 shadow-sm hover:shadow-md hover:-translate-y-0.5 transition-all duration-300"
                >
                  {/* Card Header */}
                  <div className="flex justify-between items-start">
                    <span className="px-2.5 py-0.5 rounded-full bg-blue-50 text-blue-700 text-[10px] font-bold uppercase tracking-wider">
                      {exam.courseName ? exam.courseName.slice(0, 7) : 'EXAM'}
                    </span>
                    
                    {isPassed && (
                      <span className="px-2.5 py-0.5 rounded-full bg-green-50 text-green-700 text-[10px] font-bold flex items-center gap-1">
                        <span className="w-1.5 h-1.5 rounded-full bg-green-600"></span> Passed
                      </span>
                    )}
                    {isFailed && (
                      <span className="px-2.5 py-0.5 rounded-full bg-red-50 text-red-700 text-[10px] font-bold flex items-center gap-1">
                        <span className="w-1.5 h-1.5 rounded-full bg-red-600"></span> Failed
                      </span>
                    )}
                    {isUpcoming && (
                      <span className="px-2.5 py-0.5 rounded-full bg-blue-50 text-blue-700 text-[10px] font-bold flex items-center gap-1">
                        <span className="w-1.5 h-1.5 rounded-full bg-blue-600"></span> Scheduled
                      </span>
                    )}
                    {isActive && (
                      <span className="px-2.5 py-0.5 rounded-full bg-green-100 text-green-700 text-[10px] font-bold flex items-center gap-1 animate-pulse">
                        <span className="w-1.5 h-1.5 rounded-full bg-green-600"></span> Active Now
                      </span>
                    )}
                    {isGrading && (
                      <span className="px-2.5 py-0.5 rounded-full bg-amber-50 text-amber-700 text-[10px] font-bold flex items-center gap-1">
                        <span className="w-1.5 h-1.5 rounded-full bg-amber-600 animate-ping"></span> Grading
                      </span>
                    )}
                  </div>

                  {/* Body Text */}
                  <div>
                    <h3 className="text-lg font-bold text-slate-800 mb-1 leading-snug">
                      {exam.title}
                    </h3>
                    <p className="text-xs text-slate-400 italic">
                      {exam.status === 'Scheduled' ? 'Scheduled for ' : 'Conducted on '}
                      {format(new Date(exam.startTime || exam.startedAt || new Date()), 'MMMM dd, yyyy')}
                    </p>
                  </div>

                  {/* Mid-level Info block */}
                  {isUpcoming || isActive ? (
                    <div className="flex items-center justify-center h-24 bg-slate-50 border border-dashed border-slate-200 rounded-lg">
                      <p className="text-xs text-slate-500 font-semibold flex items-center gap-1.5">
                        <Hourglass size={14} className="text-slate-400 animate-spin" />
                        {isActive ? 'Exam is active now! Join from Dashboard.' : 'Exam scheduled to commence.'}
                      </p>
                    </div>
                  ) : isGrading ? (
                    <div className="flex flex-col items-center justify-center h-24 bg-slate-50 rounded-lg p-4 text-center border border-slate-100">
                      <Hourglass className="text-amber-500 animate-pulse mb-1" size={24} />
                      <span className="text-xs font-semibold text-slate-500">Grading in progress...</span>
                    </div>
                  ) : (
                    /* Score breakdown for graded exams */
                    <div className="flex items-center justify-between py-4 border-y border-slate-100 bg-slate-50/30 px-2 rounded-lg">
                      <div className="flex flex-col items-center">
                        <span className="text-[10px] font-bold text-slate-400 mb-1 flex items-center gap-1">
                          <Award size={12} />
                          Score
                        </span>
                        <span className={`text-base font-bold ${isPassed ? 'text-green-600' : 'text-red-500'}`}>
                          {exam.scorePercent}%
                          <span className="text-xs font-normal text-slate-400 ml-1">
                            ({exam.earnedMarks}/{exam.totalMarks})
                          </span>
                        </span>
                      </div>

                      <div className="flex flex-col items-center">
                        <span className="text-[10px] font-bold text-slate-400 mb-1 flex items-center gap-1">
                          <Clock size={12} />
                          Duration
                        </span>
                        <span className="text-sm font-bold text-slate-700">60 mins</span>
                      </div>

                      <div className="flex flex-col items-center">
                        <span className="text-[10px] font-bold text-slate-400 mb-1 flex items-center gap-1">
                          <AlertTriangle size={12} />
                          Flags
                        </span>
                        <span className={`text-sm font-bold ${exam.violationCount > 0 ? 'text-red-500' : 'text-green-600'}`}>
                          {exam.violationCount}
                        </span>
                      </div>
                    </div>
                  )}

                  {/* Footer Buttons */}
                  {exam.status === 'Ended' && !isGrading ? (
                    <button
                      onClick={() => navigate(`/student/exams/${exam.examId}/result`)}
                      className="w-full py-2.5 rounded-full border border-primary-600 text-primary-600 font-bold text-xs hover:bg-blue-50/50 transition-all active:scale-95"
                    >
                      View Detailed Results
                    </button>
                  ) : exam.status === 'Ended' && isGrading ? (
                    <button
                      disabled
                      className="w-full py-2.5 rounded-full border border-slate-200 text-slate-400 font-bold text-xs cursor-not-allowed bg-slate-50"
                    >
                      Results Pending
                    </button>
                  ) : (
                    <button
                      disabled
                      className="w-full py-2.5 rounded-full border border-slate-200 text-slate-400 font-bold text-xs cursor-not-allowed bg-slate-50"
                    >
                      Results Unavailable
                    </button>
                  )}

                </div>
              );
            })}
          </div>
        )}
      </div>
    </StudentLayout>
  );
}
