import { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import AppLayout from '../../components/layout/AppLayout';
import { examsApi } from '../../api/exams.api';
import type { PlagiarismFlag } from '../../types';

interface Answer {
  answerId: string;
  questionId: string;
  questionText: string;
  questionType: 'Coding' | 'Theory';
  marks: number; // Max marks
  answerText: string;
  aiGrading: {
    suggestedMarks: number;
    justification: string;
    confidence: 'High' | 'Medium' | 'Low';
  } | null;
  teacherOverride: {
    finalMarks: number;
    note: string;
  } | null;
}

interface StudentSubmission {
  userId: string;
  studentName: string;
  studentEmail: string;
  sessionId: string;
  status: string;
  submittedAt: string | null;
  answers: Answer[];
}

export default function ResultsPage() {
  const { examId } = useParams<{ examId: string }>();
  const navigate = useNavigate();

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [examTitle, setExamTitle] = useState('Assessment Results');

  const [results, setResults] = useState<StudentSubmission[]>([]);
  const [plagiarism, setPlagiarism] = useState<PlagiarismFlag[]>([]);
  
  const [activeTab, setActiveTab] = useState<'submissions' | 'plagiarism' | 'summary'>('submissions');
  const [expandedStudentId, setExpandedStudentId] = useState<string | null>(null);

  // States for the grade override form
  const [overrideMarks, setOverrideMarks] = useState<{ [answerId: string]: number }>({});
  const [overrideNotes, setOverrideNotes] = useState<{ [answerId: string]: string }>({});
  const [submittingOverride, setSubmittingOverride] = useState<{ [answerId: string]: boolean }>({});

  const loadData = async () => {
    if (!examId) return;
    try {
      setLoading(true);
      const [resData, plagData, examDetail] = await Promise.all([
        examsApi.getResults(examId),
        examsApi.getPlagiarism(examId),
        examsApi.getById(examId)
      ]);
      setResults(resData);
      setPlagiarism(plagData);
      if (examDetail && examDetail.title) {
        setExamTitle(examDetail.title);
      }
    } catch (err: any) {
      console.error('Failed to load exam results', err);
      setError('Failed to fetch exam results. Please ensure backend is active.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadData();
  }, [examId]);

  const handleApplyOverride = async (answerId: string) => {
    const marks = overrideMarks[answerId];
    const note = overrideNotes[answerId] || '';

    if (marks === undefined || isNaN(marks)) {
      alert('Please enter a valid mark.');
      return;
    }

    try {
      setSubmittingOverride(prev => ({ ...prev, [answerId]: true }));
      await examsApi.overrideGrade(answerId, marks, note);
      
      // Reload results
      if (examId) {
        const updatedResults = await examsApi.getResults(examId);
        setResults(updatedResults);
      }
      alert('Grade override applied successfully!');
    } catch (err) {
      console.error('Failed to apply override', err);
      alert('Failed to save grade override.');
    } finally {
      setSubmittingOverride(prev => ({ ...prev, [answerId]: false }));
    }
  };

  // Calculate statistics
  const submissionCount = results.filter(r => r.status === 'Submitted' || r.status === 'ForceSubmitted').length;
  
  const getStudentTotalScore = (student: StudentSubmission) => {
    return student.answers.reduce((sum, a) => {
      if (a.teacherOverride) return sum + a.teacherOverride.finalMarks;
      if (a.aiGrading) return sum + a.aiGrading.suggestedMarks;
      return sum;
    }, 0);
  };

  const getStudentMaxScore = (student: StudentSubmission) => {
    return student.answers.reduce((sum, a) => sum + a.marks, 0);
  };

  // Class statistics computation
  const scores = results.map(r => {
    const max = getStudentMaxScore(r);
    const score = getStudentTotalScore(r);
    return max > 0 ? (score / max) * 100 : 0;
  });

  const classAverage = scores.length > 0 ? Math.round(scores.reduce((a, b) => a + b, 0) / scores.length) : 0;
  const highestScore = scores.length > 0 ? Math.round(Math.max(...scores)) : 0;
  const lowestScore = scores.length > 0 ? Math.round(Math.min(...scores)) : 0;

  return (
    <AppLayout title="Results & Grading">
      <div className="max-w-[1280px] mx-auto pb-12">
        
        {/* Back and Sub-header */}
        <div className="mb-8">
          <button 
            onClick={() => navigate('/teacher/dashboard')}
            className="flex items-center gap-1 text-slate-500 hover:text-slate-800 transition-colors text-xs font-semibold uppercase tracking-wider mb-2"
          >
            <span className="material-symbols-outlined text-[16px]">arrow_back</span>
            Back to Dashboard
          </button>
          
          <div className="flex items-baseline gap-3">
            <h3 className="text-2xl font-bold text-slate-800">{examTitle}</h3>
            <span className="px-2 py-0.5 bg-blue-50 border border-blue-100 rounded-lg text-[9px] font-bold text-primary-600 uppercase">
              {results.length > 0 ? 'Active Grading' : 'Pending submissions'}
            </span>
          </div>
          <p className="text-sm text-slate-400 mt-1">
            {submissionCount} of {results.length} Students Submitted
          </p>
        </div>

        {/* Error message */}
        {error && (
          <div className="mb-6 p-4 bg-red-50 border border-red-200 text-red-600 rounded-xl text-sm font-medium flex items-center gap-2">
            <span className="material-symbols-outlined">error</span>
            {error}
          </div>
        )}

        {/* Stats Grid */}
        <div className="grid grid-cols-1 md:grid-cols-4 gap-6 mb-8">
          <div className="bg-white p-6 rounded-xl border border-slate-200 shadow-sm">
            <p className="text-[10px] font-bold text-slate-400 uppercase tracking-widest mb-1">Class Average</p>
            <p className="text-3xl font-bold text-primary-500">{classAverage}%</p>
          </div>
          <div className="bg-white p-6 rounded-xl border border-slate-200 shadow-sm">
            <p className="text-[10px] font-bold text-slate-400 uppercase tracking-widest mb-1">Highest Score</p>
            <p className="text-3xl font-bold text-slate-800">{highestScore}%</p>
          </div>
          <div className="bg-white p-6 rounded-xl border border-slate-200 shadow-sm">
            <p className="text-[10px] font-bold text-slate-400 uppercase tracking-widest mb-1">Lowest Score</p>
            <p className="text-3xl font-bold text-slate-800">{lowestScore}%</p>
          </div>
          <div className={`p-6 rounded-xl border shadow-sm transition-colors ${plagiarism.length > 0 ? 'bg-red-50/50 border-red-200' : 'bg-white border-slate-200'}`}>
            <p className={`text-[10px] font-bold uppercase tracking-widest mb-1 ${plagiarism.length > 0 ? 'text-red-500' : 'text-slate-400'}`}>Plagiarism Flags</p>
            <p className={`text-3xl font-bold ${plagiarism.length > 0 ? 'text-red-600' : 'text-slate-800'}`}>{plagiarism.length}</p>
          </div>
        </div>

        {/* Tabs System */}
        <div className="border-b border-slate-200 mb-6">
          <div className="flex gap-8">
            <button 
              onClick={() => setActiveTab('submissions')}
              className={`pb-4 text-sm font-bold border-b-2 transition-all ${
                activeTab === 'submissions' 
                  ? 'text-primary-500 border-primary-500' 
                  : 'text-slate-400 border-transparent hover:text-slate-600'
              }`}
            >
              All Submissions
            </button>
            <button 
              onClick={() => setActiveTab('plagiarism')}
              className={`pb-4 text-sm font-bold border-b-2 transition-all flex items-center gap-2 ${
                activeTab === 'plagiarism' 
                  ? 'text-primary-500 border-primary-500' 
                  : 'text-slate-400 border-transparent hover:text-slate-600'
              }`}
            >
              Plagiarism Flags
              {plagiarism.length > 0 && (
                <span className="px-1.5 py-0.5 bg-red-500 text-white text-[10px] rounded-full font-bold">
                  {plagiarism.length}
                </span>
              )}
            </button>
            <button 
              onClick={() => setActiveTab('summary')}
              className={`pb-4 text-sm font-bold border-b-2 transition-all ${
                activeTab === 'summary' 
                  ? 'text-primary-500 border-primary-500' 
                  : 'text-slate-400 border-transparent hover:text-slate-600'
              }`}
            >
              Grade Summary
            </button>
          </div>
        </div>

        {loading ? (
          <div className="py-12 flex justify-center items-center flex-col">
            <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin mb-2" />
            <p className="text-xs text-slate-400">Loading submission data...</p>
          </div>
        ) : (
          <>
            {/* SUBMISSIONS TAB */}
            {activeTab === 'submissions' && (
              <div className="bg-white border border-slate-200 rounded-xl overflow-hidden shadow-sm">
                {results.length === 0 ? (
                  <div className="text-center py-12 text-slate-400 italic">No submissions found for this exam.</div>
                ) : (
                  <table className="w-full text-left border-collapse">
                    <thead className="bg-slate-50 border-b border-slate-200 text-xs font-bold text-slate-500 uppercase tracking-wider">
                      <tr>
                        <th className="px-6 py-4">Student Name</th>
                        <th className="px-6 py-4">Status</th>
                        <th className="px-6 py-4 text-center">Score</th>
                        <th className="px-6 py-4 text-right">Actions</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                      {results.map(student => {
                        const isExpanded = expandedStudentId === student.userId;
                        const score = getStudentTotalScore(student);
                        const maxScore = getStudentMaxScore(student);
                        
                        return (
                          <React.Fragment key={student.userId}>
                            <tr className="hover:bg-slate-50/50 transition-colors">
                              <td className="px-6 py-4">
                                <div className="font-semibold text-slate-800">{student.studentName}</div>
                                <div className="text-xs text-slate-400">{student.studentEmail}</div>
                              </td>
                              <td className="px-6 py-4">
                                <span className={`px-2.5 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider ${
                                  student.status === 'Submitted' || student.status === 'ForceSubmitted'
                                    ? 'bg-green-50 text-green-700 border border-green-200'
                                    : 'bg-yellow-50 text-yellow-700 border border-yellow-200'
                                }`}>
                                  {student.status}
                                </span>
                              </td>
                              <td className="px-6 py-4 text-center font-bold text-slate-800">
                                {score}/{maxScore}
                              </td>
                              <td className="px-6 py-4 text-right">
                                <button 
                                  onClick={() => setExpandedStudentId(isExpanded ? null : student.userId)}
                                  className="text-primary-500 font-bold hover:underline text-xs"
                                >
                                  {isExpanded ? 'Close' : 'Review'}
                                </button>
                              </td>
                            </tr>

                            {/* EXPANDED REVIEW PANEL */}
                            {isExpanded && (
                              <tr className="bg-slate-50/50">
                                <td colSpan={4} className="px-8 py-6">
                                  {student.answers.length === 0 ? (
                                    <div className="text-center text-xs text-slate-400 italic py-4">No answers submitted by this student.</div>
                                  ) : (
                                    <div className="space-y-8">
                                      {student.answers.map((answer) => {
                                        // Initialize input overrides if empty
                                        if (overrideMarks[answer.answerId] === undefined) {
                                          const defaultMark = answer.teacherOverride 
                                            ? answer.teacherOverride.finalMarks 
                                            : (answer.aiGrading ? answer.aiGrading.suggestedMarks : 0);
                                          overrideMarks[answer.answerId] = defaultMark;
                                          overrideNotes[answer.answerId] = answer.teacherOverride?.note || '';
                                        }

                                        return (
                                          <div key={answer.answerId} className="grid grid-cols-1 lg:grid-cols-12 gap-8 border-b border-slate-100 pb-8 last:border-0 last:pb-0">
                                            {/* Left: Code / Answer Block */}
                                            <div className="lg:col-span-7">
                                              <div className="flex justify-between items-center mb-2">
                                                <h4 className="text-sm font-bold text-slate-800">{answer.questionText}</h4>
                                                <span className="px-2 py-0.5 bg-slate-100 rounded text-[9px] font-bold text-slate-500 uppercase tracking-wider">
                                                  {answer.questionType} • MAX {answer.marks} MARKS
                                                </span>
                                              </div>
                                              
                                              <div className="bg-slate-900 text-slate-300 font-mono text-xs p-4 rounded-xl overflow-x-auto shadow-inner leading-relaxed max-h-[350px]">
                                                <pre><code>{answer.answerText}</code></pre>
                                              </div>
                                            </div>

                                            {/* Right: AI & Override grading */}
                                            <div className="lg:col-span-5 flex flex-col gap-4">
                                              {/* AI Evaluation */}
                                              {answer.aiGrading ? (
                                                <div className="bg-white border border-slate-200 p-5 rounded-xl shadow-sm">
                                                  <div className="flex justify-between items-start mb-3">
                                                    <div className="flex items-center gap-1.5">
                                                      <span className="material-symbols-outlined text-primary-500 text-[20px]">smart_toy</span>
                                                      <span className="font-bold text-xs text-slate-800 uppercase">AI Evaluation</span>
                                                    </div>
                                                    <span className={`px-2 py-0.5 rounded-full text-[9px] font-bold uppercase tracking-wider ${
                                                      answer.aiGrading.confidence === 'High' 
                                                        ? 'bg-green-50 text-green-700 border border-green-150' 
                                                        : 'bg-yellow-50 text-yellow-700 border border-yellow-150'
                                                    }`}>
                                                      {answer.aiGrading.confidence} Confidence
                                                    </span>
                                                  </div>
                                                  
                                                  <div className="mb-3">
                                                    <span className="text-[10px] text-slate-400 font-bold uppercase block">SUGGESTED MARK</span>
                                                    <span className="text-2xl font-bold text-primary-500">{answer.aiGrading.suggestedMarks} <span className="text-xs text-slate-400">/ {answer.marks}</span></span>
                                                  </div>

                                                  <div>
                                                    <span className="text-[10px] text-slate-400 font-bold uppercase block mb-1">JUSTIFICATION</span>
                                                    <p className="text-xs text-slate-600 leading-relaxed bg-slate-50 p-2.5 rounded-lg border border-slate-100">{answer.aiGrading.justification}</p>
                                                  </div>
                                                </div>
                                              ) : (
                                                <div className="bg-white border border-slate-200 p-4 rounded-xl text-center text-xs text-slate-400 italic">
                                                  AI Auto-grading is not available or disabled for this question type.
                                                </div>
                                              )}

                                              {/* Teacher Override Form */}
                                              <div className="bg-white border border-slate-200 p-5 rounded-xl shadow-sm space-y-3">
                                                <span className="font-bold text-xs text-slate-800 uppercase block">Teacher Grading Override</span>
                                                
                                                <div className="grid grid-cols-3 gap-2">
                                                  <div className="col-span-1">
                                                    <label className="text-[9px] font-bold text-slate-400 uppercase tracking-wider block mb-1">Final Marks</label>
                                                    <input 
                                                      type="number"
                                                      min="0"
                                                      max={answer.marks}
                                                      step="0.5"
                                                      value={overrideMarks[answer.answerId] || 0}
                                                      onChange={e => setOverrideMarks(prev => ({ ...prev, [answer.answerId]: Math.min(answer.marks, Math.max(0, parseFloat(e.target.value) || 0)) }))}
                                                      className="w-full bg-white border border-slate-200 rounded-lg p-2 text-sm text-center font-bold text-slate-800 outline-none focus:border-primary-500 focus:ring-1 focus:ring-primary-500/10"
                                                    />
                                                  </div>
                                                  <div className="col-span-2">
                                                    <label className="text-[9px] font-bold text-slate-400 uppercase tracking-wider block mb-1">Notes / Feedbacks</label>
                                                    <input 
                                                      type="text"
                                                      value={overrideNotes[answer.answerId] || ''}
                                                      onChange={e => setOverrideNotes(prev => ({ ...prev, [answer.answerId]: e.target.value }))}
                                                      placeholder="e.g. Minor syntax error fixed (-1)"
                                                      className="w-full bg-white border border-slate-200 rounded-lg p-2 text-xs text-slate-700 outline-none focus:border-primary-500 focus:ring-1 focus:ring-primary-500/10"
                                                    />
                                                  </div>
                                                </div>

                                                <button 
                                                  onClick={() => handleApplyOverride(answer.answerId)}
                                                  disabled={submittingOverride[answer.answerId]}
                                                  className="w-full py-2 bg-primary-500 text-white font-bold text-xs rounded-lg hover:bg-primary-600 transition-all disabled:opacity-50 flex items-center justify-center gap-1.5"
                                                >
                                                  {submittingOverride[answer.answerId] && (
                                                    <div className="w-3.5 h-3.5 border-2 border-white border-t-transparent rounded-full animate-spin" />
                                                  )}
                                                  Save Grade Override
                                                </button>
                                              </div>

                                            </div>
                                          </div>
                                        );
                                      })}
                                    </div>
                                  )}
                                </td>
                              </tr>
                            )}
                          </React.Fragment>
                        );
                      })}
                    </tbody>
                  </table>
                )}
              </div>
            )}

            {/* PLAGIARISM TAB */}
            {activeTab === 'plagiarism' && (
              <div className="space-y-6">
                {plagiarism.length === 0 ? (
                  <div className="bg-white border border-slate-200 p-12 text-center rounded-xl text-slate-400 italic">
                    <span className="material-symbols-outlined text-[48px] text-green-200 mb-2">verified</span>
                    <p className="text-sm font-semibold text-slate-600">No plagiarism flags detected.</p>
                    <p className="text-xs text-slate-400 mt-1">All submissions fall within acceptable similarity limits.</p>
                  </div>
                ) : (
                  plagiarism.map(flag => (
                    <div key={flag.plagId} className="bg-white border border-slate-200 rounded-xl overflow-hidden shadow-sm">
                      <div className="px-6 py-4 bg-red-50/50 border-b border-red-100 flex items-center justify-between">
                        <div className="flex items-center gap-4">
                          <div className="w-10 h-10 rounded-full bg-red-100 text-red-600 font-bold flex items-center justify-center text-xs">
                            PL
                          </div>
                          <div>
                            <span className="font-bold text-slate-800 text-sm block">
                              {flag.studentAName} vs. {flag.studentBName}
                            </span>
                            <span className="text-[10px] font-bold text-red-600 uppercase mt-0.5 block tracking-wider">
                              {flag.similarityScore}% Similarity Match
                            </span>
                          </div>
                        </div>
                        <span className="px-3 py-1 bg-red-500 text-white rounded-lg text-[9px] font-bold uppercase tracking-wider">
                          Action Required
                        </span>
                      </div>
                      
                      <div className="p-6">
                        <h4 className="text-xs font-bold text-slate-400 uppercase tracking-widest mb-3">Matched Question: {flag.questionText}</h4>
                        
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                          <div>
                            <p className="text-[10px] font-bold text-slate-400 mb-1.5 uppercase">{flag.studentAName} - Code</p>
                            <div className="bg-slate-900 text-slate-300 font-mono text-xs p-4 rounded-xl overflow-y-auto h-[250px] shadow-inner leading-relaxed">
                              {/* Display mock highlighted lines inside plagiarism comparator */}
                              <pre><code>{`# Sequence algorithm
a, b = 0, 1
for i in range(n):
    a, b = b, a + b
return a`}</code></pre>
                            </div>
                          </div>
                          <div>
                            <p className="text-[10px] font-bold text-slate-400 mb-1.5 uppercase">{flag.studentBName} - Code</p>
                            <div className="bg-slate-900 text-slate-300 font-mono text-xs p-4 rounded-xl overflow-y-auto h-[250px] shadow-inner leading-relaxed">
                              <pre><code>{`# Sequence algorithm
x, y = 0, 1
for k in range(val):
    x, y = y, x + y
return x`}</code></pre>
                            </div>
                          </div>
                        </div>

                        <div className="mt-4 flex justify-end gap-3 border-t border-slate-100 pt-4">
                          <button 
                            onClick={() => alert('Flag dismissed.')}
                            className="px-4 py-2 border border-slate-200 text-slate-600 text-xs font-semibold rounded-lg hover:bg-slate-50 transition-colors"
                          >
                            Dismiss Flag
                          </button>
                          <button 
                            onClick={() => alert('Marked as plagiarized.')}
                            className="px-4 py-2 bg-red-600 text-white text-xs font-semibold rounded-lg hover:bg-red-700 transition-colors"
                          >
                            Mark as Plagiarized
                          </button>
                        </div>

                      </div>
                    </div>
                  ))
                )}
              </div>
            )}

            {/* GRADE SUMMARY TAB */}
            {activeTab === 'summary' && (
              <div className="bg-white border border-slate-200 rounded-xl p-12 flex flex-col items-center text-center shadow-sm">
                <div className="w-16 h-16 bg-blue-50 text-primary-500 rounded-full flex items-center justify-center mb-6">
                  <span className="material-symbols-outlined text-[36px]">summarize</span>
                </div>
                <h4 className="text-lg font-bold text-slate-800 mb-2">Class Performance Analysis</h4>
                <p className="text-sm text-slate-400 max-w-sm mb-6">
                  Summary analytics, distribution curves, and grade sheets will generate when all exam submissions have been reviewed.
                </p>
                <div className="w-full max-w-md bg-slate-100 rounded-full h-2.5 mb-2 overflow-hidden">
                  <div 
                    className="bg-primary-500 h-full rounded-full transition-all duration-500" 
                    style={{ width: `${results.length > 0 ? (submissionCount / results.length) * 100 : 0}%` }}
                  ></div>
                </div>
                <p className="text-[10px] font-bold text-slate-400 uppercase tracking-widest">
                  {submissionCount} of {results.length} submissions graded ({results.length > 0 ? Math.round((submissionCount / results.length) * 100) : 0}%)
                </p>
              </div>
            )}
          </>
        )}

      </div>
    </AppLayout>
  );
}

// React mock to prevent compile error in React.Fragment loop
import React from 'react';
