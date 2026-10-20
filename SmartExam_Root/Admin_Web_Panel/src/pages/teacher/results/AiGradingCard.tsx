import type { AiGradingView, CodeAnalysisView, TestCaseResultView } from '../../../types';

// Module 11 — the AI evaluation of one answer as the teacher sees it: status, suggested mark and confidence,
// justification, the per-test-case table (hidden cases included, marked), compiler output, static-analysis
// signals and the optional LLM summary. Suggestions only — the override form next to it sets the final mark.

const STATUS_STYLE: Record<string, string> = {
  Passed: 'bg-green-50 text-green-700 border-green-200',
  Failed: 'bg-red-50 text-red-700 border-red-200',
  TimedOut: 'bg-amber-50 text-amber-700 border-amber-200',
  MemoryExceeded: 'bg-amber-50 text-amber-700 border-amber-200',
  RuntimeError: 'bg-red-50 text-red-700 border-red-200',
  NotRun: 'bg-slate-50 text-slate-500 border-slate-200',
  Pending: 'bg-blue-50 text-blue-600 border-blue-200',
};

const STATUS_LABEL: Record<string, string> = {
  Passed: 'Pass',
  Failed: 'Fail',
  TimedOut: 'Time limit',
  MemoryExceeded: 'Memory limit',
  RuntimeError: 'Crashed',
  NotRun: 'Not run',
  Pending: 'Queued',
};

function short(text: string | null | undefined, max = 60) {
  if (text === null || text === undefined || text === '') return '—';
  const t = text.replace(/\r?\n/g, '⏎');
  return t.length > max ? t.slice(0, max) + '…' : t;
}

export function GradingStatusChip({ grading }: { grading: AiGradingView }) {
  const status = grading.status ?? 'Graded';
  if (status === 'Pending') {
    return (
      <span data-testid="grading-status" className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[9px] font-bold uppercase tracking-wider bg-blue-50 text-blue-600 border border-blue-200">
        <span className="w-2.5 h-2.5 border-2 border-blue-500 border-t-transparent rounded-full animate-spin" />
        Grading…
      </span>
    );
  }
  return (
    <span data-testid="grading-status" className={`px-2 py-0.5 rounded-full text-[9px] font-bold uppercase tracking-wider border ${
      status === 'Failed' ? 'bg-red-50 text-red-700 border-red-200' : 'bg-slate-50 text-slate-600 border-slate-200'}`}>
      {status === 'Failed' ? 'Grading failed' : 'Graded'}
    </span>
  );
}

