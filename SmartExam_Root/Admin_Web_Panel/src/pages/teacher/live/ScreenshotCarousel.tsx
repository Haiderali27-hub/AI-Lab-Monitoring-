import { useEffect, useRef, useState } from 'react';
import { monitoringApi } from '../../../api/monitoring.api';
import type { ScreenshotSummary } from '../../../types';
import { violationLabel } from '../../../utils/violations';

// Newest captures shown in the drawer; older ones stay on the server (evidence export, Module 12).
const MAX_SHOWN = 8;

interface Loaded extends ScreenshotSummary {
  url: string;
}

/**
 * Module 10 — the latest screen and webcam captures of one session, newest first, as a scrollable strip; a click
 * opens the capture large. Images are fetched with the proctor's token (blob → object URL); every URL is revoked
 * when it leaves the strip or the drawer closes. `refreshKey` changes when a new capture of this student arrives.
 */
export default function ScreenshotCarousel({ examId, sessionId, refreshKey }: { examId: string; sessionId: string; refreshKey: number }) {
  const [shots, setShots] = useState<Loaded[]>([]);
  const [loading, setLoading] = useState(true);
  const [open, setOpen] = useState<Loaded | null>(null);
  const urls = useRef<Map<string, string>>(new Map());

  useEffect(() => {
    let cancelled = false;
    monitoringApi.screenshots(examId, sessionId)
      .then(async list => {
        const newest = list.slice(0, MAX_SHOWN);
        const loaded: Loaded[] = [];
        for (const shot of newest) {
          let url = urls.current.get(shot.screenshotId);
          if (!url) {
            try {
              url = URL.createObjectURL(await monitoringApi.screenshotBlob(shot.screenshotId));
              urls.current.set(shot.screenshotId, url);
            } catch {
              continue;   // expired by the retention job or removed: skip it
            }
          }
          loaded.push({ ...shot, url });
        }
        if (cancelled) return;
        // Release the captures that dropped out of the strip.
        const keep = new Set(loaded.map(s => s.screenshotId));
        urls.current.forEach((url, id) => {
          if (!keep.has(id)) {
            URL.revokeObjectURL(url);
            urls.current.delete(id);
          }
        });
        setShots(loaded);
      })
      .catch(err => console.error('Failed to load captures', err))
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [examId, sessionId, refreshKey]);

  useEffect(() => {
    const map = urls.current;
    return () => {
      map.forEach(url => URL.revokeObjectURL(url));
      map.clear();
    };
  }, [sessionId]);

  const caption = (s: ScreenshotSummary) =>
    `${s.kind === 'Webcam' ? 'Webcam' : 'Screen'} · ${new Date(s.capturedAt).toLocaleTimeString()}` +
    (s.trigger === 'Violation' ? ` · ${violationLabel(s.triggerReason)}` : '');

  return (
    <div>
      <div data-testid="screenshot-carousel" className="flex gap-2 overflow-x-auto pb-1 scrollbar-thin">
        {shots.map(s => (
          <button
            key={s.screenshotId}
            type="button"
            onClick={() => setOpen(s)}
            className={`shrink-0 w-[120px] text-left rounded-lg border overflow-hidden bg-slate-50 hover:ring-2 hover:ring-primary-500/30 ${s.trigger === 'Violation' ? 'border-orange-300' : 'border-slate-200'}`}
            title={caption(s)}
          >
            <img src={s.url} alt={caption(s)} className="w-full aspect-video object-cover bg-slate-100" />
            <span className={`block px-1.5 py-1 text-[9px] font-semibold truncate ${s.trigger === 'Violation' ? 'text-orange-700' : 'text-slate-500'}`}>
              {caption(s)}
            </span>
          </button>
        ))}
      </div>
      {!loading && shots.length === 0 && (
        <p className="text-[11px] text-slate-400 italic">No captures yet. The PC sends one every 30–90 s and on each violation.</p>
      )}

      {open && (
        <div
          role="dialog"
          aria-label="Capture"
          className="fixed inset-0 z-[60] bg-slate-900/70 flex items-center justify-center p-8"
          onClick={() => setOpen(null)}
        >
          <figure className="bg-white rounded-xl shadow-2xl max-w-5xl w-full overflow-hidden" onClick={e => e.stopPropagation()}>
            <img src={open.url} alt={caption(open)} className="w-full max-h-[75vh] object-contain bg-slate-900" />
            <figcaption className="flex items-center justify-between px-4 py-2 text-xs text-slate-600">
              <span>{caption(open)} · SHA-256 {open.sha256.slice(0, 16)}…</span>
              <button type="button" onClick={() => setOpen(null)} className="px-3 py-1 rounded-lg bg-slate-100 hover:bg-slate-200 font-semibold">
                Close
              </button>
            </figcaption>
          </figure>
        </div>
      )}
    </div>
  );
}
