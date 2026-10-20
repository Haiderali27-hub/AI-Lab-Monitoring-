import { useEffect, useMemo, useState } from 'react';
import { examsApi } from '../../api/exams.api';
import type { PlagiarismMatrix } from '../../types';

// Module 11 — class-wide plagiarism heat map: an n×n grid of pairwise similarity (0–100 %) from
// GET /exams/{id}/plagiarism/matrix, students in submission order. One hue, light → dark (sequential:
// darker = more similar); every cell also prints its value, so colour is never the only cue. The diagonal
// (a student against themselves) is neutral. Clicking an off-diagonal cell reports the pair to the parent.

interface Props {
  examId: string;
  /** Called with the two students of a clicked cell (row, column). */
  onSelectPair?: (a: { userId: string; name: string }, b: { userId: string; name: string }, similarity: number) => void;
}

// Sequential single-hue steps (red 50 → 700). Text ink flips to white on the two darkest steps.
const STEPS = [
  { min: 0, bg: '#fef2f2', ink: '#475569' },
  { min: 20, bg: '#fee2e2', ink: '#334155' },
  { min: 40, bg: '#fca5a5', ink: '#1e293b' },
  { min: 60, bg: '#ef4444', ink: '#ffffff' },
  { min: 80, bg: '#b91c1c', ink: '#ffffff' },
];

function stepFor(value: number) {
  let step = STEPS[0];
  for (const s of STEPS) if (value >= s.min) step = s;
  return step;
}

function initials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map(p => p[0]!.toUpperCase()).join('');
}

export default function PlagiarismHeatmap({ examId, onSelectPair }: Props) {
  const [questionId, setQuestionId] = useState('');
  const [data, setData] = useState<PlagiarismMatrix | null>(null);
  const [questions, setQuestions] = useState<PlagiarismMatrix['questions']>([]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [hover, setHover] = useState<{ i: number; j: number } | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError('');
    examsApi.getPlagiarismMatrix(examId, questionId || undefined)
      .then(m => {
        if (cancelled) return;
        setData(m);
        // The question list comes from the unfiltered matrix; keep it while a filter is applied.
        if (!questionId) setQuestions(m.questions);
      })
      .catch(err => {
        if (cancelled) return;
        const status = err?.response?.status;
        setError(status === 400 ? 'Practice exams are not checked for plagiarism.'
          : status === 403 ? 'Only the exam’s teacher or an admin can see the similarity matrix.'
          : 'Could not load the similarity matrix.');
        setData(null);
      })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [examId, questionId]);

  const n = data?.students.length ?? 0;
  const highest = useMemo(() => {
    if (!data) return 0;
    let max = 0;
    data.matrix.forEach((row, i) => row.forEach((v, j) => { if (i !== j) max = Math.max(max, v); }));
    return max;
  }, [data]);

  return (
    <section className="bg-white border border-slate-200 rounded-xl p-6 shadow-sm">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 mb-4">
        <div>
          <h3 className="text-base font-bold text-slate-800">Plagiarism heat map</h3>
          <p className="text-xs text-slate-400 mt-0.5">
            Pairwise similarity of submitted answers (identifier- and comment-insensitive for code). Darker = more similar.
          </p>
        </div>
        <select
          aria-label="Question"
          value={questionId}
          onChange={e => setQuestionId(e.target.value)}
          className="bg-slate-50 border border-slate-200 rounded-lg px-3 py-1.5 text-xs text-slate-700 font-medium focus:outline-none focus:ring-2 focus:ring-primary-500"
        >
          <option value="">All questions (highest per pair)</option>
          {questions.map(q => (
            <option key={q.questionId} value={q.questionId}>
              Q{q.orderIndex} · {q.type} — {q.bodyText.length > 40 ? q.bodyText.slice(0, 40) + '…' : q.bodyText}
            </option>
          ))}
        </select>
      </div>

      {error && <p className="text-sm text-slate-500 bg-slate-50 border border-slate-200 rounded-lg px-4 py-3">{error}</p>}
      {!error && loading && !data && <div className="h-24 flex items-center justify-center text-xs text-slate-400">Loading similarity…</div>}
      {!error && data && n === 0 && (
        <p className="text-sm text-slate-400 italic">No submitted answers to compare yet.</p>
      )}

      {!error && data && n > 0 && (
        <>
          <div className="overflow-x-auto">
            <div
              data-testid="plagiarism-heatmap"
              role="grid"
              aria-label="Pairwise similarity matrix"
              className="inline-grid gap-[2px]"
              style={{ gridTemplateColumns: `minmax(7rem, auto) repeat(${n}, 2.75rem)` }}
            >
              <div />
              {data.students.map(s => (
                <div key={s.userId} title={s.name} className="text-[10px] font-bold text-slate-500 text-center pb-1 truncate">
                  {initials(s.name)}
                </div>
              ))}
              {data.students.map((rowStudent, i) => (
                <div key={rowStudent.userId} className="contents" role="row">
                  <div className="text-xs font-semibold text-slate-600 pr-2 flex items-center truncate" title={rowStudent.name}>
                    {rowStudent.name}
                  </div>
                  {data.matrix[i].map((value, j) => {
                    const diagonal = i === j;
                    const step = stepFor(value);
                    const isHover = hover && ((hover.i === i && hover.j === j) || (hover.i === j && hover.j === i));
                    return (
                      <button
                        key={j}
                        type="button"
                        role="gridcell"
                        data-cell={`${i}-${j}`}
                        data-value={value}
                        disabled={diagonal}
                        title={diagonal ? rowStudent.name : `${rowStudent.name} ↔ ${data.students[j].name}: ${value}%`}
                        onMouseEnter={() => setHover({ i, j })}
                        onMouseLeave={() => setHover(null)}
                        onClick={() => !diagonal && onSelectPair?.(rowStudent, data.students[j], value)}
                        className={`h-11 rounded text-[11px] font-bold font-mono transition-shadow ${diagonal ? 'cursor-default' : 'cursor-pointer'} ${isHover && !diagonal ? 'ring-2 ring-slate-800' : ''}`}
                        style={diagonal ? { background: '#f1f5f9', color: '#94a3b8' } : { background: step.bg, color: step.ink }}
                      >
                        {diagonal ? '—' : Math.round(value)}
                      </button>
                    );
                  })}
                </div>
              ))}
            </div>
          </div>

          {/* Legend: the five steps with their lower bounds, plus the exam's flag threshold. */}
          <div className="mt-4 flex flex-wrap items-center gap-4 text-[11px] text-slate-500">
            <div className="flex items-center gap-1">
              {STEPS.map(s => (
                <span key={s.min} className="flex items-center gap-1 mr-2">
                  <span className="inline-block w-4 h-3 rounded-sm border border-slate-200" style={{ background: s.bg }} />
                  {s.min}%+
                </span>
              ))}
            </div>
            <span>Flag threshold: <b className="text-slate-700">{data.threshold}%</b></span>
            <span>Highest pair: <b className="text-slate-700">{highest}%</b></span>
            {hover && hover.i !== hover.j && (
              <span className="text-slate-700">
                {data.students[hover.i].name} ↔ {data.students[hover.j].name}: <b>{data.matrix[hover.i][hover.j]}%</b>
              </span>
            )}
          </div>
        </>
      )}
    </section>
  );
}
