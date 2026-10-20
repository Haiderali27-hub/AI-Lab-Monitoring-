import React, { useCallback, useEffect, useRef, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import AppLayout from '../../components/layout/AppLayout';
import PlagiarismHeatmap from '../../components/analytics/PlagiarismHeatmap';
import { examsApi } from '../../api/exams.api';
import { useToast } from '../../context/ToastContext';
import type { AnswerResult, PlagiarismFlag, StudentResultRow } from '../../types';
import AiGradingCard from './results/AiGradingCard';
import PlagiarismPairView, { parseMatchingSegments } from './results/PlagiarismPairView';

// Finalised sessions (graded, receipt fixed); TimedOut = the server closed it at the deadline.
const FINALISED = ['Submitted', 'ForceSubmitted', 'TimedOut'];

// Module 11: coding answers are graded in the background; while any is Pending the page re-reads the results.
const PENDING_POLL_MS = 4000;

const isPending = (a: AnswerResult) => a.aiGrading?.status === 'Pending';

export default function ResultsPage() {
  const { examId } = useParams<{ examId: string }>();
  const navigate = useNavigate();
  const { showToast } = useToast();

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [examTitle, setExamTitle] = useState('Assessment Results');

  const [results, setResults] = useState<StudentResultRow[]>([]);
  const [plagiarism, setPlagiarism] = useState<PlagiarismFlag[]>([]);

  const [activeTab, setActiveTab] = useState<'submissions' | 'plagiarism' | 'summary'>('submissions');
  const [expandedStudentId, setExpandedStudentId] = useState<string | null>(null);
  const [focusedPair, setFocusedPair] = useState<string | null>(null);

  // Grade override form state (absent = show the current override / AI suggestion)
  const [overrideMarks, setOverrideMarks] = useState<{ [answerId: string]: number }>({});
  const [overrideNotes, setOverrideNotes] = useState<{ [answerId: string]: string }>({});
  const [submittingOverride, setSubmittingOverride] = useState<{ [answerId: string]: boolean }>({});
  const [regrading, setRegrading] = useState<{ [answerId: string]: boolean }>({});
  const [accepting, setAccepting] = useState(false);
  const flagRefs = useRef<{ [key: string]: HTMLDivElement | null }>({});

  const loadData = useCallback(async () => {
    if (!examId) {
      setLoading(false);
      setExamTitle('No exam selected');
      return;
    }
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
      setError(err?.response?.status === 403
        ? 'Only the exam’s teacher or an admin can see its results.'
        : 'Failed to fetch exam results. Please ensure backend is active.');
    } finally {
      setLoading(false);
    }
  }, [examId]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  // Re-read quietly (no spinner) while the background grader is still working on any answer.
  const anyPending = results.some(r => r.answers.some(isPending));
  useEffect(() => {
    if (!examId || !anyPending) return;
    const timer = window.setInterval(async () => {
      try {
        setResults(await examsApi.getResults(examId));
      } catch {
        // Keep the last good data; the next tick retries.
      }
    }, PENDING_POLL_MS);
    return () => window.clearInterval(timer);
  }, [examId, anyPending]);

  const markFor = (answer: AnswerResult) =>
    overrideMarks[answer.answerId]
    ?? answer.teacherOverride?.finalMarks
    ?? (answer.aiGrading && !isPending(answer) ? answer.aiGrading.suggestedMarks : 0);

  const handleApplyOverride = async (answer: AnswerResult) => {
    const marks = markFor(answer);
    const note = overrideNotes[answer.answerId] ?? answer.teacherOverride?.note ?? '';

    if (marks === undefined || isNaN(marks)) {
      alert('Please enter a valid mark.');
      return;
    }

    try {
      setSubmittingOverride(prev => ({ ...prev, [answer.answerId]: true }));
      await examsApi.overrideGrade(answer.answerId, marks, note);

      if (examId) setResults(await examsApi.getResults(examId));
      alert('Grade override applied successfully!');
    } catch (err) {
      console.error('Failed to apply override', err);
      alert('Failed to save grade override.');
    } finally {
      setSubmittingOverride(prev => ({ ...prev, [answer.answerId]: false }));
    }
  };

  const handleRegrade = async (answerId: string) => {
    try {
      setRegrading(prev => ({ ...prev, [answerId]: true }));
      await examsApi.regradeAnswer(answerId);
      if (examId) setResults(await examsApi.getResults(examId));
      showToast('Regrade queued', 'The answer will be compiled and graded again.', 'success');
    } catch (err: any) {
      showToast('Regrade failed', err?.response?.data?.message ?? 'Could not queue the answer.', 'error');
    } finally {
      setRegrading(prev => ({ ...prev, [answerId]: false }));
    }
  };

  const handleAcceptAll = async () => {
    if (!examId) return;
    if (!window.confirm('Accept the AI suggestion as the final mark for every graded answer that has no override yet?')) return;
    try {
      setAccepting(true);
      const res = await examsApi.acceptAiSuggestions(examId);
      setResults(await examsApi.getResults(examId));
      setOverrideMarks({});
      showToast('AI suggestions accepted', res.message, 'success');
    } catch (err: any) {
      showToast('Accept failed', err?.response?.data?.message ?? 'Could not accept the suggestions.', 'error');
    } finally {
      setAccepting(false);
    }
  };

  // Calculate statistics
  const submissionCount = results.filter(r => FINALISED.includes(r.status)).length;
  const pendingCount = results.reduce((n, r) => n + r.answers.filter(isPending).length, 0);

  const getStudentTotalScore = (student: StudentResultRow) =>
    student.answers.reduce((sum, a) => {
      if (a.teacherOverride) return sum + a.teacherOverride.finalMarks;
      if (a.aiGrading) return sum + a.aiGrading.suggestedMarks;
      return sum;
    }, 0);

  const getStudentMaxScore = (student: StudentResultRow) =>
    student.answers.reduce((sum, a) => sum + a.marks, 0);

  // Class statistics computation
  const scores = results.map(r => {
    const max = getStudentMaxScore(r);
    const score = getStudentTotalScore(r);
    return max > 0 ? (score / max) * 100 : 0;
  });

  const classAverage = scores.length > 0 ? Math.round(scores.reduce((a, b) => a + b, 0) / scores.length) : 0;
  const highestScore = scores.length > 0 ? Math.round(Math.max(...scores)) : 0;
  const lowestScore = scores.length > 0 ? Math.round(Math.min(...scores)) : 0;

  // Full answer texts for the side-by-side plagiarism view (key: userId + questionId).
  const answerText = (userId: string | undefined, questionId: string) => {
    if (!userId) return undefined;
    return results.find(r => r.userId === userId)?.answers.find(a => a.questionId === questionId)?.answerText;
  };

  const focusPair = (a: { userId: string }, b: { userId: string }) => {
    const flag = plagiarism.find(f =>
      (f.userIdA === a.userId && f.userIdB === b.userId) || (f.userIdA === b.userId && f.userIdB === a.userId));
    if (!flag) {
      showToast('Below the threshold', 'This pair was not flagged; open the matrix by question to compare it.', 'info');
      return;
    }
    setFocusedPair(flag.plagId);
    flagRefs.current[flag.plagId]?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

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

          <div className="flex flex-wrap items-baseline justify-between gap-3">
            <div className="flex items-baseline gap-3">
              <h3 className="text-2xl font-bold text-slate-800">{examTitle}</h3>
              <span className="px-2 py-0.5 bg-blue-50 border border-blue-100 rounded-lg text-[9px] font-bold text-primary-600 uppercase">
                {results.length > 0 ? 'Active Grading' : 'Pending submissions'}
              </span>
            </div>
            {results.length > 0 && (
              <button
                onClick={handleAcceptAll}
                disabled={accepting}
                className="px-3 py-1.5 border border-primary-200 text-primary-600 bg-white text-xs font-bold rounded-lg hover:bg-primary-50 disabled:opacity-50"
              >
                {accepting ? 'Accepting…' : 'Accept AI suggestions'}
              </button>
            )}
          </div>
          <p className="text-sm text-slate-400 mt-1">
            {submissionCount} of {results.length} Students Submitted
            {pendingCount > 0 && (
              <span data-testid="grading-pending" className="ml-2 text-blue-500">· {pendingCount} answer(s) still being graded…</span>
            )}
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
                  <div className="text-center py-12 text-slate-400 italic">
                    {!examId ? "Please select an exam to view results." : "No submissions found for this exam."}
                  </div>
                ) : (
                  <table className="w-full text-left border-collapse">
                    <thead className="bg-slate-50 border-b border-slate-200 text-xs font-bold text-slate-500 uppercase tracking-wider">
                      <tr>
                        <th className="px-6 py-4">Student Name</th>
                        <th className="px-6 py-4">Status</th>
                        <th className="px-6 py-4 text-center">Score</th>
                        <th className="px-6 py-4">Receipt</th>
                        <th className="px-6 py-4 text-right">Actions</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                      {results.map(student => {
                        const isExpanded = expandedStudentId === student.userId;
                        const score = getStudentTotalScore(student);
                        const maxScore = getStudentMaxScore(student);
                        const studentPending = student.answers.some(isPending);

                        return (
                          <React.Fragment key={student.userId}>
                            <tr className="hover:bg-slate-50/50 transition-colors">
                              <td className="px-6 py-4">
                                <div className="font-semibold text-slate-800">{student.studentName}</div>
                                <div className="text-xs text-slate-400">{student.studentEmail}</div>
                              </td>
                              <td className="px-6 py-4">
                                <span className={`px-2.5 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider ${
                                  FINALISED.includes(student.status)
                                    ? 'bg-green-50 text-green-700 border border-green-200'
                                    : 'bg-yellow-50 text-yellow-700 border border-yellow-200'
                                }`}>
                                  {student.status}
                                </span>
                              </td>
                              <td className="px-6 py-4 text-center font-bold text-slate-800">
                                {score}/{maxScore}
                                {studentPending && <span className="block text-[10px] font-semibold text-blue-500">grading…</span>}
                              </td>
                              <td className="px-6 py-4 max-w-[10rem]">
                                {student.receiptSha256 ? (
                                  // Full hash in the DOM (CSS truncation only) so it can be compared with the student's copy.
                                  <code
                                    data-testid="receipt-hash"
                                    title={`SHA-256 over ${student.receiptAnswerCount ?? 0} answer(s): ${student.receiptSha256}`}
                                    className="block truncate font-mono text-[11px] text-slate-500"
                                  >{student.receiptSha256}</code>
                                ) : (
                                  <span className="text-xs text-slate-300">—</span>
                                )}
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
                                <td colSpan={5} className="px-8 py-6">
                                  {student.answers.length === 0 ? (
                                    <div className="text-center text-xs text-slate-400 italic py-4">No answers submitted by this student.</div>
                                  ) : (
                                    <div className="space-y-8">
                                      {student.answers.map((answer) => (
                                        <div key={answer.answerId} className="grid grid-cols-1 lg:grid-cols-12 gap-8 border-b border-slate-100 pb-8 last:border-0 last:pb-0">
                                          {/* Left: Code / Answer Block */}
                                          <div className="lg:col-span-6">
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
                                          <div className="lg:col-span-6 flex flex-col gap-4">
                                            {answer.aiGrading ? (
                                              <AiGradingCard
                                                grading={answer.aiGrading}
                                                maxMarks={answer.marks}
                                                onRegrade={answer.questionType !== 'Mcq' && FINALISED.includes(student.status)
                                                  ? () => handleRegrade(answer.answerId) : undefined}
                                                regrading={regrading[answer.answerId]}
                                              />
                                            ) : (
                                              <div className="bg-white border border-slate-200 p-4 rounded-xl text-center text-xs text-slate-400 italic">
                                                AI Auto-grading is not available or disabled for this question type.
                                              </div>
                                            )}

                                            {/* Teacher Override Form */}
                                            <div className="bg-white border border-slate-200 p-5 rounded-xl shadow-sm space-y-3">
                                              <div className="flex items-center justify-between">
                                                <span className="font-bold text-xs text-slate-800 uppercase block">Teacher Grading Override</span>
                                                {answer.teacherOverride && (
                                                  <span className="px-2 py-0.5 rounded-full bg-primary-50 text-primary-600 text-[9px] font-bold uppercase">Overridden</span>
                                                )}
                                              </div>

                                              <div className="grid grid-cols-3 gap-2">
                                                <div className="col-span-1">
                                                  <label className="text-[9px] font-bold text-slate-400 uppercase tracking-wider block mb-1">Final Marks</label>
                                                  <input
                                                    type="number"
                                                    min="0"
                                                    max={answer.marks}
                                                    step="0.5"
                                                    value={markFor(answer)}
                                                    onChange={e => setOverrideMarks(prev => ({ ...prev, [answer.answerId]: Math.min(answer.marks, Math.max(0, parseFloat(e.target.value) || 0)) }))}
                                                    className="w-full bg-white border border-slate-200 rounded-lg p-2 text-sm text-center font-bold text-slate-800 outline-none focus:border-primary-500 focus:ring-1 focus:ring-primary-500/10"
                                                  />
                                                </div>
                                                <div className="col-span-2">
                                                  <label className="text-[9px] font-bold text-slate-400 uppercase tracking-wider block mb-1">Notes / Feedbacks</label>
                                                  <input
                                                    type="text"
                                                    value={overrideNotes[answer.answerId] ?? answer.teacherOverride?.note ?? ''}
                                                    onChange={e => setOverrideNotes(prev => ({ ...prev, [answer.answerId]: e.target.value }))}
                                                    placeholder="e.g. Minor syntax error fixed (-1)"
                                                    className="w-full bg-white border border-slate-200 rounded-lg p-2 text-xs text-slate-700 outline-none focus:border-primary-500 focus:ring-1 focus:ring-primary-500/10"
                                                  />
                                                </div>
                                              </div>

                                              <button
                                                onClick={() => handleApplyOverride(answer)}
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
                                      ))}
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
                {examId && <PlagiarismHeatmap examId={examId} onSelectPair={focusPair} />}

                {plagiarism.length === 0 ? (
                  <div className="bg-white border border-slate-200 p-12 text-center rounded-xl text-slate-400 italic">
                    <span className="material-symbols-outlined text-[48px] text-green-200 mb-2">verified</span>
                    <p className="text-sm font-semibold text-slate-600">No plagiarism flags detected.</p>
                    <p className="text-xs text-slate-400 mt-1">All submissions fall within acceptable similarity limits.</p>
                  </div>
                ) : (
                  plagiarism.map(flag => (
                    <div
                      key={flag.plagId}
                      ref={el => { flagRefs.current[flag.plagId] = el; }}
                      data-testid="plagiarism-flag"
                      className={`bg-white border rounded-xl overflow-hidden shadow-sm ${focusedPair === flag.plagId ? 'border-red-400 ring-2 ring-red-200' : 'border-slate-200'}`}
                    >
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
                          Review Required
                        </span>
                      </div>

                      <div className="p-6">
                        <h4 className="text-xs font-bold text-slate-400 uppercase tracking-widest mb-3">Matched Question: {flag.questionText}</h4>
                        <PlagiarismPairView
                          nameA={flag.studentAName}
                          nameB={flag.studentBName}
                          segments={parseMatchingSegments(flag.matchingSegments)}
                          fullA={answerText(flag.userIdA, flag.questionId)}
                          fullB={answerText(flag.userIdB, flag.questionId)}
                        />
                        <p className="mt-4 border-t border-slate-100 pt-4 text-xs text-slate-400">
                          Similarity is evidence, not a verdict: identical code can come from shared starter code or a
                          standard algorithm. Record your decision with a grade override on the students' answers.
                        </p>
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
