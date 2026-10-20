import type { TileStatus } from '../types';

// Mirrors Backend_API/Services/ViolationClassifier.cs (single-event severity).
const SEVERITY: Record<string, TileStatus> = {
  FocusLost: 'Warning',
  KeyBlocked: 'Warning',
  ClipboardBlocked: 'Warning',
  UnauthorizedProcess: 'Suspicious',
  MultiMonitor: 'Suspicious',
  VirtualMachine: 'Suspicious',
  LockdownDegraded: 'Suspicious',
  BackupTampered: 'Suspicious',
  CameraCovered: 'Suspicious',
  CameraFrozen: 'Suspicious',
  CameraUnavailable: 'Warning',
  NoFace: 'Suspicious',
  MultipleFaces: 'Suspicious',
  LookingAway: 'Suspicious',
  PhoneDetected: 'Suspicious',
  ProcessKilled: 'Violation',
  UsbInserted: 'Violation',
  RemoteSession: 'Violation',
  SeatMismatch: 'Violation',
  Violation: 'Violation',
};

const LABELS: Record<string, string> = {
  FocusLost: 'Left the exam window',
  KeyBlocked: 'Blocked keys',
  ClipboardBlocked: 'Outside clipboard blocked',
  UnauthorizedProcess: 'Unauthorized app',
  ProcessKilled: 'Unauthorized app closed',
  UsbInserted: 'USB device inserted',
  MultiMonitor: 'Second monitor',
  RemoteSession: 'Remote desktop session',
  VirtualMachine: 'Virtual machine',
  LockdownDegraded: 'Lockdown degraded',
  BackupTampered: 'Local answer backup altered',
  CameraCovered: 'Camera covered',
  CameraFrozen: 'Camera feed frozen',
  CameraUnavailable: 'Camera unavailable',
  NoFace: 'No face in view',
  MultipleFaces: 'Multiple faces in view',
  LookingAway: 'Looking away from the screen',
  PhoneDetected: 'Mobile phone detected',
  SeatMismatch: 'Seat mismatch',
  Violation: 'Security violation',
};

const RANK: Record<TileStatus, number> = { Normal: 0, Warning: 1, Suspicious: 2, Violation: 3 };

export function severityOf(violationType?: string | null): TileStatus {
  return (violationType && SEVERITY[violationType]) || 'Violation';
}

export function worseStatus(a: TileStatus, b: TileStatus): TileStatus {
  return RANK[b] > RANK[a] ? b : a;
}

/** "Blocked keys: Alt+Tab", "Unauthorized app: chrome (Reported)" … */
export function violationLabel(violationType?: string | null, payloadJson?: string | null): string {
  const base = (violationType && LABELS[violationType]) || 'Security violation';
  try {
    const detail = payloadJson ? JSON.parse(payloadJson)?.detail : null;
    return detail ? `${base}: ${detail}` : base;
  } catch {
    return base;
  }
}
