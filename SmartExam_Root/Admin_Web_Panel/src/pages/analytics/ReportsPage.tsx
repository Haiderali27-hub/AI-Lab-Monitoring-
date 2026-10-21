import { useState, useEffect } from 'react';
import AppLayout from '../../components/layout/AppLayout';
import { analyticsApi } from '../../api/analytics.api';
import type { ReportExportType } from '../../api/analytics.api';
import { getFreshAccessToken } from '../../api/client';
import { examsApi } from '../../api/exams.api';
import type { ExamReport, Exam } from '../../types';
import { Download, FileText, Calendar, Loader2, FileSpreadsheet, FileArchive } from 'lucide-react';
import { useToast } from '../../context/ToastContext';

// Module 12 exports. The labels must not contain "download" (the history table's buttons) or "generate new report".
const EXPORTS: { type: ReportExportType; label: string; hint: string; icon: 'pdf' | 'xlsx' | 'zip' }[] = [
  { type: 'attendance', label: 'Attendance PDF', hint: 'Who sat the exam: seat, eligibility, class attendance, start and submit times', icon: 'pdf' },
  { type: 'violations', label: 'Violations PDF', hint: 'Violations per student with screenshot thumbnails and incidents', icon: 'pdf' },
  { type: 'scores', label: 'Scores PDF', hint: 'Marks per question and total for every assigned student', icon: 'pdf' },
  { type: 'excel', label: 'Scores Excel', hint: 'Scores and attendance as an .xlsx workbook', icon: 'xlsx' },
  { type: 'evidence-zip', label: 'Evidence ZIP', hint: 'Screenshots, events CSV, incidents, audit extract and a SHA-256 manifest', icon: 'zip' },
];

