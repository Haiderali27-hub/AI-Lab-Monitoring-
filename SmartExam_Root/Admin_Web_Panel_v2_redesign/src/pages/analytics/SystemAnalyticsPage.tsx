import { useState, useEffect } from 'react';
import AppLayout from '../../components/layout/AppLayout';
import { analyticsApi } from '../../api/analytics.api';
import type { SystemAnalytics } from '../../types';
import { BarChart3, TrendingUp, AlertTriangle, Calendar, Award } from 'lucide-react';

export default function SystemAnalyticsPage() {
  const [data, setData] = useState<SystemAnalytics | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    analyticsApi.getSystemAnalytics()
      .then(setData)
      .catch(err => console.error('Error loading system analytics:', err))
      .finally(() => setLoading(false));
  }, []);

  if (loading) {
    return (
      <AppLayout title="System Analytics">
        <div className="min-h-[400px] flex items-center justify-center">
          <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin" />
        </div>
      </AppLayout>
    );
  }

  if (!data) {
    return (
      <AppLayout title="System Analytics">
        <div className="text-center py-12 text-slate-500">Failed to load analytics data.</div>
      </AppLayout>
    );
  }

  // Find max counts for scaling the graphs
  const maxExamsByMonth = Math.max(...data.examsByMonth.map(m => m.count), 1);
  const maxTopCourses = Math.max(...data.topCoursesByExamCount.map(c => c.count), 1);
  const maxViolations = Math.max(...data.violationTrendLast30Days.map(v => v.count), 1);

  // Generate SVG points for the line chart (1000x200 grid)
  const totalDays = data.violationTrendLast30Days.length;
  const points = data.violationTrendLast30Days.map((v, i) => {
    const x = totalDays > 1 ? (i / (totalDays - 1)) * 960 + 20 : 500;
    const y = maxViolations > 0 ? 180 - (v.count / maxViolations) * 140 : 150;
    return { x, y, count: v.count, date: new Date(v.date).toLocaleDateString('en-US', { month: 'short', day: 'numeric' }) };
  });

  const polylinePointsStr = points.map(p => `${p.x},${p.y}`).join(' ');
  const areaPathStr = points.length > 0 
    ? `M ${points[0].x},180 ` + points.map(p => `L ${p.x},${p.y}`).join(' ') + ` L ${points[points.length - 1].x},180 Z`
    : '';

  return (
    <AppLayout title="System Analytics">
      {/* Date Range Selector Header */}
      <div className="mb-6 flex justify-between items-center">
        <div>
          <p className="text-xs font-semibold tracking-wider text-slate-400 uppercase">PLATFORM OVERVIEW</p>
          <h2 className="text-2xl font-bold text-slate-800">Operational Metrics</h2>
        </div>
        <div className="flex items-center gap-2 px-3 py-1.5 border border-slate-200 rounded-lg bg-white text-slate-600 text-sm font-medium">
          <Calendar size={16} className="text-primary-500" />
          <span>Last 30 Days</span>
        </div>
      </div>

      {/* Stats Counter Strip */}
      <div className="grid grid-cols-1 md:grid-cols-5 gap-6 mb-8">
        <div className="bg-white border border-slate-200 p-5 rounded-xl shadow-sm">
          <p className="text-xs font-bold tracking-wider text-slate-400 uppercase mb-2">Total Students</p>
          <p className="text-3xl font-black text-slate-800 font-mono">{data.totalStudents}</p>
        </div>
        <div className="bg-white border border-slate-200 p-5 rounded-xl shadow-sm">
          <p className="text-xs font-bold tracking-wider text-slate-400 uppercase mb-2">Total Teachers</p>
          <p className="text-3xl font-black text-slate-800 font-mono">{data.totalTeachers}</p>
        </div>
        <div className="bg-white border border-slate-200 p-5 rounded-xl shadow-sm">
          <p className="text-xs font-bold tracking-wider text-slate-400 uppercase mb-2">Total Exams</p>
          <p className="text-3xl font-black text-slate-800 font-mono">{data.totalExams}</p>
        </div>
        <div className="bg-white border border-slate-200 p-5 rounded-xl shadow-sm">
          <div className="flex justify-between items-start mb-2">
            <p className="text-xs font-bold tracking-wider text-slate-400 uppercase">Exams (30 Days)</p>
            <TrendingUp size={16} className="text-green-600" />
          </div>
          <p className="text-3xl font-black text-slate-800 font-mono">{data.totalExamsLast30Days}</p>
        </div>
        <div className="bg-white border border-slate-200 p-5 rounded-xl shadow-sm">
          <div className="flex justify-between items-start mb-2">
            <p className="text-xs font-bold tracking-wider text-slate-400 uppercase">Violations (30 Days)</p>
            <AlertTriangle size={16} className="text-red-500" />
          </div>
          <p className="text-3xl font-black text-slate-800 font-mono">{data.totalViolationsLast30Days}</p>
        </div>
      </div>

      {/* Charts Section */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 mb-8">
        {/* Exams Per Month */}
        <section className="col-span-1 lg:col-span-7 bg-white border border-slate-200 p-6 rounded-xl shadow-sm">
          <div className="flex justify-between items-center mb-6">
            <h3 className="text-base font-bold text-slate-800 flex items-center gap-2">
              <BarChart3 size={18} className="text-primary-500" />
              Exams Per Month (Last 6 Months)
            </h3>
          </div>
          
          <div className="h-64 flex items-end gap-4 px-2 pb-6 border-b border-slate-100">
            {data.examsByMonth.length === 0 ? (
              <div className="w-full text-center text-sm text-slate-400 py-16">No monthly records.</div>
            ) : (
              data.examsByMonth.map((item: { label: string; count: number }, i: number) => {
                const heightPct = (item.count / maxExamsByMonth) * 100;
                return (
                  <div key={i} className="flex-1 flex flex-col items-center gap-2 group">
                    <span className="text-xs font-semibold text-slate-500 opacity-0 group-hover:opacity-100 transition-opacity font-mono">{item.count}</span>
                    <div className="w-full bg-slate-100 rounded-t-md h-44 flex items-end">
                      <div
                        className="w-full bg-primary-500 rounded-t-md transition-all duration-700 ease-out"
                        style={{ height: `${heightPct}%` }}
                      />
                    </div>
                    <span className="text-xs font-medium text-slate-500 truncate w-full text-center mt-1">{item.label}</span>
                  </div>
                );
              })
            )}
          </div>
        </section>

        {/* Top Courses */}
        <section className="col-span-1 lg:col-span-5 bg-white border border-slate-200 p-6 rounded-xl shadow-sm">
          <div className="flex justify-between items-center mb-6">
            <h3 className="text-base font-bold text-slate-800 flex items-center gap-2">
              <Award size={18} className="text-primary-500" />
              Top Courses by Exam Activity
            </h3>
          </div>

          <div className="space-y-4">
            {data.topCoursesByExamCount.length === 0 ? (
              <div className="text-center text-sm text-slate-400 py-16">No course metrics recorded.</div>
            ) : (
              data.topCoursesByExamCount.map((item: { label: string; count: number }, i: number) => {
                const widthPct = (item.count / maxTopCourses) * 100;
                return (
                  <div key={i} className="space-y-1">
                    <div className="flex justify-between text-xs font-semibold text-slate-600">
                      <span className="truncate max-w-[200px]">{item.label}</span>
                      <span className="font-mono">{item.count} Exams</span>
                    </div>
                    <div className="h-2.5 w-full bg-slate-100 rounded-full overflow-hidden">
                      <div
                        className="h-full bg-primary-500 rounded-full transition-all duration-700 ease-out"
                        style={{ width: `${widthPct}%`, opacity: 0.5 + (widthPct / 100) * 0.5 }}
                      />
                    </div>
                  </div>
                );
              })
            )}
          </div>
        </section>
      </div>

      {/* Violation Trend Line Chart */}
      <section className="bg-white border border-slate-200 p-6 rounded-xl shadow-sm">
        <div className="flex justify-between items-center mb-6">
          <div>
            <h3 className="text-base font-bold text-slate-800">Violation Trend Timeline</h3>
            <p className="text-xs text-slate-400 mt-0.5">Detected system anomalies across proctored sessions</p>
          </div>
        </div>

        {points.length === 0 ? (
          <div className="text-center py-12 text-slate-400 text-sm">No violation metrics recorded in the last 30 days.</div>
        ) : (
          <div className="w-full">
            <div className="h-[220px] w-full relative">
              <svg className="w-full h-full" viewBox="0 0 1000 200" preserveAspectRatio="none">
                {/* Grid horizontal guidelines */}
                <line x1="20" y1="40" x2="980" y2="40" stroke="#f1f5f9" strokeWidth="1" />
                <line x1="20" y1="110" x2="980" y2="110" stroke="#f1f5f9" strokeWidth="1" />
                <line x1="20" y1="180" x2="980" y2="180" stroke="#e2e8f0" strokeWidth="1.5" />

                {/* Shaded Area Fill */}
                {areaPathStr && (
                  <path d={areaPathStr} fill="#ba1a1a" fillOpacity="0.04" />
                )}

                {/* Line Path */}
                {polylinePointsStr && (
                  <polyline
                    fill="none"
                    stroke="#ba1a1a"
                    strokeWidth="2.5"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    points={polylinePointsStr}
                  />
                )}

                {/* Dots with tooltip highlights */}
                {points.map((p: { x: number; y: number; count: number; date: string }, idx: number) => (
                  <g key={idx} className="group cursor-pointer">
                    <circle cx={p.x} cy={p.y} r="4" fill="#ba1a1a" className="transition-all duration-150 hover:r-6" />
                    <circle cx={p.x} cy={p.y} r="10" fill="transparent" />
                    <title>{`${p.date}: ${p.count} violations`}</title>
                  </g>
                ))}
              </svg>
            </div>

            {/* X-Axis labels */}
            <div className="flex justify-between px-4 mt-2 text-[10px] font-semibold text-slate-400 uppercase">
              <span>{points[0].date}</span>
              <span>{points[Math.floor(points.length / 2)].date}</span>
              <span>Today</span>
            </div>
          </div>
        )}
      </section>
    </AppLayout>
  );
}
