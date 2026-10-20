import type { ReadinessResult, SessionTimelineItem } from '../../../types';
import { violationLabel } from '../../../utils/violations';

/** How one server timeline item reads in the drawer. */
export interface TimelineEntry {
  key: string;
  title: string;
  time: string;
  description?: string;
  tone: 'alert' | 'action' | 'info';
}

type Payload = Record<string, unknown>;

function parse(json?: string | null): Payload | null {
  if (!json) return null;
  try {
    const value = JSON.parse(json);
    return value && typeof value === 'object' ? value : null;
  } catch {
    return null;
  }
}

function list(value: unknown): string {
  return Array.isArray(value) ? value.join(', ') : '';
}

// The interesting part of a client event's payload, in words.
function eventDetail(item: SessionTimelineItem): string | undefined {
  const p = parse(item.detail);
  if (!p) return item.detail || undefined;
  switch (item.type) {
    case 'Reconnected':
      return `Back after ${p.offlineSeconds ?? '?'} s offline. Events from the gap are replayed with their original times.`;
    case 'Disconnected':
      return 'No heartbeat from the student PC for 30 seconds.';
    case 'NetworkOffline':
      return p.offlineFromUtc && p.offlineToUtc
        ? `Offline ${new Date(String(p.offlineFromUtc)).toLocaleTimeString()}–${new Date(String(p.offlineToUtc)).toLocaleTimeString()}${p.queuedEvents ? `, ${p.queuedEvents} event(s) replayed` : ''}.`
        : undefined;
    case 'ProcessList': {
      const parts = [list(p.added) && `Started: ${list(p.added)}`, list(p.removed) && `Closed: ${list(p.removed)}`].filter(Boolean);
      return parts.length ? parts.join('. ') : undefined;
    }
    default:
      return String(p.detail ?? p.activeWindow ?? p.reason ?? '') || undefined;
  }
}

function actionDetail(item: SessionTimelineItem): string | undefined {
  const p = parse(item.detail);
  if (!p) return undefined;
  if (item.type === 'SESSION_UNLOCKED') {
    return `${p.minutes} min${p.reason ? ` · Reason: ${p.reason}` : ''}`;
  }
  if (item.type === 'SESSION_TIME_EXTENDED') return `+${p.additionalMinutes} min (total extra ${p.extraMinutes} min)`;
  return undefined;
}

/** Turns a GET /exams/{id}/sessions/{sid}/timeline item into a drawer entry. */
export function describeTimelineItem(item: SessionTimelineItem, index: number): TimelineEntry {
  const time = new Date(item.at).toLocaleTimeString();
  const key = `${item.kind}-${item.type}-${item.at}-${item.screenshotId ?? index}`;
  switch (item.kind) {
    case 'Event': {
      const isViolation = item.severity !== 'Info';
      return {
        key,
        time,
        // Violations use the tile's wording ("Blocked keys", detail below); other events the server's label.
        title: isViolation ? violationLabel(item.type) : item.label,
        description: eventDetail(item),
        tone: isViolation ? 'alert' : 'info',
      };
    }
    case 'Screenshot':
      return {
        key,
        time,
        title: item.type === 'Webcam' ? 'Webcam Snapshot' : 'Screen Captured',
        description: item.detail ? `Evidence captured for: ${violationLabel(item.detail)}` : undefined,
        tone: item.detail ? 'alert' : 'info',
      };
    case 'Warning':
      return { key, time, title: item.label, description: item.detail ?? undefined, tone: 'action' };
    case 'Incident':
      return { key, time, title: item.label, description: item.detail ?? undefined, tone: 'alert' };
    case 'Action':
      return { key, time, title: item.label, description: actionDetail(item), tone: 'action' };
    default: {
      const receipt = parse(item.detail);
      return {
        key,
        time,
        title: item.label,
        description: receipt?.receiptSha256
          ? `${receipt.answerCount} answer(s) locked. Receipt ${String(receipt.receiptSha256).slice(0, 12)}…`
          : undefined,
        tone: 'info',
      };
    }
  }
}

export type ReadinessState = 'passed' | 'failed' | 'none';

/** Module 2 readiness result as the tile shows it (a pass is what matters once the exam runs, fresh or not). */
export function readinessState(r?: ReadinessResult | null): ReadinessState {
  if (!r) return 'none';
  return r.passedAll ? 'passed' : 'failed';
}

export const READINESS_LABEL: Record<ReadinessState, string> = {
  passed: 'Ready',
  failed: 'Check failed',
  none: 'No check',
};