function TestCaseTable({ cases }: { cases: TestCaseResultView[] }) {
  return (
    <div className="overflow-x-auto border border-slate-100 rounded-lg">
      <table className="w-full text-left text-[11px]">
        <thead className="bg-slate-50 text-[9px] font-bold uppercase tracking-wider text-slate-400">
          <tr>
            <th className="px-2 py-1.5">Case</th>
            <th className="px-2 py-1.5">Input</th>
            <th className="px-2 py-1.5">Expected</th>
            <th className="px-2 py-1.5">Actual</th>
            <th className="px-2 py-1.5">Result</th>
            <th className="px-2 py-1.5 text-right">ms</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {cases.map((c, i) => (
            <tr key={`${c.testCaseId ?? 'compile'}-${i}`} data-testid="testcase-result" className="align-top">
              <td className="px-2 py-1.5 whitespace-nowrap font-semibold text-slate-700">
                {c.name}
                {c.isHidden && <span className="ml-1 px-1 rounded bg-slate-800 text-white text-[8px] font-bold uppercase">Hidden</span>}
                {c.isEdgeCase && <span className="ml-1 px-1 rounded bg-indigo-50 text-indigo-600 text-[8px] font-bold uppercase">Edge</span>}
                {c.weight !== 1 && <span className="ml-1 text-[9px] text-slate-400">×{c.weight}</span>}
              </td>
              <td className="px-2 py-1.5 font-mono text-slate-500" title={c.input}>{short(c.input, 24)}</td>
              <td className="px-2 py-1.5 font-mono text-slate-500" title={c.expectedOutput}>{short(c.expectedOutput, 24)}</td>
              <td className="px-2 py-1.5 font-mono text-slate-700" title={c.actualOutput ?? ''}>{c.status === 'Pending' ? '…' : short(c.actualOutput, 24)}</td>
              <td className="px-2 py-1.5 whitespace-nowrap">
                <span className={`px-1.5 py-0.5 rounded border text-[9px] font-bold uppercase ${STATUS_STYLE[c.status] ?? STATUS_STYLE.NotRun}`}
                      title={c.exitCode !== null && c.exitCode !== 0 ? `Exit code ${c.exitCode}` : c.compareMode ?? undefined}>
                  {STATUS_LABEL[c.status] ?? c.status}
                </span>
              </td>
              <td className="px-2 py-1.5 text-right font-mono text-slate-400">{c.durationMs ?? '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function AnalysisBlock({ analysis }: { analysis: CodeAnalysisView }) {
  const suspicionStyle = analysis.hardcodingSuspicion === 'High'
    ? 'bg-red-50 text-red-700 border-red-200'
    : analysis.hardcodingSuspicion === 'Medium' ? 'bg-amber-50 text-amber-700 border-amber-200' : 'bg-green-50 text-green-700 border-green-200';
  return (
    <div data-testid="code-analysis" className="space-y-2">
      <span className="text-[10px] text-slate-400 font-bold uppercase block">Code analysis (signals)</span>
      <div className="flex flex-wrap gap-1.5 text-[10px] font-semibold">
        <span className="px-2 py-0.5 rounded bg-slate-100 text-slate-600" title="From the deepest loop nesting">Complexity {analysis.complexitySignal}</span>
        <span className="px-2 py-0.5 rounded bg-slate-100 text-slate-600">{analysis.loopCount} loop(s), nesting {analysis.maxLoopNesting}</span>
        {analysis.usesRecursion && (
          <span className="px-2 py-0.5 rounded bg-slate-100 text-slate-600">Recursion: {analysis.recursiveFunctions.join(', ')}</span>
        )}
        <span className={`px-2 py-0.5 rounded border ${suspicionStyle}`}>Hard-coding suspicion: {analysis.hardcodingSuspicion}</span>
        {analysis.nearCorrect && <span className="px-2 py-0.5 rounded bg-indigo-50 text-indigo-600">Near-correct</span>}
      </div>
      {analysis.hardcodingSignals.length > 0 && (
        <ul className="list-disc pl-4 text-[11px] text-slate-600 space-y-0.5">
          {analysis.hardcodingSignals.map(s => <li key={s}>{s}</li>)}
        </ul>
      )}
    </div>
  );
}

interface Props {
  grading: AiGradingView;
  maxMarks: number;
  onRegrade?: () => void;
  regrading?: boolean;
}

export default function AiGradingCard({ grading, maxMarks, onRegrade, regrading }: Props) {
  const pending = grading.status === 'Pending';
  const notIsolated = (grading.engine ?? '').toLowerCase().includes('not isolated');
  const cases = grading.testCaseResults ?? [];

  return (
    <div className="bg-white border border-slate-200 p-5 rounded-xl shadow-sm space-y-3">
      <div className="flex justify-between items-start gap-2">
        <div className="flex items-center gap-1.5">
          <span className="material-symbols-outlined text-primary-500 text-[20px]">smart_toy</span>
          <span className="font-bold text-xs text-slate-800 uppercase">AI Evaluation</span>
        </div>
        <div className="flex items-center gap-1.5 flex-wrap justify-end">
          <GradingStatusChip grading={grading} />
          {!pending && (
            <span className={`px-2 py-0.5 rounded-full text-[9px] font-bold uppercase tracking-wider ${
              grading.confidence === 'High'
                ? 'bg-green-50 text-green-700 border border-green-150'
                : 'bg-yellow-50 text-yellow-700 border border-yellow-150'
            }`}>
              {grading.confidence} Confidence
            </span>
          )}
        </div>
      </div>

      <div className="flex items-end justify-between gap-2">
        <div>
          <span className="text-[10px] text-slate-400 font-bold uppercase block">SUGGESTED MARK</span>
          <span className="text-2xl font-bold text-primary-500">
            {pending ? '…' : grading.suggestedMarks} <span className="text-xs text-slate-400">/ {maxMarks}</span>
          </span>
        </div>
        {grading.engine && (
          <div className="text-right">
            <span className="text-[9px] text-slate-400 font-bold uppercase block">Engine</span>
            <span className="text-[10px] font-semibold text-slate-600">{grading.engine}</span>
            {notIsolated && (
              <span data-testid="engine-warning" className="block mt-0.5 px-1.5 py-0.5 rounded bg-amber-50 border border-amber-200 text-amber-700 text-[9px] font-bold uppercase"
                    title="The local runner has no network or file-system isolation. Use Docker on the exam server.">
                Not isolated
              </span>
            )}
          </div>
        )}
      </div>

      <div>
        <span className="text-[10px] text-slate-400 font-bold uppercase block mb-1">JUSTIFICATION</span>
        <p className="text-xs text-slate-600 leading-relaxed bg-slate-50 p-2.5 rounded-lg border border-slate-100 whitespace-pre-line">{grading.justification}</p>
      </div>

      {cases.length > 0 && (
        <div>
          <span className="text-[10px] text-slate-400 font-bold uppercase block mb-1">
            Test cases ({cases.filter(c => c.passed).length}/{cases.length} passed)
          </span>
          <TestCaseTable cases={cases} />
        </div>
      )}

      {grading.compileOutput && (
        <details className="text-[11px]">
          <summary className="cursor-pointer text-[10px] text-slate-400 font-bold uppercase">Compiler output</summary>
          <pre className="mt-1 bg-slate-900 text-slate-200 p-2.5 rounded-lg overflow-x-auto max-h-48 whitespace-pre-wrap">{grading.compileOutput}</pre>
        </details>
      )}

      {grading.analysis && <AnalysisBlock analysis={grading.analysis} />}

      {grading.llmSummary && (
        <div>
          <span className="text-[10px] text-slate-400 font-bold uppercase block mb-1">LLM summary (assistive)</span>
          <p className="text-xs text-slate-600 whitespace-pre-line bg-indigo-50/40 border border-indigo-100 rounded-lg p-2.5">{grading.llmSummary}</p>
        </div>
      )}

      {onRegrade && !pending && (
        <button
          type="button"
          onClick={onRegrade}
          disabled={regrading}
          className="text-[11px] font-semibold text-primary-500 hover:underline disabled:opacity-50"
        >
          {regrading ? 'Queuing…' : 'Re-run grading'}
        </button>
      )}
      {grading.durationMs != null && !pending && (
        <span className="ml-3 text-[10px] text-slate-400">Run time {grading.durationMs} ms</span>
      )}
    </div>
  );
}
