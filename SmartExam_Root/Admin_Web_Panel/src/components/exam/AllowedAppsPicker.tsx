import { useEffect, useRef, useState } from 'react';
import { ALLOWED_APP_CATALOGUE, normalizeExeName } from '../../utils/allowedApps';

interface Props {
  value: string[];
  onChange: (apps: string[]) => void;
}

/**
 * Module 8 allowed-apps picker: pick common lab tools from a catalogue (chips), with a free-text
 * fallback for anything else. Values are executable names, e.g. "code.exe".
 */
export default function AllowedAppsPicker({ value, onChange }: Props) {
  const [open, setOpen] = useState(false);
  const [custom, setCustom] = useState('');
  const [customError, setCustomError] = useState('');
  const popoverRef = useRef<HTMLDivElement | null>(null);

  const has = (exe: string) => value.some(v => v.toLowerCase() === exe.toLowerCase());
  const add = (exe: string) => {
    if (!has(exe)) onChange([...value, exe]);
  };
  const remove = (exe: string) => onChange(value.filter(v => v.toLowerCase() !== exe.toLowerCase()));

  // Close the catalogue on an outside click.
  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (popoverRef.current && !popoverRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', onDown);
    return () => document.removeEventListener('mousedown', onDown);
  }, [open]);

  const addCustom = () => {
    const exe = normalizeExeName(custom);
    if (!exe) {
      setCustomError('Enter an executable name, e.g. sublime_text.exe');
      return;
    }
    add(exe);
    setCustom('');
    setCustomError('');
  };

  const available = ALLOWED_APP_CATALOGUE.filter(o => !has(o.exe));

  return (
    <div>
      <div className="flex flex-wrap items-center gap-2 min-h-[42px] p-2 border border-slate-200 rounded-xl" aria-label="Allowed applications">
        {value.length === 0 && (
          <span className="text-sm text-slate-400 px-2">Only the exam app is allowed.</span>
        )}
        {value.map(exe => (
          <span
            key={exe}
            data-testid="allowed-app-chip"
            className="inline-flex items-center gap-1.5 pl-3 pr-1.5 py-1 rounded-full bg-primary-50 border border-primary-100 text-primary-700 text-xs font-semibold font-mono"
          >
            {exe}
            <button
              type="button"
              onClick={() => remove(exe)}
              aria-label={`Remove ${exe}`}
              className="w-5 h-5 rounded-full hover:bg-primary-100 flex items-center justify-center text-primary-500"
            >
              ×
            </button>
          </span>
        ))}

        <div className="relative" ref={popoverRef}>
          <button
            type="button"
            onClick={() => setOpen(o => !o)}
            aria-haspopup="listbox"
            aria-expanded={open}
            className="inline-flex items-center gap-1 px-3 py-1 rounded-full border border-dashed border-slate-300 text-xs font-semibold text-slate-600 hover:border-primary-400 hover:text-primary-600"
          >
            + Add allowed app
          </button>
          {open && (
            <ul
              role="listbox"
              aria-label="Common lab applications"
              className="absolute z-20 mt-2 w-72 max-h-72 overflow-auto bg-white border border-slate-200 rounded-xl shadow-lg py-1"
            >
              {available.length === 0 && (
                <li className="px-4 py-2 text-xs text-slate-400">Every catalogue app is already allowed.</li>
              )}
              {available.map(o => (
                <li
                  key={o.exe}
                  role="option"
                  aria-selected={false}
                  tabIndex={0}
                  onClick={() => { add(o.exe); setOpen(false); }}
                  onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); add(o.exe); setOpen(false); } }}
                  className="px-4 py-2 cursor-pointer hover:bg-slate-50 focus:bg-slate-50 outline-none flex justify-between gap-3"
                >
                  <span className="text-sm text-slate-700">{o.label}</span>
                  <span className="text-xs text-slate-400 font-mono">{o.exe}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>

      <div className="flex gap-2 mt-2">
        <input
          type="text"
          value={custom}
          onChange={e => { setCustom(e.target.value); setCustomError(''); }}
          onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); addCustom(); } }}
          aria-label="Other executable"
          placeholder="Other executable, e.g. sublime_text.exe"
          className="flex-1 px-4 py-2 border border-slate-200 rounded-xl focus:ring-2 focus:ring-primary-500/10 focus:border-primary-500 outline-none transition-all text-sm text-slate-800"
        />
        <button
          type="button"
          onClick={addCustom}
          className="px-4 py-2 rounded-xl border border-slate-200 text-sm font-semibold text-slate-600 hover:bg-slate-50"
        >
          Add
        </button>
      </div>
      {customError && <p className="text-xs text-red-500 mt-1">{customError}</p>}
    </div>
  );
}