export default function ReportsPage() {
  const { showToast } = useToast();
  const [exams, setExams] = useState<Exam[]>([]);
  const [selectedExamId, setSelectedExamId] = useState('');
  const [reports, setReports] = useState<ExamReport[]>([]);
  const [loading, setLoading] = useState(false);
  const [genLoading, setGenLoading] = useState(false);
  const [exporting, setExporting] = useState<ReportExportType | null>(null);

  useEffect(() => {
    // Reports are the exam owner's (or an admin's): other teachers' exams would only answer 403.
    examsApi.getAll().then(all => setExams(all.filter(e => e.canManage !== false))).catch(err => console.error(err));
  }, []);

  const loadReportHistory = (examId: string) => {
    setLoading(true);
    analyticsApi.getReportHistory(examId)
      .then(setReports)
      .catch(err => console.error(err))
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    if (!selectedExamId) {
      setReports([]);
      return;
    }
    loadReportHistory(selectedExamId);
  }, [selectedExamId]);

  // The download endpoint needs the bearer token, so the file is fetched and saved from a blob.
  const saveReportFile = async (fileName: string) => {
    const token = await getFreshAccessToken();
    const res = await fetch(analyticsApi.getReportDownloadUrl(fileName), {
      headers: { Authorization: `Bearer ${token}` }
    });
    if (!res.ok) throw new Error('File download failed');
    const blob = await res.blob();
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = fileName;
    link.click();
    setTimeout(() => URL.revokeObjectURL(link.href), 10_000);
  };

  const handleDownload = (fileName: string) => {
    showToast('Downloading', 'Requesting the report file from the server...', 'info');
    saveReportFile(fileName)
      .then(() => showToast('Completed', 'Report downloaded successfully.', 'success'))
      .catch(() => showToast('Error', 'Failed to retrieve file from server.', 'error'));
  };

  // Generate one export, save it at once, and list it in the history.
  const handleExport = async (type: ReportExportType, label: string) => {
    if (!selectedExamId) return;
    setExporting(type);
    try {
      const result = await analyticsApi.generateExport(type, selectedExamId);
      await saveReportFile(result.fileName);
      showToast('Exported', `${label} is ready (${result.fileName}).`, 'success');
      loadReportHistory(selectedExamId);
    } catch (err) {
      console.error(err);
      showToast('Error', `${label} could not be generated.`, 'error');
    } finally {
      setExporting(null);
    }
  };

  const handleGenerateNew = async () => {
    if (!selectedExamId) return;
    setGenLoading(true);
    try {
      await analyticsApi.generateReport(selectedExamId);
      showToast('Success', 'Compiled a new proctor report.', 'success');
      loadReportHistory(selectedExamId);
    } catch (err) {
      console.error(err);
      showToast('Error', 'Report generation failed.', 'error');
    } finally {
      setGenLoading(false);
    }
  };

  return (
    <AppLayout title="Generated Reports">
      {/* Exam Selector Panel */}
      <div className="mb-6 flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 bg-white p-4 border border-slate-200 rounded-xl shadow-sm">
        <div className="flex items-center gap-3 w-full sm:w-auto">
          <FileText className="text-primary-500" size={20} />
          <select
            value={selectedExamId}
            onChange={(e) => setSelectedExamId(e.target.value)}
            className="w-full sm:w-64 bg-slate-50 border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-700 font-medium focus:outline-none focus:ring-2 focus:ring-primary-500"
          >
            <option value="">-- Select Exam to view reports --</option>
            {exams.map(e => (
              <option key={e.examId} value={e.examId}>{e.title}</option>
            ))}
          </select>
        </div>

        {selectedExamId && (
          <button
            onClick={handleGenerateNew}
            disabled={genLoading}
            className="w-full sm:w-auto flex items-center justify-center gap-2 px-4 py-2 bg-primary-600 hover:bg-primary-700 disabled:opacity-50 text-white rounded-lg text-sm font-semibold transition-colors shadow-sm"
          >
            {genLoading && <Loader2 size={16} className="animate-spin" />}
            <span>Generate New Report</span>
          </button>
        )}
      </div>

      {/* Module 12 exports */}
      {selectedExamId && (
        <div className="mb-6 bg-white p-4 border border-slate-200 rounded-xl shadow-sm">
          <div className="flex items-center justify-between mb-3">
            <h3 className="text-sm font-bold text-slate-800">Exports</h3>
            <span className="text-xs text-slate-400">Each export is recorded in the audit log with its SHA-256.</span>
          </div>
          <div className="flex flex-wrap gap-2">
            {EXPORTS.map(x => (
              <button
                key={x.type}
                onClick={() => handleExport(x.type, x.label)}
                disabled={exporting !== null}
                title={x.hint}
                className="flex items-center gap-2 px-3 py-2 border border-slate-200 hover:border-primary-300 hover:bg-primary-50 disabled:opacity-50 rounded-lg text-xs font-semibold text-slate-700 transition-colors"
              >
                {exporting === x.type ? (
                  <Loader2 size={14} className="animate-spin text-primary-500" />
                ) : x.icon === 'xlsx' ? (
                  <FileSpreadsheet size={14} className="text-green-600" />
                ) : x.icon === 'zip' ? (
                  <FileArchive size={14} className="text-amber-600" />
                ) : (
                  <FileText size={14} className="text-red-500" />
                )}
                <span>{x.label}</span>
              </button>
            ))}
          </div>
        </div>
      )}

      {/* Reports Listing Table */}
      <div className="bg-white border border-slate-200 rounded-xl overflow-hidden shadow-sm">
        <div className="px-6 py-4 border-b border-slate-100">
          <h3 className="text-base font-bold text-slate-800">Report Generation Log</h3>
        </div>

        {!selectedExamId ? (
          <div className="py-16 text-center text-slate-400 text-sm">
            Select an exam above to view previously generated reports.
          </div>
        ) : loading ? (
          <div className="py-16 flex items-center justify-center">
            <Loader2 className="animate-spin text-primary-500" size={24} />
          </div>
        ) : reports.length === 0 ? (
          <div className="py-16 text-center text-slate-400 text-sm">
            No reports exist for this exam. Click "Generate New Report" above to create one.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left">
              <thead>
                <tr className="bg-slate-50 border-b border-slate-100 text-slate-400 text-[10px] font-bold tracking-wider uppercase">
                  <th className="px-6 py-3">Report File Name</th>
                  <th className="px-6 py-3">Report Type</th>
                  <th className="px-6 py-3">Compiled At</th>
                  <th className="px-6 py-3"></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 text-sm">
                {reports.map((report) => (
                  <tr key={report.reportId} className="hover:bg-slate-50/50 transition-colors">
                    <td className="px-6 py-4 font-medium text-slate-700 flex items-center gap-2">
                      <FileText size={16} className="text-slate-400" />
                      {report.fileName}
                    </td>
                    <td className="px-6 py-4">
                      <span className="px-2 py-0.5 rounded-full bg-blue-50 border border-blue-100 text-primary-600 text-xs font-semibold">
                        {report.reportType}
                      </span>
                    </td>
                    <td className="px-6 py-4 text-slate-500 font-mono text-xs flex items-center gap-1.5 mt-1 border-none">
                      <Calendar size={14} className="text-slate-400" />
                      {new Date(report.generatedAt).toLocaleString()}
                    </td>
                    <td className="px-6 py-4 text-right">
                      <button
                        onClick={() => handleDownload(report.fileName)}
                        className="p-1.5 text-primary-600 hover:bg-primary-50 rounded-lg transition-colors inline-flex items-center gap-1 text-xs font-bold"
                        title="Download this file"
                      >
                        <Download size={14} />
                        <span>Download</span>
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </AppLayout>
  );
}
