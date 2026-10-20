import type { MatchedSegment } from '../../../types';

// Module 11 — side-by-side view of one plagiarism pair. When both full answers are known the code is shown with
// line numbers and the matched line ranges highlighted; otherwise the stored excerpts are shown.

// Parse the backend's MatchingSegments JSON ([{ aStartLine, aEndLine, bStartLine, bEndLine, A, B }]; older rows
// carry only { A, B }). Returns [] for missing/empty/malformed input so callers can show a safe fallback.
export function parseMatchingSegments(raw?: string): MatchedSegment[] {
  if (!raw) return [];
  try {
    const parsed = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    return parsed
      .filter((s: any) => s && (typeof s.A === 'string' || typeof s.B === 'string'))
      .map((s: any) => ({
        A: s.A ?? '',
        B: s.B ?? '',
        aStartLine: typeof s.aStartLine === 'number' ? s.aStartLine : undefined,
        aEndLine: typeof s.aEndLine === 'number' ? s.aEndLine : undefined,
        bStartLine: typeof s.bStartLine === 'number' ? s.bStartLine : undefined,
        bEndLine: typeof s.bEndLine === 'number' ? s.bEndLine : undefined,
      }));
  } catch {
    return [];
  }
}

function highlighted(ranges: [number | undefined, number | undefined][]) {
  const set = new Set<number>();
  for (const [from, to] of ranges) {
    if (from === undefined || to === undefined) continue;
    for (let l = from; l <= to; l++) set.add(l);
  }
  return set;
}

function CodePane({ title, text, lines }: { title: string; text: string; lines: Set<number> }) {
  return (
    <div>
      <p className="text-[10px] font-bold text-slate-400 mb-1.5 uppercase">{title}</p>
      <div className="bg-slate-900 text-slate-300 font-mono text-xs py-3 rounded-xl overflow-auto h-[260px] shadow-inner leading-relaxed">
        {text.replace(/\r\n/g, '\n').split('\n').map((line, i) => {
          const n = i + 1;
          const hit = lines.has(n);
          return (
            <div key={n} data-matched={hit || undefined} className={`flex px-3 ${hit ? 'bg-red-500/25' : ''}`}>
              <span className="w-8 shrink-0 text-right pr-3 text-slate-500 select-none">{n}</span>
              <pre className="whitespace-pre">{line || ' '}</pre>
            </div>
          );
        })}
      </div>
    </div>
  );
}

interface Props {
  nameA: string;
  nameB: string;
  segments: MatchedSegment[];
  /** Full answer texts, when available from the results. */
  fullA?: string;
  fullB?: string;
}

export default function PlagiarismPairView({ nameA, nameB, segments, fullA, fullB }: Props) {
  if (segments.length === 0) {
    return (
      <div className="flex items-center gap-3 bg-slate-50 border border-slate-200 rounded-xl px-4 py-4 text-sm text-slate-500">
        <span className="material-symbols-outlined text-slate-400 text-[20px]">info</span>
        <span>No matched regions were stored for this pair.</span>
      </div>
    );
  }

  const hasRanges = segments.some(s => s.aStartLine !== undefined);
  if (hasRanges && fullA !== undefined && fullB !== undefined) {
    return (
      <div className="grid grid-cols-1 md:grid-cols-2 gap-6" data-testid="plagiarism-pair">
        <CodePane title={`${nameA} — matched lines highlighted`} text={fullA} lines={highlighted(segments.map(s => [s.aStartLine, s.aEndLine]))} />
        <CodePane title={`${nameB} — matched lines highlighted`} text={fullB} lines={highlighted(segments.map(s => [s.bStartLine, s.bEndLine]))} />
      </div>
    );
  }

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-6" data-testid="plagiarism-pair">
      <CodePane title={`${nameA} — matched excerpts`} text={segments.map(s => s.A).join('\n\n')} lines={new Set()} />
      <CodePane title={`${nameB} — matched excerpts`} text={segments.map(s => s.B).join('\n\n')} lines={new Set()} />
    </div>
  );
}
