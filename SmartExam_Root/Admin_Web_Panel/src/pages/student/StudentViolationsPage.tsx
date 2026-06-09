import { useState, useEffect } from 'react';
import StudentLayout from '../../components/layout/StudentLayout';
import { studentApi } from '../../api/student.api';
import { useToast } from '../../context/ToastContext';
import { format } from 'date-fns';
import { 
  AlertTriangle, 
  Info, 
  Calendar, 
  Clock, 
  ShieldCheck, 
  AlertCircle 
} from 'lucide-react';

export default function StudentViolationsPage() {
  const { showToast } = useToast();
  
  const [data, setData] = useState<{ totalViolations: number; violations: any[] } | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    studentApi.getViolations()
      .then(res => {
        setData(res);
      })
      .catch(() => {
        showToast('Error', 'Failed to load violations history', 'error');
      })
      .finally(() => setLoading(false));
  }, []);

  if (loading) {
    return (
      <StudentLayout>
        <div className="py-24 flex justify-center">
          <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin" />
        </div>
      </StudentLayout>
    );
  }

  // Handle empty state: no violations recorded
  if (!data || data.totalViolations === 0 || data.violations.length === 0) {
    return (
      <StudentLayout>
        <div className="max-w-xl mx-auto text-center py-20 px-4">
          <div className="w-20 h-20 bg-green-50 border border-green-200 rounded-full flex items-center justify-center mb-6 mx-auto text-green-600 shadow-sm animate-bounce">
            <ShieldCheck size={44} />
          </div>
          <h2 className="text-2xl font-extrabold text-slate-800 mb-2">Academic Integrity Secured</h2>
          <p className="text-sm text-slate-500 mb-6 max-w-sm mx-auto leading-relaxed">
            Your record is perfectly clean. We celebrate your commitment to absolute academic honesty and compliance!
          </p>
          <button
            onClick={() => window.location.reload()}
            className="px-6 py-2.5 bg-green-600 text-white font-bold rounded-lg hover:bg-green-700 transition-colors shadow-sm"
          >
            Keep it up!
          </button>
        </div>
      </StudentLayout>
    );
  }

  // helper function to extract violation metadata details
  const getViolationLogDetails = (payloadStr: string) => {
    try {
      if (!payloadStr) return 'AI Proctoring Alert: Suspicious environmental anomaly detected.';
      const details = JSON.parse(payloadStr);
      if (details.blockedApp) {
        return `[CRITICAL] Blocked application foreground detection: ${details.blockedApp}`;
      }
      if (details.activeWindow) {
        return `[CRITICAL] Active window changed focus to: ${details.activeWindow}`;
      }
      if (details.description) {
        return `[WARNING] ${details.description}`;
      }
      return `[FLAG] Proctoring check: ${payloadStr}`;
    } catch {
      return `[FLAG] Proctoring log data: ${payloadStr}`;
    }
  };

  return (
    <StudentLayout>
      <div className="max-w-6xl mx-auto w-full px-4 py-6">
        
        {/* Header Section */}
        <div className="mb-8">
          <h1 className="text-3xl font-extrabold text-slate-900 tracking-tight mb-2">Violation History</h1>
          <div className="flex gap-2.5 flex-wrap">
            <div className="bg-amber-100 text-amber-800 px-3.5 py-1.5 rounded-full flex items-center gap-1.5 font-bold text-xs shadow-sm">
              <AlertTriangle size={14} className="text-amber-600" />
              <span>{data.totalViolations} Total Flags</span>
            </div>
            <div className="bg-slate-100 text-slate-600 px-3.5 py-1.5 rounded-full flex items-center gap-1.5 font-bold text-xs">
              <Clock size={14} />
              <span>Recorded via Auto-Monitor</span>
            </div>
          </div>
        </div>

        {/* Warning Banner */}
        <div className="bg-amber-50 border-l-4 border-amber-500 p-5 rounded-r-xl mb-8 flex gap-4 shadow-sm">
          <Info className="text-amber-600 flex-shrink-0 mt-0.5" size={22} />
          <div>
            <p className="text-sm font-medium text-amber-800 leading-relaxed">
              These events were automatically recorded by the SmartExam proctoring system. Minor incidents are logged for review and don't necessarily lead to immediate disqualification. Please contact your course instructor if you believe a mistake occurred.
            </p>
          </div>
        </div>

        {/* Layout Grid */}
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
          
          {/* List of violations */}
          <div className="lg:col-span-8 space-y-4">
            {data.violations.map((violation: any, idx: number) => {
              return (
                <div 
                  key={violation.violationId || idx}
                  className="bg-white p-5 rounded-xl border border-slate-200 shadow-sm hover:shadow-md transition-shadow duration-200"
                >
                  <div className="flex justify-between items-start mb-4">
                    <div>
                      <h3 className="text-base font-bold text-slate-800 mb-0.5">
                        {violation.examTitle || 'Semester Examination Session'}
                      </h3>
                      <p className="text-xs text-slate-400 font-semibold uppercase tracking-wider">
                        {violation.courseName || 'SmartExam Monitoring'}
                      </p>
                    </div>
                    <span className="bg-amber-50 border border-amber-200 text-amber-700 px-2.5 py-0.5 rounded-full text-[10px] font-bold flex items-center gap-1 uppercase tracking-wider">
                      <AlertCircle size={10} />
                      {violation.violationType || 'Focus Divergence'}
                    </span>
                  </div>

                  <div className="flex items-center gap-6 text-xs text-slate-500 mb-4">
                    <div className="flex items-center gap-1.5">
                      <Calendar size={14} className="text-slate-400" />
                      <span>{violation.createdAt ? format(new Date(violation.createdAt), 'MMM dd, yyyy') : 'Recently'}</span>
                    </div>
                    <div className="flex items-center gap-1.5">
                      <Clock size={14} className="text-slate-400" />
                      <span>{violation.createdAt ? format(new Date(violation.createdAt), 'hh:mm a') : 'Unspecified'}</span>
                    </div>
                  </div>

                  <div className="bg-slate-50 p-4 rounded-lg border border-slate-200/60 shadow-inner">
                    <p className="text-[10px] font-bold text-slate-400 uppercase tracking-widest mb-1.5">System Monitor Log Payload</p>
                    <code className="font-mono text-xs text-slate-700 block overflow-x-auto whitespace-pre leading-relaxed select-all">
                      {getViolationLogDetails(violation.payload)}
                    </code>
                  </div>
                </div>
              );
            })}
          </div>

          {/* Sidebar cards */}
          <div className="lg:col-span-4 space-y-6">
            
            {/* Integrity Score */}
            <div className="bg-green-50/60 border border-green-100 p-6 rounded-2xl text-center shadow-sm">
              <div className="inline-flex items-center justify-center w-14 h-14 bg-green-100 text-green-700 rounded-full mb-4 shadow-inner">
                <ShieldCheck size={28} />
              </div>
              <h2 className="font-bold text-base text-green-800 mb-2">Integrity Status</h2>
              <p className="text-xs text-green-700/80 mb-4 leading-relaxed">
                You have maintained compliant behavior logs on your other active test trials. Maintain a high standard!
              </p>
              <div className="h-2 w-full bg-green-200/50 rounded-full mb-2">
                <div className="h-full bg-green-600 rounded-full w-[90%]" />
              </div>
              <p className="text-[10px] font-bold text-green-700 text-right uppercase tracking-wider">
                90% Clean History Rating
              </p>
            </div>

            {/* Proctored Tip */}
            <div className="bg-blue-50 border border-blue-100 p-5 rounded-2xl text-blue-900 shadow-sm relative overflow-hidden group">
              <div className="relative z-10">
                <h4 className="text-[9px] font-bold text-blue-400 uppercase tracking-widest mb-1">Testing Tip</h4>
                <p className="text-sm font-bold text-slate-800 mb-2">Avoid Flagging</p>
                <p className="text-xs text-slate-600 leading-relaxed mb-4">
                  Ensure your testing environment is well-lit and you've disabled all background notifications, overlay apps, and auto-updates before starting the session.
                </p>
                <button 
                  onClick={() => showToast('Rules & Guides', 'Directing to best practices documentation...', 'info')}
                  className="bg-white text-primary-600 hover:bg-blue-50 px-4 py-1.5 rounded-full font-bold text-[10px] uppercase shadow-sm border border-blue-100"
                >
                  Read Best Practices
                </button>
              </div>
            </div>

          </div>

        </div>

      </div>
    </StudentLayout>
  );
}
