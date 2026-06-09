import { useState, useEffect } from 'react';
import { useSearchParams } from 'react-router-dom';
import AppLayout from '../../components/layout/AppLayout';
import { examsApi } from '../../api/exams.api';
import type { Exam, ExamAssignment } from '../../types';

export default function EligibilityPage() {
  const [exams, setExams] = useState<Exam[]>([]);
  const [searchParams, setSearchParams] = useSearchParams();
  const [selectedExamId, setSelectedExamId] = useState('');
  const [assignments, setAssignments] = useState<ExamAssignment[]>([]);
  const [loading, setLoading] = useState(false);
  const [hasChanges, setHasChanges] = useState(false);

  // Load all exams initially
  useEffect(() => {
    examsApi.getAll()
      .then((data) => {
        setExams(data);
        // Prioritize URL param if present
        const urlExamId = searchParams.get('examId');
        if (urlExamId) {
          setSelectedExamId(urlExamId);
        } else if (data.length > 0) {
          setSelectedExamId(data[0].examId);
        }
      })
      .catch((err) => console.error('Failed to load exams:', err));
  }, [searchParams]);

  // Load assignments when selected exam changes
  useEffect(() => {
    if (!selectedExamId) return;
    setLoading(true);
    examsApi.getAssignments(selectedExamId)
      .then((data) => {
        setAssignments(data);
        setHasChanges(false);
      })
      .catch((err) => console.error('Failed to load assignments:', err))
      .finally(() => setLoading(false));
  }, [selectedExamId]);

  const toggleEligibility = (userId: string) => {
    setAssignments((prev) =>
      prev.map((a) => (a.userId === userId ? { ...a, isEligible: !a.isEligible } : a))
    );
    setHasChanges(true);
  };

  const handleNoteChange = (userId: string, note: string) => {
    setAssignments((prev) =>
      prev.map((a) => (a.userId === userId ? { ...a, eligibilityNote: note } : a))
    );
    setHasChanges(true);
  };

  const handleMarkAll = (eligible: boolean) => {
    setAssignments((prev) => prev.map((a) => ({ ...a, isEligible: eligible })));
    setHasChanges(true);
  };

  const handleSave = async () => {
    if (!selectedExamId) return;
    try {
      await examsApi.updateEligibility(
        selectedExamId,
        assignments.map((a) => ({
          userId: a.userId,
          isEligible: a.isEligible,
          eligibilityNote: a.eligibilityNote || '',
        }))
      );
      setHasChanges(false);
      alert('Eligibility parameters successfully updated.');
    } catch (err) {
      console.error(err);
      alert('Failed to save eligibility changes.');
    }
  };

  const handleDiscard = () => {
    if (selectedExamId) {
      setLoading(true);
      examsApi.getAssignments(selectedExamId)
        .then((data) => {
          setAssignments(data);
          setHasChanges(false);
        })
        .finally(() => setLoading(false));
    }
  };

  return (
    <AppLayout title="Eligibility Management">
      <div className="space-y-6">
        {/* Header Control Row */}
        <div className="flex flex-col md:flex-row md:items-end justify-between gap-6">
          <div className="space-y-2">
            <label className="text-xs font-bold tracking-wider text-slate-400 uppercase block">Select Examination</label>
            <div className="relative w-full md:w-[320px]">
              <select
                value={selectedExamId}
                onChange={(e) => {
                  setSelectedExamId(e.target.value);
                  setSearchParams({ examId: e.target.value });
                }}
                className="w-full h-10 border border-slate-200 bg-white rounded-lg px-4 pr-10 focus:ring-2 focus:ring-primary-500 focus:border-transparent outline-none text-sm cursor-pointer shadow-sm"
              >
                <option value="">Choose an exam</option>
                {exams.map((exam) => (
                  <option key={exam.examId} value={exam.examId}>
                    {exam.title} ({exam.courseName})
                  </option>
                ))}
              </select>
            </div>
          </div>
          
          <div className="flex items-center gap-3">
            <div className="bg-blue-50 px-4 py-2 rounded-full flex items-center gap-2">
              <svg className="w-4.5 h-4.5 text-primary-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 4.354a4 4 0 110 5.292M15 21H3v-1a6 6 0 0112 0v1zm0 0h6v-1a6 6 0 00-9-5.197M13 7a4 4 0 11-8 0 4 4 0 018 0z" />
              </svg>
              <span className="font-bold text-primary-600 text-sm">{assignments.length} Students</span>
            </div>
            <button
              onClick={() => handleMarkAll(true)}
              className="px-4 h-10 bg-blue-50 hover:bg-blue-100 text-primary-600 font-bold rounded-lg transition-colors flex items-center gap-2 text-xs"
            >
              Mark All Eligible
            </button>
            <button
              onClick={() => handleMarkAll(false)}
              className="px-4 h-10 border border-slate-200 text-slate-500 font-semibold rounded-lg hover:bg-slate-50 transition-colors flex items-center gap-2 text-xs"
            >
              Mark All Ineligible
            </button>
          </div>
        </div>

        {/* Table View */}
        {loading ? (
          <div className="min-h-[300px] flex items-center justify-center">
            <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin" />
          </div>
        ) : (
          <div className="bg-white border border-slate-200 rounded-xl overflow-hidden shadow-sm">
            <div className="overflow-x-auto">
              <table className="w-full text-left border-collapse">
                <thead>
                  <tr className="bg-slate-50 border-b border-slate-200">
                    <th className="px-6 py-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Student Name</th>
                    <th className="px-6 py-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Email</th>
                    <th className="px-6 py-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Workstation</th>
                    <th className="px-6 py-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Eligibility</th>
                    <th className="px-6 py-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Note / Reason</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {assignments.length === 0 ? (
                    <tr>
                      <td colSpan={5} className="px-6 py-12 text-center text-sm text-slate-400">
                        {selectedExamId ? 'No student assignments registered.' : 'Please select an exam.'}
                      </td>
                    </tr>
                  ) : (
                    assignments.map((item) => (
                      <tr 
                        key={item.userId} 
                        className={`transition-colors hover:bg-slate-50/50 ${
                          !item.isEligible ? 'bg-red-50/20 hover:bg-red-50/30' : ''
                        }`}
                      >
                        <td className="px-6 py-4 flex items-center gap-3">
                          <div className={`w-8 h-8 rounded-full flex items-center justify-center font-bold text-xs ${
                            item.isEligible ? 'bg-blue-50 text-primary-500' : 'bg-red-100 text-red-600'
                          }`}>
                            {item.studentName.split(' ').map((n: string) => n[0]).join('')}
                          </div>
                          <span className={`font-semibold text-sm ${!item.isEligible ? 'text-red-700' : 'text-slate-700'}`}>
                            {item.studentName}
                          </span>
                        </td>
                        <td className="px-6 py-4 text-sm text-slate-400">{item.studentEmail}</td>
                        <td className="px-6 py-4 font-mono text-sm text-slate-500">{item.workstationNumber || 'Not Mapped'}</td>
                        <td className="px-6 py-4">
                          <label className="relative inline-flex items-center cursor-pointer">
                            <input
                              type="checkbox"
                              checked={item.isEligible}
                              onChange={() => toggleEligibility(item.userId)}
                              className="sr-only peer"
                            />
                            <div className="w-10 h-5 bg-slate-200 rounded-full peer peer-checked:after:translate-x-full after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-slate-300 after:border after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:bg-primary-500"></div>
                          </label>
                        </td>
                        <td className="px-6 py-4">
                          <input
                            type="text"
                            placeholder="Add reason/note..."
                            value={item.eligibilityNote || ''}
                            onChange={(e) => handleNoteChange(item.userId, e.target.value)}
                            className={`w-full bg-transparent border-none p-0 focus:ring-0 text-sm ${
                              !item.isEligible ? 'text-red-700 font-semibold' : 'text-slate-500 italic'
                            }`}
                          />
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>

      {/* Floating Save Footer */}
      {hasChanges && (
        <footer className="fixed bottom-0 left-[240px] right-0 bg-white/90 backdrop-blur-md border-t border-slate-200 px-8 py-4 z-40 flex justify-between items-center shadow-lg animate-fade-in">
          <div className="flex items-center gap-3">
            <div className="relative">
              <div className="w-3 h-3 bg-orange-500 rounded-full animate-pulse"></div>
              <div className="absolute inset-0 w-3 h-3 bg-orange-400 rounded-full scale-150 opacity-20"></div>
            </div>
            <p className="text-slate-800 text-sm font-semibold">Unsaved changes in eligibility list</p>
          </div>
          <div className="flex items-center gap-3">
            <button
              onClick={handleDiscard}
              className="px-6 h-10 text-slate-400 hover:text-slate-700 text-sm font-semibold transition-colors"
            >
              Discard
            </button>
            <button
              onClick={handleSave}
              className="px-8 h-10 bg-primary-500 text-white font-bold text-sm rounded-lg hover:bg-primary-600 shadow-lg shadow-blue-200 transition-all active:scale-[0.98]"
            >
              Save Changes
            </button>
          </div>
        </footer>
      )}
    </AppLayout>
  );
}
