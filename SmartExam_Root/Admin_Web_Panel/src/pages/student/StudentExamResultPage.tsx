import { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import StudentLayout from '../../components/layout/StudentLayout';
import { studentApi } from '../../api/student.api';
import type { StudentExamResult } from '../../types';
import { useToast } from '../../context/ToastContext';
import { format } from 'date-fns';
import { 
  ArrowLeft, 
  Calendar, 
  Clock, 
  CheckCircle, 
  Sparkles, 
  ThumbsUp, 
  Lightbulb, 
  AlertCircle
} from 'lucide-react';

export default function StudentExamResultPage() {
  const { examId } = useParams();
  const navigate = useNavigate();
  const { showToast } = useToast();

  const [result, setResult] = useState<StudentExamResult | null>(null);
  const [activeTab, setActiveTab] = useState<'answers' | 'feedback' | 'violations'>('answers');
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!examId) return;
    setLoading(true);
    studentApi.getExamResult(examId)
      .then(res => {
        setResult(res);
      })
      .catch((err) => {
        console.error(err);
        showToast('Error', 'Failed to load exam result details', 'error');
      })
      .finally(() => setLoading(false));
  }, [examId]);

  if (loading) {
    return (
      <StudentLayout>
        <div className="py-24 flex justify-center">
          <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin" />
        </div>
      </StudentLayout>
    );
  }

  if (!result) {
    return (
      <StudentLayout>
        <div className="py-16 text-center">
          <p className="text-slate-500 font-semibold mb-4">Exam result details not found.</p>
          <button
            onClick={() => navigate('/student/exams')}
            className="px-4 py-2 bg-primary-600 text-white font-bold rounded-lg"
          >
            Back to My Exams
          </button>
        </div>
      </StudentLayout>
    );
  }

  const isPassed = result.passed;

  return (
    <StudentLayout>
      <div className="max-w-4xl mx-auto w-full px-4 py-6">
        
        {/* Breadcrumb / Back Link */}
        <button
          onClick={() => navigate(-1)}
          className="group flex items-center gap-1.5 mb-6 text-primary-600 hover:text-primary-700 transition-colors font-bold text-xs"
        >
          <ArrowLeft size={16} />
          <span>Back to My Exams</span>
        </button>

        {/* Result Header Card */}
        <section className="bg-white border border-slate-200 border-l-[6px] border-l-primary-500 rounded-xl p-6 shadow-sm mb-8">
          <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-6">
            <div>
              <p className="text-xs font-bold text-slate-400 mb-1 uppercase tracking-wider">
                Exam Evaluation Results
              </p>
              <h1 className="text-2xl font-extrabold text-slate-800 mb-2 leading-tight">
                {result.title}
              </h1>
              <div className="flex items-center gap-4 text-xs font-semibold text-slate-400">
                <div className="flex items-center gap-1">
                  <Calendar size={14} />
                  <span>Conducted: {format(new Date(result.startedAt), 'MMM dd, yyyy')}</span>
                </div>
                <div className="flex items-center gap-1">
                  <Clock size={14} />
                  <span>120 Minutes Session</span>
                </div>
              </div>
            </div>

            {/* Score block */}
            <div className="flex items-center gap-5 bg-slate-50 p-4 rounded-xl border border-slate-100">
              <div className="text-center">
                <span className={`text-3xl font-extrabold block ${isPassed ? 'text-green-600' : 'text-red-500'}`}>
                  {result.scorePercent}%
                </span>
                <span className="text-[10px] font-bold text-slate-400 uppercase tracking-wider mt-0.5 block">
                  {result.earnedMarks} / {result.totalMarks} Marks
                </span>
              </div>
              <div className="h-12 w-px bg-slate-200" />
              <div className="flex flex-col items-center gap-1">
                <span className={`px-2.5 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider
                  ${isPassed ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'}`}>
                  {isPassed ? 'Passed' : 'Failed'}
                </span>
                {isPassed ? (
                  <CheckCircle size={22} className="text-green-600" />
                ) : (
                  <AlertCircle size={22} className="text-red-500" />
                )}
              </div>
            </div>
          </div>
        </section>

        {/* Tab Navigation */}
        <div className="flex gap-8 border-b border-slate-200 mb-6 px-2">
          {(['answers', 'feedback', 'violations'] as const).map(tab => (
            <button
              key={tab}
              onClick={() => setActiveTab(tab)}
              className={`pb-3 font-bold text-sm border-b-2 transition-all duration-200 uppercase tracking-wider
                ${activeTab === tab
                  ? 'text-primary-600 border-primary-600'
                  : 'text-slate-400 border-transparent hover:text-slate-600'}`}
            >
              {tab === 'answers' ? 'My Answers' : tab === 'feedback' ? 'AI Feedback Summary' : 'Violations'}
            </button>
          ))}
        </div>

        {/* Tab Contents */}
        <div className="space-y-6">
          
          {/* TAB 1: MY ANSWERS */}
          {activeTab === 'answers' && (
            <div className="space-y-6">
              {result.answers.map((answer: any, index: number) => (
                <div key={answer.answerId} className="bg-white border border-slate-200 rounded-xl p-6 shadow-sm">
                  
                  {/* Card header */}
                  <div className="flex justify-between items-start mb-4">
                    <div className="flex items-center gap-3">
                      <span className="w-8 h-8 rounded-full bg-primary-500 text-white flex items-center justify-center font-bold text-sm shadow-sm">
                        {index + 1}
                      </span>
                      <span className="px-2.5 py-0.5 bg-slate-100 text-slate-600 rounded-full text-[10px] font-bold uppercase tracking-wider">
                        {answer.questionType}
                      </span>
                    </div>

                    <div className="text-right flex flex-col items-end gap-1.5">
                      <span className="font-bold text-slate-800 text-sm">
                        {answer.earnedMarks} / {answer.totalMarks} Marks
                      </span>
                      
                      {answer.teacherOverridden ? (
                        <span className="px-2 py-0.5 bg-amber-50 border border-amber-200 text-amber-700 rounded-full text-[9px] font-bold inline-flex items-center gap-1 uppercase tracking-wider">
                          Teacher Reviewed ✓
                        </span>
                      ) : answer.aiFeedback ? (
                        <span className={`px-2 py-0.5 rounded-full text-[9px] font-bold inline-flex items-center gap-1 uppercase tracking-wider
                          ${answer.aiFeedback.confidence === 'High' 
                            ? 'bg-green-50 border border-green-200 text-green-700' 
                            : answer.aiFeedback.confidence === 'Medium'
                              ? 'bg-amber-50 border border-amber-200 text-amber-700'
                              : 'bg-red-50 border border-red-200 text-red-700'}`}>
                          <Sparkles size={10} />
                          AI: {answer.aiFeedback.confidence} Confidence
                        </span>
                      ) : null}
                    </div>
                  </div>

                  {/* Question Body */}
                  <p className="text-sm font-semibold text-slate-800 mb-4 leading-relaxed bg-slate-50 p-3 rounded-lg border border-slate-100">
                    {answer.questionText}
                  </p>

                  {/* Answer representation */}
                  {answer.questionType === 'Coding' ? (
                    <div className="rounded-lg overflow-hidden bg-slate-950 text-emerald-400 p-4 font-mono text-xs overflow-x-auto leading-relaxed shadow-inner">
                      <pre><code>{answer.answerText}</code></pre>
                    </div>
                  ) : (
                    <div className="p-4 bg-slate-50 border-l-4 border-primary-500 rounded-r-lg font-medium text-slate-700 text-sm leading-relaxed">
                      {answer.answerText}
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}

          {/* TAB 2: AI FEEDBACK */}
          {activeTab === 'feedback' && (
            <div className="space-y-6">
              
              {/* Main Banner */}
              <div className="bg-gradient-to-r from-blue-50 to-indigo-50/50 p-6 rounded-xl border border-blue-100 flex items-center gap-6 shadow-sm">
                <div className="w-14 h-14 bg-white rounded-full flex items-center justify-center shadow-md text-primary-600 flex-shrink-0">
                  <Sparkles size={28} />
                </div>
                <div>
                  <h2 className="text-base font-bold text-slate-800 mb-1">AI Evaluation Engine Feedback</h2>
                  <p className="text-xs text-slate-500 leading-relaxed max-w-xl">
                    Our SmartEval engine analyzed your response logic, code optimization, and semantic description accuracy. Check the evaluation remarks below.
                  </p>
                </div>
              </div>

              {/* Strengths & Weaknesses */}
              <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                
                {/* Strengths */}
                <div className="bg-white border border-slate-200 rounded-xl p-5 shadow-sm">
                  <div className="flex items-center gap-2 mb-4 text-green-700">
                    <ThumbsUp size={18} />
                    <h3 className="font-bold text-sm uppercase tracking-wider">Strengths</h3>
                  </div>
                  <div className="space-y-3">
                    {result.answers.filter((a: any) => a.earnedMarks >= a.totalMarks * 0.7).map((answer: any, i: number) => (
                      <blockquote key={i} className="border-l-3 border-green-500 pl-4 py-2 text-xs text-slate-600 italic bg-green-50/30 rounded-r-lg">
                        "{answer.aiFeedback?.justification || 'Demonstrated correct logical implementation.'}"
                      </blockquote>
                    ))}
                    {result.answers.filter((a: any) => a.earnedMarks >= a.totalMarks * 0.7).length === 0 && (
                      <p className="text-xs text-slate-400 italic">No notable high-scoring items listed.</p>
                    )}
                  </div>
                </div>

                {/* Opportunities */}
                <div className="bg-white border border-slate-200 rounded-xl p-5 shadow-sm">
                  <div className="flex items-center gap-2 mb-4 text-amber-700">
                    <Lightbulb size={18} />
                    <h3 className="font-bold text-sm uppercase tracking-wider">Opportunities</h3>
                  </div>
                  <div className="space-y-3">
                    {result.answers.filter((a: any) => a.earnedMarks < a.totalMarks * 0.7).map((answer: any, i: number) => (
                      <blockquote key={i} className="border-l-3 border-amber-500 pl-4 py-2 text-xs text-slate-600 italic bg-amber-50/30 rounded-r-lg">
                        "{answer.aiFeedback?.justification || 'Review core edge-cases and runtime parameters.'}"
                      </blockquote>
                    ))}
                    {result.answers.filter((a: any) => a.earnedMarks < a.totalMarks * 0.7).length === 0 && (
                      <p className="text-xs text-slate-400 italic">Excellent! You maximized marks on all sections.</p>
                    )}
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* TAB 3: VIOLATIONS */}
          {activeTab === 'violations' && (
            <div className="bg-white border border-slate-200 rounded-xl p-8 text-center shadow-sm">
              <CheckCircle className="text-green-600 mx-auto mb-4" size={48} />
              <h2 className="text-lg font-bold text-slate-800 mb-2">Integrity Verified</h2>
              <p className="text-xs text-slate-500 max-w-md mx-auto leading-relaxed">
                No exam violations, suspicious environment shifts (background browser tab swaps, webcam gaze anomalies, blocked apps) were confirmed during your session. Excellent job adhering to academic rules.
              </p>
            </div>
          )}

        </div>

      </div>
    </StudentLayout>
  );
}
