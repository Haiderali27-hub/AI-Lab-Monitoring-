// Module 8: catalogue for the allowed-apps picker in the create-exam wizard. The desktop lockdown
// compares process names case-insensitively and ignores ".exe", so these are plain executable names.
export interface AllowedAppOption {
  label: string;
  exe: string;
}

export const ALLOWED_APP_CATALOGUE: AllowedAppOption[] = [
  { label: 'Visual Studio Code', exe: 'code.exe' },
  { label: 'Code::Blocks', exe: 'codeblocks.exe' },
  { label: 'Dev-C++', exe: 'devcpp.exe' },
  { label: 'Python IDLE', exe: 'pythonw.exe' },
  { label: 'Python (console)', exe: 'python.exe' },
  { label: 'Eclipse', exe: 'eclipse.exe' },
  { label: 'IntelliJ IDEA', exe: 'idea64.exe' },
  { label: 'PyCharm', exe: 'pycharm64.exe' },
  { label: 'Apache NetBeans', exe: 'netbeans64.exe' },
  { label: 'Visual Studio', exe: 'devenv.exe' },
  { label: 'Notepad++', exe: 'notepad++.exe' },
  { label: 'MySQL Workbench', exe: 'MySQLWorkbench.exe' },
  { label: 'MATLAB', exe: 'matlab.exe' },
];

/** "Code" → "code.exe"; returns null for anything that is not a plain executable file name. */
export function normalizeExeName(raw: string): string | null {
  let name = raw.trim();
  if (!name) return null;
  // Accept a full path pasted from Explorer, keep only the file name.
  name = name.split(/[\\/]/).pop() ?? name;
  if (!/\.exe$/i.test(name)) name = `${name}.exe`;
  return /^[\w.+\- ]{1,60}\.exe$/i.test(name) ? name : null;
}

/** Parses Exam.allowedApps (a JSON array string) defensively. */
export function parseAllowedApps(json: string | null | undefined): string[] {
  try {
    const apps = JSON.parse(json || '[]');
    return Array.isArray(apps) ? apps.filter((a): a is string => typeof a === 'string' && a.trim().length > 0) : [];
  } catch {
    return [];
  }
}
