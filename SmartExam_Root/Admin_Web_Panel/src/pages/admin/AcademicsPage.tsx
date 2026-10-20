import { useState, useEffect, useMemo } from 'react';
import AppLayout from '../../components/layout/AppLayout';
import { academicsApi } from '../../api/academics.api';
import type { Course, Department } from '../../types';

const errorMessage = (err: any, fallback: string): string => err?.response?.data?.message || fallback;

const inputClass = 'h-9 px-3 border border-slate-200 rounded-lg focus:ring-2 focus:ring-primary-500 outline-none text-xs bg-white';

/**
 * Module 7 — academic structure (Department → Course; sections hang off courses).
 * Admins add, rename and delete departments and courses. The API refuses to delete a department
 * that still has courses or a course that still has sections (409), and keeps department names
 * and per-department course codes unique; its messages are shown as-is.
 */
export default function AcademicsPage() {
  const [departments, setDepartments] = useState<Department[]>([]);
  const [courses, setCourses] = useState<Course[]>([]);
  const [loading, setLoading] = useState(true);
  const [notice, setNotice] = useState<{ kind: 'error' | 'ok'; text: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [deptFilter, setDeptFilter] = useState('');

  // Forms
  const [newDeptName, setNewDeptName] = useState('');
  const [editingDept, setEditingDept] = useState<{ id: string; name: string } | null>(null);
  const [newCourse, setNewCourse] = useState({ deptId: '', name: '', code: '' });
  const [editingCourse, setEditingCourse] = useState<{ id: string; deptId: string; name: string; code: string } | null>(null);

  const load = async () => {
    try {
      const [d, c] = await Promise.all([academicsApi.getDepartments(), academicsApi.getCourses()]);
      setDepartments(d);
      setCourses(c);
      setNewCourse(prev => (prev.deptId || d.length === 0 ? prev : { ...prev, deptId: d[0].deptId }));
    } catch (err) {
      setNotice({ kind: 'error', text: errorMessage(err, 'Failed to load departments and courses.') });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(); }, []);

  const visibleCourses = useMemo(
    () => (deptFilter ? courses.filter(c => c.deptId === deptFilter) : courses),
    [courses, deptFilter],
  );

  // Runs one mutation, shows its outcome and reloads both lists (course counts change with courses).
  const run = async (action: () => Promise<unknown>, success: string, failure: string) => {
    setBusy(true);
    setNotice(null);
    try {
      await action();
      setNotice({ kind: 'ok', text: success });
      await load();
      return true;
    } catch (err) {
      setNotice({ kind: 'error', text: errorMessage(err, failure) });
      return false;
    } finally {
      setBusy(false);
    }
  };

  const handleCreateDept = async (e: React.FormEvent) => {
    e.preventDefault();
    const name = newDeptName.trim();
    if (!name) return;
    if (await run(() => academicsApi.createDepartment(name), `Department "${name}" created.`, 'Failed to create department.'))
      setNewDeptName('');
  };

  const handleSaveDept = async () => {
    if (!editingDept) return;
    const name = editingDept.name.trim();
    if (await run(() => academicsApi.updateDepartment(editingDept.id, name), 'Department renamed.', 'Failed to rename department.'))
      setEditingDept(null);
  };

  const handleDeleteDept = async (d: Department) => {
    if (!window.confirm(`Delete department "${d.name}"?`)) return;
    await run(() => academicsApi.deleteDepartment(d.deptId), `Department "${d.name}" deleted.`, 'Failed to delete department.');
    if (deptFilter === d.deptId) setDeptFilter('');
  };

  const handleCreateCourse = async (e: React.FormEvent) => {
    e.preventDefault();
    const { deptId, name, code } = newCourse;
    if (!deptId || !name.trim() || !code.trim()) return;
    if (await run(() => academicsApi.createCourse(deptId, name.trim(), code.trim()),
      `Course ${code.trim().toUpperCase()} created.`, 'Failed to create course.'))
      setNewCourse({ deptId, name: '', code: '' });
  };

  const handleSaveCourse = async () => {
    if (!editingCourse) return;
    const { id, deptId, name, code } = editingCourse;
    if (await run(() => academicsApi.updateCourse(id, deptId, name.trim(), code.trim()), 'Course updated.', 'Failed to update course.'))
      setEditingCourse(null);
  };

  const handleDeleteCourse = async (c: Course) => {
    if (!window.confirm(`Delete course ${c.code} — ${c.name}?`)) return;
    await run(() => academicsApi.deleteCourse(c.courseId), `Course ${c.code} deleted.`, 'Failed to delete course.');
  };

  return (
    <AppLayout title="Departments & Courses">
      <div className="space-y-6">
        <div>
          <h3 className="text-lg font-bold text-slate-800">Academic Structure</h3>
          <p className="text-sm text-slate-400">
            Departments own courses; teachers' sections (and their exams) belong to a course.
          </p>
        </div>

        {notice && (
          <div
            role={notice.kind === 'error' ? 'alert' : 'status'}
            className={`flex items-start justify-between gap-4 text-sm rounded-lg px-4 py-3 border ${
              notice.kind === 'error' ? 'bg-red-50 border-red-200 text-red-700' : 'bg-green-50 border-green-200 text-green-800'
            }`}
          >
            <span>{notice.text}</span>
            <button onClick={() => setNotice(null)} className="text-xs font-bold opacity-60 hover:opacity-100" aria-label="Dismiss">✕</button>
          </div>
        )}

        {loading ? (
          <div className="min-h-[300px] flex items-center justify-center">
            <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin" />
          </div>
        ) : (
          <div className="grid grid-cols-1 xl:grid-cols-[2fr_3fr] gap-6 items-start">
            {/* ── Departments ── */}
            <section data-testid="departments-panel" className="bg-white border border-slate-200 rounded-xl overflow-hidden shadow-sm">
              <div className="px-5 py-4 border-b border-slate-200">
                <h4 className="text-base font-bold text-slate-800">Departments</h4>
                <p className="text-xs text-slate-400">{departments.length} department(s)</p>
              </div>
              <form onSubmit={handleCreateDept} className="px-5 py-3 border-b border-slate-100 bg-slate-50/60 flex gap-2 items-end">
                <div className="flex-1 space-y-1">
                  <label htmlFor="dept-name" className="text-[10px] font-bold tracking-wider text-slate-400 uppercase">Department name</label>
                  <input id="dept-name" value={newDeptName} onChange={e => setNewDeptName(e.target.value)} maxLength={128}
                         placeholder="e.g. Electrical Engineering" required className={`${inputClass} w-full`} />
                </div>
                <button type="submit" disabled={busy}
                        className="px-4 h-9 bg-primary-500 text-white font-bold text-xs rounded-lg hover:bg-primary-600 disabled:opacity-50">
                  Add Department
                </button>
              </form>
              <table className="w-full text-left">
                <thead>
                  <tr className="bg-slate-50 border-b border-slate-200">
                    <th className="px-5 py-3 text-[10px] font-bold tracking-wider text-slate-400 uppercase">Name</th>
                    <th className="px-5 py-3 text-[10px] font-bold tracking-wider text-slate-400 uppercase text-center">Courses</th>
                    <th className="px-5 py-3 w-36"></th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {departments.length === 0 && (
                    <tr><td colSpan={3} className="px-5 py-8 text-center text-sm text-slate-400">No departments yet.</td></tr>
                  )}
                  {departments.map(d => (
                    <tr key={d.deptId} data-testid="department-row" className="hover:bg-slate-50/50">
                      <td className="px-5 py-3 text-sm font-semibold text-slate-700">
                        {editingDept?.id === d.deptId ? (
                          <input aria-label="Department name" autoFocus value={editingDept.name} maxLength={128}
                                 onChange={e => setEditingDept({ ...editingDept, name: e.target.value })}
                                 onKeyDown={e => { if (e.key === 'Enter') handleSaveDept(); if (e.key === 'Escape') setEditingDept(null); }}
                                 className={`${inputClass} w-full`} />
                        ) : d.name}
                      </td>
                      <td className="px-5 py-3 text-sm text-slate-500 text-center" data-testid="department-course-count">{d.courseCount}</td>
                      <td className="px-5 py-3 text-right whitespace-nowrap">
                        {editingDept?.id === d.deptId ? (
                          <>
                            <button onClick={handleSaveDept} disabled={busy} className="text-xs font-bold text-primary-600 hover:underline mr-3">Save</button>
                            <button onClick={() => setEditingDept(null)} className="text-xs font-semibold text-slate-400 hover:underline">Cancel</button>
                          </>
                        ) : (
                          <>
                            <button onClick={() => setEditingDept({ id: d.deptId, name: d.name })} className="text-xs font-bold text-primary-600 hover:underline mr-3">Rename</button>
                            <button onClick={() => handleDeleteDept(d)} disabled={busy}
                                    title={d.courseCount > 0 ? 'Delete its courses first' : undefined}
                                    className="text-xs font-bold text-red-600 hover:underline">Delete</button>
                          </>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </section>

            {/* ── Courses ── */}
            <section data-testid="courses-panel" className="bg-white border border-slate-200 rounded-xl overflow-hidden shadow-sm">
              <div className="px-5 py-4 border-b border-slate-200 flex items-center justify-between gap-3">
                <div>
                  <h4 className="text-base font-bold text-slate-800">Courses</h4>
                  <p className="text-xs text-slate-400">{visibleCourses.length} course(s)</p>
                </div>
                <select aria-label="Filter by department" value={deptFilter} onChange={e => setDeptFilter(e.target.value)}
                        className={`${inputClass} cursor-pointer`}>
                  <option value="">All departments</option>
                  {departments.map(d => <option key={d.deptId} value={d.deptId}>{d.name}</option>)}
                </select>
              </div>
              <form onSubmit={handleCreateCourse} className="px-5 py-3 border-b border-slate-100 bg-slate-50/60 flex flex-wrap gap-2 items-end">
                <div className="space-y-1">
                  <label htmlFor="course-dept" className="text-[10px] font-bold tracking-wider text-slate-400 uppercase">Department</label>
                  <select id="course-dept" value={newCourse.deptId} required onChange={e => setNewCourse({ ...newCourse, deptId: e.target.value })}
                          className={`${inputClass} cursor-pointer block`}>
                    {departments.length === 0 && <option value="">Add a department first</option>}
                    {departments.map(d => <option key={d.deptId} value={d.deptId}>{d.name}</option>)}
                  </select>
                </div>
                <div className="space-y-1">
                  <label htmlFor="course-code" className="text-[10px] font-bold tracking-wider text-slate-400 uppercase">Course code</label>
                  <input id="course-code" value={newCourse.code} maxLength={32} required placeholder="e.g. CS401"
                         onChange={e => setNewCourse({ ...newCourse, code: e.target.value })}
                         className={`${inputClass} w-28 font-mono uppercase block`} />
                </div>
                <div className="flex-1 min-w-[160px] space-y-1">
                  <label htmlFor="course-name" className="text-[10px] font-bold tracking-wider text-slate-400 uppercase">Course name</label>
                  <input id="course-name" value={newCourse.name} maxLength={128} required placeholder="e.g. Operating Systems"
                         onChange={e => setNewCourse({ ...newCourse, name: e.target.value })}
                         className={`${inputClass} w-full block`} />
                </div>
                <button type="submit" disabled={busy || departments.length === 0}
                        className="px-4 h-9 bg-primary-500 text-white font-bold text-xs rounded-lg hover:bg-primary-600 disabled:opacity-50">
                  Add Course
                </button>
              </form>
              <table className="w-full text-left">
                <thead>
                  <tr className="bg-slate-50 border-b border-slate-200">
                    <th className="px-5 py-3 text-[10px] font-bold tracking-wider text-slate-400 uppercase">Code</th>
                    <th className="px-5 py-3 text-[10px] font-bold tracking-wider text-slate-400 uppercase">Name</th>
                    <th className="px-5 py-3 text-[10px] font-bold tracking-wider text-slate-400 uppercase">Department</th>
                    <th className="px-5 py-3 text-[10px] font-bold tracking-wider text-slate-400 uppercase text-center">Sections</th>
                    <th className="px-5 py-3 w-32"></th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {visibleCourses.length === 0 && (
                    <tr><td colSpan={5} className="px-5 py-8 text-center text-sm text-slate-400">No courses.</td></tr>
                  )}
                  {visibleCourses.map(c => editingCourse?.id === c.courseId ? (
                    <tr key={c.courseId} data-testid="course-row" className="bg-blue-50/40">
                      <td className="px-5 py-2">
                        <input aria-label="Course code" value={editingCourse.code} maxLength={32}
                               onChange={e => setEditingCourse({ ...editingCourse, code: e.target.value })}
                               className={`${inputClass} w-24 font-mono uppercase`} />
                      </td>
                      <td className="px-5 py-2">
                        <input aria-label="Course name" value={editingCourse.name} maxLength={128}
                               onChange={e => setEditingCourse({ ...editingCourse, name: e.target.value })}
                               className={`${inputClass} w-full`} />
                      </td>
                      <td className="px-5 py-2">
                        <select aria-label="Course department" value={editingCourse.deptId}
                                onChange={e => setEditingCourse({ ...editingCourse, deptId: e.target.value })}
                                className={`${inputClass} cursor-pointer`}>
                          {departments.map(d => <option key={d.deptId} value={d.deptId}>{d.name}</option>)}
                        </select>
                      </td>
                      <td className="px-5 py-2 text-sm text-slate-500 text-center">{c.sectionCount}</td>
                      <td className="px-5 py-2 text-right whitespace-nowrap">
                        <button onClick={handleSaveCourse} disabled={busy} className="text-xs font-bold text-primary-600 hover:underline mr-3">Save</button>
                        <button onClick={() => setEditingCourse(null)} className="text-xs font-semibold text-slate-400 hover:underline">Cancel</button>
                      </td>
                    </tr>
                  ) : (
                    <tr key={c.courseId} data-testid="course-row" className="hover:bg-slate-50/50">
                      <td className="px-5 py-3 text-sm font-mono font-bold text-slate-700">{c.code}</td>
                      <td className="px-5 py-3 text-sm text-slate-700">{c.name}</td>
                      <td className="px-5 py-3 text-sm text-slate-500">{c.departmentName}</td>
                      <td className="px-5 py-3 text-sm text-slate-500 text-center">{c.sectionCount}</td>
                      <td className="px-5 py-3 text-right whitespace-nowrap">
                        <button onClick={() => setEditingCourse({ id: c.courseId, deptId: c.deptId, name: c.name, code: c.code })}
                                className="text-xs font-bold text-primary-600 hover:underline mr-3">Edit</button>
                        <button onClick={() => handleDeleteCourse(c)} disabled={busy}
                                title={c.sectionCount > 0 ? 'Remove its sections first' : undefined}
                                className="text-xs font-bold text-red-600 hover:underline">Delete</button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </section>
          </div>
        )}
      </div>
    </AppLayout>
  );
}
