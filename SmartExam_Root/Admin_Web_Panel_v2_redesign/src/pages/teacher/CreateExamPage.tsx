import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import AppLayout from '../../components/layout/AppLayout';
import { examsApi } from '../../api/exams.api';
import type { QuestionType } from '../../types';

interface Section {
  sectionId: string;
  name: string;
  courseName: string;
}

interface Student {
  userId: string;
  name: string;
  email: string;
}

interface QuestionForm {
  type: QuestionType;
  bodyText: string;
  marks: number;
  testCases: {
    input: string;
    expectedOutput: string;
    isHidden: boolean;
  }[];
}

export default function CreateExamPage() {
  const navigate = useNavigate();
  const [step, setStep] = useState(1);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  // Dropdown options loaded from APIs
  const [sections, setSections] = useState<Section[]>([]);
  const [students, setStudents] = useState<Student[]>([]);

  // Form State
  const [title, setTitle] = useState('');
  const [selectedSectionId, setSelectedSectionId] = useState('');
  const [scheduledDate, setScheduledDate] = useState('');
  const [startTime, setStartTime] = useState('09:00');
  const [durationMinutes, setDurationMinutes] = useState(120);
  const [instructions, setInstructions] = useState('');

  const [questions, setQuestions] = useState<QuestionForm[]>([]);

  const [allowedApps, setAllowedApps] = useState('');
  const [aiEvaluationEnabled, setAiEvaluationEnabled] = useState(true);
  const [plagiarismThreshold, setPlagiarismThreshold] = useState(70);

  const [assignedStudentIds, setAssignedStudentIds] = useState<string[]>([]);

  // Load teacher sections on mount
  useEffect(() => {
    examsApi.getSections()
      .then(setSections)
      .catch(err => console.error('Failed to load sections', err));
  }, []);

  // Load students when section changes
  useEffect(() => {
    if (selectedSectionId) {
      examsApi.getSectionStudents(selectedSectionId)
        .then(data => {
          setStudents(data);
          // By default, assign all students enrolled
          setAssignedStudentIds(data.map(s => s.userId));
        })
        .catch(err => console.error('Failed to load students', err));
    } else {
      setStudents([]);
      setAssignedStudentIds([]);
    }
  }, [selectedSectionId]);

  const handleAddQuestion = () => {
    setQuestions(prev => [
      ...prev,
      { type: 'Coding', bodyText: '', marks: 10, testCases: [] }
    ]);
  };

  const handleRemoveQuestion = (index: number) => {
    setQuestions(prev => prev.filter((_, i) => i !== index));
  };

  const handleQuestionChange = (index: number, field: keyof QuestionForm, value: any) => {
    setQuestions(prev => prev.map((q, i) => {
      if (i === index) {
        return { ...q, [field]: value };
      }
      return q;
    }));
  };

  const handleAddTestCase = (qIndex: number) => {
    setQuestions(prev => prev.map((q, i) => {
      if (i === qIndex) {
        return {
          ...q,
          testCases: [...q.testCases, { input: '', expectedOutput: '', isHidden: false }]
        };
      }
      return q;
    }));
  };

  const handleRemoveTestCase = (qIndex: number, tcIndex: number) => {
    setQuestions(prev => prev.map((q, i) => {
      if (i === qIndex) {
        return {
          ...q,
          testCases: q.testCases.filter((_, ti) => ti !== tcIndex)
        };
      }
      return q;
    }));
  };

  const handleTestCaseChange = (qIndex: number, tcIndex: number, field: string, value: any) => {
    setQuestions(prev => prev.map((q, i) => {
      if (i === qIndex) {
        const newTestCases = q.testCases.map((tc, ti) => {
          if (ti === tcIndex) {
            return { ...tc, [field]: value };
          }
          return tc;
        });
        return { ...q, testCases: newTestCases };
      }
      return q;
    }));
  };

  const handleStudentToggle = (studentId: string) => {
    setAssignedStudentIds(prev =>
      prev.includes(studentId)
        ? prev.filter(id => id !== studentId)
        : [...prev, studentId]
    );
  };

  const handleToggleSelectAllStudents = () => {
    if (assignedStudentIds.length === students.length) {
      setAssignedStudentIds([]);
    } else {
      setAssignedStudentIds(students.map(s => s.userId));
    }
  };

  const validateStep = () => {
    setError('');
    if (step === 1) {
      if (!title.trim()) return 'Exam title is required.';
      if (!selectedSectionId) return 'Please select a course/section.';
      if (!scheduledDate) return 'Please select a scheduled date.';
      if (!startTime) return 'Please select a start time.';
    } else if (step === 2) {
      if (questions.length === 0) return 'Please add at least one question.';
      for (let i = 0; i < questions.length; i++) {
        const q = questions[i];
        if (!q.bodyText.trim()) return `Question ${i + 1} description is empty.`;
        if (q.marks <= 0) return `Question ${i + 1} marks must be greater than 0.`;
        if (q.type === 'Coding' && q.testCases.length === 0) {
          return `Question ${i + 1} is coding and must have at least one test case.`;
        }
        for (let j = 0; j < q.testCases.length; j++) {
          const tc = q.testCases[j];
          if (!tc.input.trim() && q.type === 'Coding') return `Question ${i + 1} test case ${j + 1} input is empty.`;
          if (!tc.expectedOutput.trim()) return `Question ${i + 1} test case ${j + 1} expected output is empty.`;
        }
      }
    } else if (step === 4) {
      if (assignedStudentIds.length === 0) return 'Please assign at least one student.';
    }
    return '';
  };

  const handleNext = () => {
    const validationError = validateStep();
    if (validationError) {
      setError(validationError);
      return;
    }
    setStep(prev => prev + 1);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };

  const handleBack = () => {
    setError('');
    setStep(prev => prev - 1);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };

  const handleSubmit = async () => {
    const validationError = validateStep();
    if (validationError) {
      setError(validationError);
      return;
    }

    setLoading(true);
    setError('');

    // Combine date and time
    const startDateTime = new Date(`${scheduledDate}T${startTime}`);

    // Parse apps array
    const appsList = allowedApps
      .split(',')
      .map(app => app.trim())
      .filter(app => app.length > 0);

    const payload = {
      title,
      sectionId: selectedSectionId,
      startTime: startDateTime.toISOString(),
      durationMinutes,
      allowedApps: JSON.stringify(appsList),
      aiEvaluationEnabled,
      plagiarismThreshold,
      questions: questions.map(q => ({
        type: q.type,
        bodyText: q.bodyText,
        marks: q.marks,
        testCases: q.testCases
      })),
      studentIds: assignedStudentIds
    };

    try {
      await examsApi.create(payload);
      navigate('/teacher/dashboard');
    } catch (err: any) {
      setError(err.response?.data?.message || 'Failed to create exam. Please try again.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <AppLayout title="Create New Exam">
      <div className="max-w-4xl mx-auto pb-12">
        {/* Stepper */}
        <div className="mb-10 w-full">
          <div className="flex items-center justify-between relative">
            {/* Step 1 Head */}
            <div className="flex flex-col items-center z-10">
              <div className={`w-10 h-10 rounded-full flex items-center justify-center font-bold transition-all duration-300 ${
                step > 1 
                  ? 'bg-green-500 text-white ring-4 ring-green-100' 
                  : step === 1 
                  ? 'bg-primary-500 text-white ring-4 ring-primary-100' 
                  : 'bg-slate-200 text-slate-500'
              }`}>
                {step > 1 ? <span className="material-symbols-outlined text-[20px]">check</span> : '1'}
              </div>
              <span className={`mt-2 text-[10px] font-semibold uppercase tracking-wider ${step === 1 ? 'text-primary-500 font-bold' : 'text-slate-500'}`}>Basic Info</span>
            </div>
            <div className={`flex-1 border-t-2 transition-all duration-300 mx-4 -mt-6 ${step > 1 ? 'border-green-500' : 'border-slate-200'}`}></div>

            {/* Step 2 Head */}
            <div className="flex flex-col items-center z-10">
              <div className={`w-10 h-10 rounded-full flex items-center justify-center font-bold transition-all duration-300 ${
                step > 2 
                  ? 'bg-green-500 text-white ring-4 ring-green-100' 
                  : step === 2 
                  ? 'bg-primary-500 text-white ring-4 ring-primary-100' 
                  : 'bg-slate-200 text-slate-500'
              }`}>
                {step > 2 ? <span className="material-symbols-outlined text-[20px]">check</span> : '2'}
              </div>
              <span className={`mt-2 text-[10px] font-semibold uppercase tracking-wider ${step === 2 ? 'text-primary-500 font-bold' : 'text-slate-500'}`}>Questions</span>
            </div>
            <div className={`flex-1 border-t-2 transition-all duration-300 mx-4 -mt-6 ${step > 2 ? 'border-green-500' : 'border-slate-200'}`}></div>

            {/* Step 3 Head */}
            <div className="flex flex-col items-center z-10">
              <div className={`w-10 h-10 rounded-full flex items-center justify-center font-bold transition-all duration-300 ${
                step > 3 
                  ? 'bg-green-500 text-white ring-4 ring-green-100' 
                  : step === 3 
                  ? 'bg-primary-500 text-white ring-4 ring-primary-100' 
                  : 'bg-slate-200 text-slate-500'
              }`}>
                {step > 3 ? <span className="material-symbols-outlined text-[20px]">check</span> : '3'}
              </div>
              <span className={`mt-2 text-[10px] font-semibold uppercase tracking-wider ${step === 3 ? 'text-primary-500 font-bold' : 'text-slate-500'}`}>Rules & Settings</span>
            </div>
            <div className={`flex-1 border-t-2 transition-all duration-300 mx-4 -mt-6 ${step > 3 ? 'border-green-500' : 'border-slate-200'}`}></div>

            {/* Step 4 Head */}
            <div className="flex flex-col items-center z-10">
              <div className={`w-10 h-10 rounded-full flex items-center justify-center font-bold transition-all duration-300 ${
                step === 4 
                  ? 'bg-primary-500 text-white ring-4 ring-primary-100' 
                  : 'bg-slate-200 text-slate-500'
              }`}>
                4
              </div>
              <span className={`mt-2 text-[10px] font-semibold uppercase tracking-wider ${step === 4 ? 'text-primary-500 font-bold' : 'text-slate-500'}`}>Assign Students</span>
            </div>
          </div>
        </div>

        {/* Error Message */}
        {error && (
          <div className="mb-6 p-4 bg-red-50 border border-red-200 text-red-600 rounded-xl text-sm font-medium flex items-center gap-2">
            <span className="material-symbols-outlined">error</span>
            {error}
          </div>
        )}

        {/* Form Content */}
        <div className="bg-white border border-slate-200 rounded-xl shadow-sm overflow-hidden mb-6">
          
          {/* STEP 1: BASIC INFO */}
          {step === 1 && (
            <div className="p-8 space-y-6">
              <div className="border-b border-slate-100 pb-4 mb-6">
                <h3 className="text-xl font-bold text-slate-800">General Information</h3>
                <p className="text-sm text-slate-400">Provide the core details for this assessment.</p>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                <div className="space-y-4">
                  <div>
                    <label htmlFor="examTitle" className="block text-[11px] font-bold text-slate-500 uppercase tracking-wider mb-2">EXAM TITLE</label>
                    <input 
                      id="examTitle"
                      type="text" 
                      value={title}
                      onChange={e => setTitle(e.target.value)}
                      className="w-full px-4 py-2.5 border border-slate-200 rounded-xl focus:ring-2 focus:ring-primary-500/10 focus:border-primary-500 outline-none transition-all text-slate-800"
                      placeholder="e.g. CS102 - Data Structures Midterm"
                    />
                  </div>

                  <div>
                    <label htmlFor="section" className="block text-[11px] font-bold text-slate-500 uppercase tracking-wider mb-2">COURSE / SECTION</label>
                    <select 
                      id="section"
                      value={selectedSectionId}
                      onChange={e => setSelectedSectionId(e.target.value)}
                      className="w-full px-4 py-2.5 border border-slate-200 rounded-xl focus:ring-2 focus:ring-primary-500/10 focus:border-primary-500 outline-none transition-all bg-white text-slate-800"
                    >
                      <option value="">Select a Course Section</option>
                      {sections.map(sec => (
                        <option key={sec.sectionId} value={sec.sectionId}>
                          {sec.courseName} ({sec.name})
                        </option>
                      ))}
                    </select>
                  </div>
                </div>

                <div className="space-y-4">
                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <label htmlFor="scheduledDate" className="block text-[11px] font-bold text-slate-500 uppercase tracking-wider mb-2">SCHEDULED DATE</label>
                      <input 
                        id="scheduledDate"
                        type="date" 
                        value={scheduledDate}
                        onChange={e => setScheduledDate(e.target.value)}
                        className="w-full px-4 py-2.5 border border-slate-200 rounded-xl focus:ring-2 focus:ring-primary-500/10 focus:border-primary-500 outline-none transition-all text-slate-800"
                      />
                    </div>
                    <div>
                      <label htmlFor="startTime" className="block text-[11px] font-bold text-slate-500 uppercase tracking-wider mb-2">START TIME</label>
                      <input 
                        id="startTime"
                        type="time" 
                        value={startTime}
                        onChange={e => setStartTime(e.target.value)}
                        className="w-full px-4 py-2.5 border border-slate-200 rounded-xl focus:ring-2 focus:ring-primary-500/10 focus:border-primary-500 outline-none transition-all text-slate-800"
                      />
                    </div>
                  </div>

                  <div>
                    <label htmlFor="duration" className="block text-[11px] font-bold text-slate-500 uppercase tracking-wider mb-2">DURATION (MINUTES)</label>
                    <div className="flex items-center border border-slate-200 rounded-xl overflow-hidden focus-within:ring-2 focus-within:ring-primary-500/10 focus-within:border-primary-500 transition-all">
                      <button 
                        type="button"
                        onClick={() => setDurationMinutes(m => Math.max(15, m - 15))}
                        className="p-3 text-primary-500 hover:bg-slate-50 transition-colors"
                      >
                        <span className="material-symbols-outlined text-[20px] font-bold">remove_circle</span>
                      </button>
                      <input 
                        id="duration"
                        type="number" 
                        value={durationMinutes}
                        onChange={e => setDurationMinutes(Math.max(15, parseInt(e.target.value) || 0))}
                        className="flex-1 text-center py-2.5 border-none outline-none focus:ring-0 text-slate-800 font-semibold"
                      />
                      <button 
                        type="button"
                        onClick={() => setDurationMinutes(m => m + 15)}
                        className="p-3 text-primary-500 hover:bg-slate-50 transition-colors"
                      >
                        <span className="material-symbols-outlined text-[20px] font-bold">add_circle</span>
                      </button>
                    </div>
                  </div>
                </div>
              </div>

              <div>
                <label htmlFor="instructions" className="block text-[11px] font-bold text-slate-500 uppercase tracking-wider mb-2">INSTRUCTIONS</label>
                <textarea 
                  id="instructions"
                  value={instructions}
                  onChange={e => setInstructions(e.target.value)}
                  className="w-full px-4 py-2.5 border border-slate-200 rounded-xl focus:ring-2 focus:ring-primary-500/10 focus:border-primary-500 outline-none transition-all resize-none text-slate-800"
                  placeholder="Mention rules regarding permitted IDEs, internet access, calculations, etc."
                  rows={4}
                />
              </div>
            </div>
          )}

          {/* STEP 2: QUESTIONS */}
          {step === 2 && (
            <div className="p-8 space-y-6">
              <div className="border-b border-slate-100 pb-4 mb-6 flex justify-between items-end">
                <div>
                  <h3 className="text-xl font-bold text-slate-800">Exam Questions</h3>
                  <p className="text-sm text-slate-400">Manage and organize your assessment items.</p>
                </div>
                <button 
                  type="button"
                  onClick={handleAddQuestion}
                  className="flex items-center gap-1.5 px-4 py-2 bg-blue-50 text-primary-600 rounded-xl font-semibold text-sm hover:bg-blue-100 transition-all border border-blue-200"
                >
                  <span className="material-symbols-outlined text-[18px]">add</span>
                  Add Question
                </button>
              </div>

              {questions.length === 0 ? (
                <div className="text-center py-12 border-2 border-dashed border-slate-200 rounded-xl bg-slate-50">
                  <span className="material-symbols-outlined text-[48px] text-slate-300 mb-2">assignment</span>
                  <p className="text-sm font-semibold text-slate-600">No questions added yet.</p>
                  <p className="text-xs text-slate-400 mb-4">Add questions for this exam to proceed.</p>
                  <button 
                    type="button"
                    onClick={handleAddQuestion}
                    className="px-4 py-2 bg-primary-500 text-white rounded-lg font-semibold text-xs hover:bg-primary-600 transition-all"
                  >
                    Add First Question
                  </button>
                </div>
              ) : (
                <div className="space-y-6">
                  {questions.map((q, qIndex) => (
                    <div key={qIndex} className="bg-slate-50 border border-slate-200 rounded-xl overflow-hidden shadow-sm">
                      {/* Question Header */}
                      <div className="p-4 bg-white border-b border-slate-200 flex items-center justify-between">
                        <div className="flex items-center gap-3">
                          <span className="px-3 py-1 bg-blue-50 text-primary-600 rounded-full text-[10px] font-bold uppercase tracking-wider">
                            Q{qIndex + 1} - {q.type}
                          </span>
                          <select 
                            value={q.type}
                            onChange={e => handleQuestionChange(qIndex, 'type', e.target.value as QuestionType)}
                            className="px-2 py-1 border border-slate-200 rounded-lg text-xs bg-white text-slate-800"
                          >
                            <option value="Coding">Coding</option>
                            <option value="Theory">Theory</option>
                          </select>
                        </div>
                        <div className="flex items-center gap-4">
                          <div className="flex items-center gap-2">
                            <span className="text-[10px] font-bold text-slate-400 uppercase">Marks</span>
                            <input 
                              type="number"
                              value={q.marks}
                              onChange={e => handleQuestionChange(qIndex, 'marks', Math.max(1, parseInt(e.target.value) || 0))}
                              className="w-16 px-2 py-1 border border-slate-200 rounded-lg text-xs text-center font-bold text-slate-800"
                            />
                          </div>
                          <button 
                            type="button"
                            onClick={() => handleRemoveQuestion(qIndex)}
                            className="text-slate-400 hover:text-red-500 transition-colors"
                          >
                            <span className="material-symbols-outlined text-[20px]">delete</span>
                          </button>
                        </div>
                      </div>

                      {/* Question Body */}
                      <div className="p-6 space-y-4 bg-white">
                        <div>
                          <label className="block text-[10px] font-bold text-slate-500 uppercase tracking-wider mb-2">QUESTION TEXT</label>
                          <textarea 
                            value={q.bodyText}
                            onChange={e => handleQuestionChange(qIndex, 'bodyText', e.target.value)}
                            className="w-full px-4 py-2 border border-slate-200 rounded-xl focus:ring-2 focus:ring-primary-500/10 focus:border-primary-500 outline-none transition-all text-slate-800"
                            placeholder="Write the question prompt or coding task details..."
                            rows={3}
                          />
                        </div>

                        {/* Test Cases (Only for Coding type) */}
                        {q.type === 'Coding' && (
                          <div className="mt-4 border border-slate-200 rounded-xl overflow-hidden bg-slate-50/50">
                            <div className="px-4 py-2 border-b border-slate-200 flex justify-between items-center bg-slate-50">
                              <span className="font-semibold text-[10px] text-slate-500 uppercase tracking-wider">Test Cases ({q.testCases.length})</span>
                              <button 
                                type="button"
                                onClick={() => handleAddTestCase(qIndex)}
                                className="text-primary-500 text-xs font-bold hover:underline flex items-center gap-1"
                              >
                                <span className="material-symbols-outlined text-[16px]">add</span> Add Test Case
                              </button>
                            </div>
                            
                            {q.testCases.length === 0 ? (
                              <div className="p-4 text-center text-xs text-slate-400 italic">No test cases. A coding question requires test cases for automatic grading.</div>
                            ) : (
                              <table className="w-full text-left text-xs border-collapse">
                                <thead>
                                  <tr className="border-b border-slate-200 text-slate-400">
                                    <th className="px-4 py-2 uppercase font-medium">Input arguments</th>
                                    <th className="px-4 py-2 uppercase font-medium">Expected Output</th>
                                    <th className="px-4 py-2 uppercase font-medium text-center">Hidden?</th>
                                    <th className="px-4 py-2 text-right">Action</th>
                                  </tr>
                                </thead>
                                <tbody>
                                  {q.testCases.map((tc, tcIndex) => (
                                    <tr key={tcIndex} className="border-b border-slate-100 bg-white">
                                      <td className="px-4 py-2 font-mono">
                                        <input 
                                          type="text"
                                          value={tc.input}
                                          onChange={e => handleTestCaseChange(qIndex, tcIndex, 'input', e.target.value)}
                                          className="w-full bg-slate-50 border border-slate-200 rounded px-2 py-1 text-slate-800 font-mono text-[11px]"
                                          placeholder="e.g. [5, 3], 4"
                                        />
                                      </td>
                                      <td className="px-4 py-2 font-mono">
                                        <input 
                                          type="text"
                                          value={tc.expectedOutput}
                                          onChange={e => handleTestCaseChange(qIndex, tcIndex, 'expectedOutput', e.target.value)}
                                          className="w-full bg-slate-50 border border-slate-200 rounded px-2 py-1 text-slate-800 font-mono text-[11px]"
                                          placeholder="e.g. 15"
                                        />
                                      </td>
                                      <td className="px-4 py-2 text-center">
                                        <input 
                                          type="checkbox"
                                          checked={tc.isHidden}
                                          onChange={e => handleTestCaseChange(qIndex, tcIndex, 'isHidden', e.target.checked)}
                                          className="rounded border-slate-200 text-primary-500 focus:ring-primary-500/20"
                                        />
                                      </td>
                                      <td className="px-4 py-2 text-right">
                                        <button 
                                          type="button"
                                          onClick={() => handleRemoveTestCase(qIndex, tcIndex)}
                                          className="text-slate-400 hover:text-red-500 transition-colors"
                                        >
                                          <span className="material-symbols-outlined text-[16px]">close</span>
                                        </button>
                                      </td>
                                    </tr>
                                  ))}
                                </tbody>
                              </table>
                            )}
                          </div>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}

          {/* STEP 3: RULES & SETTINGS */}
          {step === 3 && (
            <div className="p-8 space-y-6">
              <div className="border-b border-slate-100 pb-4 mb-6">
                <h3 className="text-xl font-bold text-slate-800">Rules & Integrity Settings</h3>
                <p className="text-sm text-slate-400">Configure security boundaries and automatic assessments.</p>
              </div>

              <div className="space-y-6">
                <div>
                  <label className="block text-[11px] font-bold text-slate-500 uppercase tracking-wider mb-2">
                    ALLOWED APPLICATIONS (COMMA-SEPARATED)
                  </label>
                  <input 
                    type="text" 
                    value={allowedApps}
                    onChange={e => setAllowedApps(e.target.value)}
                    className="w-full px-4 py-2.5 border border-slate-200 rounded-xl focus:ring-2 focus:ring-primary-500/10 focus:border-primary-500 outline-none transition-all text-slate-800"
                    placeholder="e.g. code.exe, codeblocks.exe, chrome.exe"
                  />
                  <p className="text-xs text-slate-400 mt-1">
                    Enter the executable file names that students are permitted to open. Leave empty to allow any software.
                  </p>
                </div>

                <div className="flex items-start justify-between p-4 bg-slate-50 border border-slate-200 rounded-xl">
                  <div className="space-y-1">
                    <span className="font-semibold text-sm text-slate-800 block">Enable AI Evaluation</span>
                    <span className="text-xs text-slate-500">Automatically grade submissions using LLMs based on Rubric instructions.</span>
                  </div>
                  <input 
                    type="checkbox"
                    checked={aiEvaluationEnabled}
                    onChange={e => setAiEvaluationEnabled(e.target.checked)}
                    className="rounded-full w-10 h-6 bg-slate-300 border-none text-primary-500 focus:ring-primary-500/20"
                    style={{ appearance: 'checkbox' }} // Simplified UI check
                  />
                </div>

                <div>
                  <div className="flex justify-between items-baseline mb-2">
                    <label className="text-[11px] font-bold text-slate-500 uppercase tracking-wider">
                      PLAGIARISM DETECTOR THRESHOLD
                    </label>
                    <span className="text-sm font-bold text-primary-500">{plagiarismThreshold}% Similarity</span>
                  </div>
                  <input 
                    type="range"
                    min="30"
                    max="95"
                    step="5"
                    value={plagiarismThreshold}
                    onChange={e => setPlagiarismThreshold(parseInt(e.target.value))}
                    className="w-full accent-primary-500"
                  />
                  <p className="text-xs text-slate-400 mt-1">
                    Highlight submissions in red when code similarity meets or exceeds this threshold.
                  </p>
                </div>
              </div>
            </div>
          )}

          {/* STEP 4: ASSIGN STUDENTS */}
          {step === 4 && (
            <div className="p-8 space-y-6">
              <div className="border-b border-slate-100 pb-4 mb-6 flex justify-between items-end">
                <div>
                  <h3 className="text-xl font-bold text-slate-800">Assign Enrolled Students</h3>
                  <p className="text-sm text-slate-400">Select which students from the section will sit for this exam.</p>
                </div>
                <button 
                  type="button"
                  onClick={handleToggleSelectAllStudents}
                  className="px-4 py-2 bg-slate-100 hover:bg-slate-200 text-slate-700 rounded-xl font-semibold text-xs border border-slate-200 transition-colors"
                >
                  {assignedStudentIds.length === students.length ? 'Deselect All' : 'Select All'}
                </button>
              </div>

              {students.length === 0 ? (
                <div className="text-center py-8 text-slate-400 italic text-sm">
                  No students are enrolled in this section. Please enroll students or select another section.
                </div>
              ) : (
                <div className="border border-slate-200 rounded-xl overflow-hidden">
                  <table className="w-full text-left text-sm border-collapse">
                    <thead className="bg-slate-50 border-b border-slate-200">
                      <tr>
                        <th className="px-6 py-3 font-bold text-slate-500 text-xs uppercase tracking-wider w-16 text-center">Assign</th>
                        <th className="px-6 py-3 font-bold text-slate-500 text-xs uppercase tracking-wider">Student Name</th>
                        <th className="px-6 py-3 font-bold text-slate-500 text-xs uppercase tracking-wider">Email Address</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                      {students.map(s => (
                        <tr key={s.userId} className="hover:bg-slate-50/50">
                          <td className="px-6 py-3.5 text-center">
                            <input 
                              type="checkbox"
                              checked={assignedStudentIds.includes(s.userId)}
                              onChange={() => handleStudentToggle(s.userId)}
                              className="rounded border-slate-200 text-primary-500 focus:ring-primary-500/20"
                            />
                          </td>
                          <td className="px-6 py-3.5 font-medium text-slate-800">{s.name}</td>
                          <td className="px-6 py-3.5 text-slate-500">{s.email}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          )}

          {/* Card Footer Actions */}
          <div className="p-6 bg-slate-50 border-t border-slate-200 flex justify-between items-center">
            {step === 1 ? (
              <button 
                type="button"
                onClick={() => navigate('/teacher/dashboard')}
                className="px-6 py-2.5 rounded-xl border border-slate-300 text-slate-600 font-semibold hover:bg-slate-100 transition-all text-sm"
              >
                Cancel
              </button>
            ) : (
              <button 
                type="button"
                onClick={handleBack}
                className="flex items-center gap-1 px-5 py-2.5 rounded-xl border border-slate-300 text-slate-600 font-semibold hover:bg-slate-100 transition-all text-sm"
              >
                <span className="material-symbols-outlined text-[18px]">arrow_back</span>
                Back
              </button>
            )}

            {step < 4 ? (
              <button 
                type="button"
                onClick={handleNext}
                className="px-8 py-2.5 bg-primary-500 text-white rounded-xl font-semibold shadow-lg shadow-primary-500/20 hover:bg-primary-600 transition-all active:scale-95 text-sm"
              >
                Next Step
              </button>
            ) : (
              <button 
                type="button"
                onClick={handleSubmit}
                disabled={loading}
                className="px-8 py-2.5 bg-primary-500 text-white rounded-xl font-semibold shadow-lg shadow-primary-500/20 hover:bg-primary-600 transition-all active:scale-95 disabled:opacity-50 disabled:pointer-events-none text-sm flex items-center gap-2"
              >
                {loading && <div className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />}
                Create Exam
              </button>
            )}
          </div>

        </div>
      </div>
    </AppLayout>
  );
}
