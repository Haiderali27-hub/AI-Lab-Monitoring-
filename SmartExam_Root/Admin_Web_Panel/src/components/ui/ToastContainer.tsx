import { X } from 'lucide-react';
import { useToast } from '../../context/ToastContext';

const typeStyles = {
  success: { bar: 'bg-green-500', icon: '✓', ring: 'bg-green-100 text-green-700' },
  warning: { bar: 'bg-orange-500', icon: '⚠', ring: 'bg-orange-100 text-orange-700' },
  info:    { bar: 'bg-blue-500',   icon: 'ℹ', ring: 'bg-blue-100 text-blue-700' },
  error:   { bar: 'bg-red-500',    icon: '✕', ring: 'bg-red-100 text-red-700' },
};

export default function ToastContainer() {
  const { toasts, removeToast } = useToast();

  return (
    <div className="fixed bottom-6 right-6 z-50 flex flex-col gap-2 w-80">
      {toasts.map(toast => {
        const style = typeStyles[toast.type];
        return (
          <div key={toast.id} className="bg-white rounded-lg border border-slate-200 shadow-lg overflow-hidden animate-in slide-in-from-right-5">
            <div className="p-4 flex items-start gap-3">
              <span className={`w-6 h-6 rounded-full flex items-center justify-center text-xs flex-shrink-0 ${style.ring}`}>
                {style.icon}
              </span>
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium text-slate-900">{toast.title}</p>
                <p className="text-xs text-slate-500 mt-0.5">{toast.message}</p>
              </div>
              <button onClick={() => removeToast(toast.id)} className="text-slate-300 hover:text-slate-500">
                <X size={14} />
              </button>
            </div>
            {/* Progress bar */}
            <div className={`h-1 ${style.bar} animate-shrink`} />
          </div>
        );
      })}
    </div>
  );
}
