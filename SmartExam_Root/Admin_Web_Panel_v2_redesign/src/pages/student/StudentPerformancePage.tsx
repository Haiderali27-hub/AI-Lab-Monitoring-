import { useState, useEffect } from 'react';
import StudentLayout from '../../components/layout/StudentLayout';
import { studentApi } from '../../api/student.api';
import type { StudentDashboard } from '../../types';
import { useToast } from '../../context/ToastContext';
import { format } from 'date-fns';
import { 
  FileText, 
  TrendingUp, 
  Award, 
  AlertTriangle
} from 'lucide-react';

export default function StudentPerformancePage() {
  const { showToast } = useToast();
  const [trend, setTrend] = useState<any[]>([]);
  const [dashboard, setDashboard] = useState<StudentDashboard | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    Promise.all([
      studentApi.getPerformanceTrend(),
      studentApi.getDashboard()
    ])
      .then(([trendData, dashData]) => {
        setTrend(trendData);
        setDashboard(dashData);
      })
      .catch((err) => {
        console.error(err);
        showToast('Error', 'Failed to load performance analytics', 'error');
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

  // Calculate best score from trend
  const bestScore = trend.length > 0 ? Math.max(...trend.map(t => t.scorePercent)) : 0;
  
  // Calculate if improving trend
  const isImproving = trend.length > 1 && trend[trend.length - 1].scorePercent > trend[0].scorePercent;

  // Group trend data by course for the "Performance by Course" card
  const courseGroups = trend.reduce((acc: Record<string, { total: number; count: number }>, item: any) => {
    const title = item.title.split('—')[1]?.trim() || item.title.split('-')[0]?.trim() || 'General';
    if (!acc[title]) {
      acc[title] = { total: 0, count: 0 };
    }
    acc[title].total += item.scorePercent;
    acc[title].count += 1;
    return acc;
  }, {});

  const coursesList = Object.entries(courseGroups).map(([name, val]) => ({
    name,
    avg: Math.round(val.total / val.count)
  }));

  // Simple SVG Line Chart Coordinates Generator
  const chartHeight = 160;
  const chartWidth = 720;
  const paddingX = 40;
  const paddingY = 20;

  const points = trend.map((t: any, index: number) => {
    const x = paddingX + (index / Math.max(trend.length - 1, 1)) * (chartWidth - paddingX * 2);
    // invert Y since 0 is top
    const y = chartHeight - paddingY - (t.scorePercent / 100) * (chartHeight - paddingY * 2);
    return { x, y, scorePercent: t.scorePercent, title: t.title };
  });

  const polylinePointsStr = points.map(p => `${p.x},${p.y}`).join(' ');

  return (
    <StudentLayout>
      <div className="max-w-6xl mx-auto w-full px-4 py-6">
        
        {/* Header Section */}
        <div className="mb-8">
          <h1 className="text-3xl font-extrabold text-slate-900 tracking-tight mb-1">My Performance</h1>
          <p className="text-sm text-slate-500 font-medium">Track your progress across all proctored examination sessions</p>
        </div>

        {/* Stat Cards (4-column) */}
        {dashboard && (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6 mb-8">
            <div className="bg-blue-50 border border-blue-200 text-blue-800 p-5 rounded-xl shadow-sm hover:scale-[1.01] transition-transform">
              <FileText className="mb-2 text-blue-600" size={24} />
              <div className="text-xs font-bold uppercase tracking-wider text-blue-500">Exams Taken</div>
              <div className="text-3xl font-extrabold mt-1">{dashboard.totalExamsTaken}</div>
            </div>

            <div className="bg-emerald-50 border border-emerald-200 text-emerald-800 p-5 rounded-xl shadow-sm hover:scale-[1.01] transition-transform">
              <TrendingUp className="mb-2 text-emerald-600" size={24} />
              <div className="text-xs font-bold uppercase tracking-wider text-emerald-500">Average Score</div>
              <div className="text-3xl font-extrabold mt-1">{dashboard.averageScore}%</div>
            </div>

            <div className="bg-purple-50 border border-purple-200 text-purple-800 p-5 rounded-xl shadow-sm hover:scale-[1.01] transition-transform">
              <Award className="mb-2 text-purple-600" size={24} />
              <div className="text-xs font-bold uppercase tracking-wider text-purple-500">Best Score</div>
              <div className="text-3xl font-extrabold mt-1">{bestScore}%</div>
            </div>

            <div className="bg-orange-50 border border-orange-200 text-orange-800 p-5 rounded-xl shadow-sm hover:scale-[1.01] transition-transform">
              <AlertTriangle className="mb-2 text-orange-600" size={24} />
              <div className="text-xs font-bold uppercase tracking-wider text-orange-500">Total Flags</div>
              <div className="text-3xl font-extrabold mt-1">{dashboard.totalViolations}</div>
            </div>
          </div>
        )}

        {/* Score Over Time Line Chart */}
        {trend.length > 0 && (
          <div className="bg-white border border-slate-200 rounded-xl p-6 mb-8 shadow-sm">
            <div className="flex justify-between items-center mb-6">
              <h2 className="text-lg font-bold text-slate-800">Score Over Time</h2>
              
              {isImproving && (
                <span className="bg-green-100 text-green-700 text-xs font-bold px-3 py-1 rounded-full flex items-center gap-1">
                  <TrendingUp size={14} />
                  Improving trend
                </span>
              )}
            </div>

            <div className="relative w-full overflow-hidden border border-slate-100 rounded-lg bg-slate-50 p-4">
              
              {/* Pass Mark Line */}
              <div 
                className="absolute left-0 right-0 border-t border-dashed border-slate-300"
                style={{ bottom: '50%' }}
              >
                <span className="absolute right-4 -mt-4 text-[10px] font-bold text-slate-400 uppercase tracking-wider">
                  Pass Mark (50%)
                </span>
              </div>

              {/* Chart SVG */}
              <div className="h-44 w-full">
                <svg className="w-full h-full" viewBox={`0 0 ${chartWidth} ${chartHeight}`}>
                  {/* Grid Lines */}
                  <line x1={paddingX} y1={paddingY} x2={chartWidth - paddingX} y2={paddingY} stroke="#f1f5f9" strokeWidth={1} />
                  <line x1={paddingX} y1={chartHeight - paddingY} x2={chartWidth - paddingX} y2={chartHeight - paddingY} stroke="#e2e8f0" strokeWidth={1.5} />

                  {/* Polyline connecting points */}
                  {points.length > 1 && (
                    <polyline
                      fill="none"
                      stroke="#2563eb"
                      strokeWidth={3}
                      points={polylinePointsStr}
                      className="transition-all duration-500"
                    />
                  )}

                  {/* Draw Dots */}
                  {points.map((p: any, i: number) => (
                    <g key={i} className="group cursor-pointer">
                      <circle
                        cx={p.x}
                        cy={p.y}
                        r={5}
                        fill={p.scorePercent >= 50 ? '#10b981' : '#ef4444'}
                        stroke="#ffffff"
                        strokeWidth={2}
                        className="transition-all duration-150 hover:r-8"
                      />
                      {/* Hover stats label */}
                      <text
                        x={p.x}
                        y={p.y - 12}
                        textAnchor="middle"
                        className="text-[9px] font-extrabold fill-slate-700 opacity-0 group-hover:opacity-100 transition-opacity bg-white px-1"
                      >
                        {p.scorePercent}%
                      </text>
                    </g>
                  ))}
                </svg>
              </div>

              {/* X Axis Labels */}
              <div className="flex justify-between px-6 pt-2 border-t border-slate-100 bg-white mt-2 rounded">
                {trend.map((t: any, i: number) => (
                  <span key={i} className="text-[9px] font-bold text-slate-400 uppercase tracking-wider truncate w-14 text-center">
                    {t.title.split(' ')[0] || `Exam ${i + 1}`}
                  </span>
                ))}
              </div>
            </div>
          </div>
        )}

        {/* Bottom Grid: Course breakdown & Timeline */}
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          
          {/* Performance by Course */}
          <div className="bg-white border border-slate-200 rounded-xl p-6 shadow-sm">
            <h2 className="text-lg font-bold text-slate-800 mb-6">Performance by Course</h2>
            <div className="space-y-5">
              {coursesList.map((course: any, idx: number) => (
                <div key={idx}>
                  <div className="flex justify-between items-center mb-1.5">
                    <span className="text-sm font-bold text-slate-700">{course.name}</span>
                    <span className="text-xs font-bold text-primary-600">{course.avg}%</span>
                  </div>
                  <div className="w-full h-3 bg-slate-100 rounded-full overflow-hidden border border-slate-200 shadow-inner">
                    <div
                      className="h-full bg-gradient-to-r from-primary-500 to-indigo-500 rounded-full transition-all duration-500"
                      style={{ width: `${course.avg}%` }}
                    />
                  </div>
                </div>
              ))}
              {coursesList.length === 0 && (
                <p className="text-xs text-slate-400 italic text-center py-6">No course data available.</p>
              )}
            </div>
          </div>

          {/* Exam Timeline */}
          <div className="bg-white border border-slate-200 rounded-xl p-6 shadow-sm">
            <h2 className="text-lg font-bold text-slate-800 mb-6">Exam Timeline</h2>
            
            <div className="relative pl-6 space-y-6">
              {/* Timeline Connector Line */}
              <div className="absolute left-[9px] top-1.5 bottom-1.5 w-0.5 bg-slate-200" />

              {trend.slice().reverse().map((exam: any, idx: number) => {
                const passed = exam.scorePercent >= 50;
                return (
                  <div key={idx} className="relative">
                    {/* Timeline Dot */}
                    <div 
                      className={`absolute -left-[22px] top-1 h-3.5 w-3.5 rounded-full border-4 border-white ring-2 
                        ${passed ? 'bg-green-500 ring-green-400' : 'bg-red-500 ring-red-400'}`}
                    />
                    <div>
                      <div className="text-[10px] font-bold text-slate-400">
                        {exam.startedAt ? format(new Date(exam.startedAt), 'MMM dd, yyyy - hh:mm a') : 'Completed'}
                      </div>
                      <div className="text-sm font-bold text-slate-800 mt-0.5">{exam.title}</div>
                      <div className="text-xs font-medium text-slate-500 mt-0.5">
                        Score: <span className={passed ? 'text-green-600 font-bold' : 'text-red-500 font-bold'}>{exam.scorePercent}%</span>
                      </div>
                    </div>
                  </div>
                );
              })}
              
              {trend.length === 0 && (
                <p className="text-xs text-slate-400 italic text-center py-6">No historical records in timeline.</p>
              )}
            </div>
          </div>

        </div>

      </div>
    </StudentLayout>
  );
}
