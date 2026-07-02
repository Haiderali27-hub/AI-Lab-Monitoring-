import { useState, useEffect, useRef } from 'react';
import AppLayout from '../../components/layout/AppLayout';
import { usersApi } from '../../api/users.api';
import type { User, UserRole } from '../../types';

export default function UsersPage() {
  const [users, setUsers] = useState<User[]>([]);
  const [activeTab, setActiveTab] = useState<UserRole | 'All'>('All');
  const [search, setSearch] = useState('');
  const [showPanel, setShowPanel] = useState(false);
  const [loading, setLoading] = useState(true);

  // Form state
  const [formName, setFormName] = useState('');
  const [formEmail, setFormEmail] = useState('');
  const [formPassword, setFormPassword] = useState('');
  const [formRole, setFormRole] = useState<UserRole>('Student');
  const [formError, setFormError] = useState('');
  const [formLoading, setFormLoading] = useState(false);
  const [editingUser, setEditingUser] = useState<User | null>(null);

  // CSV import
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const [importing, setImporting] = useState(false);

  // Dropdown menu state
  const [activeMenuId, setActiveMenuId] = useState<string | null>(null);

  const menuRef = useRef<HTMLDivElement | null>(null);

  const openCreatePanel = () => {
    setEditingUser(null);
    setFormName('');
    setFormEmail('');
    setFormPassword('');
    setFormRole('Student');
    setFormError('');
    setShowPanel(true);
    setActiveMenuId(null);
  };

  const openEditPanel = (user: User) => {
    setEditingUser(user);
    setFormName(user.name);
    setFormEmail(user.email);
    setFormPassword('');
    setFormRole(user.role);
    setFormError('');
    setShowPanel(true);
    setActiveMenuId(null);
  };

  const loadUsers = async () => {
    setLoading(true);
    try {
      const data = await usersApi.getAll();
      setUsers(data);
    } catch (err) {
      console.error('Failed to load users:', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadUsers();
  }, []);

  // Handle click outside menu to close it
  useEffect(() => {
    const handleOutsideClick = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        setActiveMenuId(null);
      }
    };
    document.addEventListener('mousedown', handleOutsideClick);
    return () => document.removeEventListener('mousedown', handleOutsideClick);
  }, []);

  const handleSubmitUser = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError('');
    setFormLoading(true);
    try {
      if (editingUser) {
        await usersApi.update(editingUser.userId, {
          name: formName,
          email: formEmail,
          role: formRole,
          password: formPassword.trim() ? formPassword : undefined
        });
      } else {
        await usersApi.create({
          name: formName,
          email: formEmail,
          password: formPassword,
          role: formRole
        });
      }
      setShowPanel(false);
      setEditingUser(null);
      loadUsers();
    } catch (err: any) {
      console.error(err);
      setFormError(err.response?.data?.message || `Failed to ${editingUser ? 'update' : 'create'} user`);
    } finally {
      setFormLoading(false);
    }
  };

  const handleToggleActive = async (user: User) => {
    setActiveMenuId(null);
    try {
      if (user.isActive) {
        if (!window.confirm(`Deactivate ${user.name}? They will be logged out and unable to sign in.`)) return;
        await usersApi.deactivate(user.userId);
      } else {
        await usersApi.activate(user.userId);
      }
      loadUsers();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to change account status.');
    }
  };

  const handleDelete = async (user: User) => {
    setActiveMenuId(null);
    if (!window.confirm(`Permanently delete ${user.name}? This cannot be undone.`)) return;
    try {
      await usersApi.remove(user.userId);
      loadUsers();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to delete user.');
    }
  };

  // Parse a simple CSV (header row: name,email,password,role) into import rows
  const handleCsvFile = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (fileInputRef.current) fileInputRef.current.value = ''; // allow re-selecting same file
    if (!file) return;
    setImporting(true);
    try {
      const text = await file.text();
      const lines = text.split(/\r?\n/).map(l => l.trim()).filter(Boolean);
      if (lines.length < 2) { alert('CSV must have a header row and at least one data row.'); return; }
      const header = lines[0].toLowerCase().split(',').map(h => h.trim());
      const idx = {
        name: header.indexOf('name'),
        email: header.indexOf('email'),
        password: header.indexOf('password'),
        role: header.indexOf('role')
      };
      if (idx.name < 0 || idx.email < 0 || idx.password < 0 || idx.role < 0) {
        alert('CSV header must include: name, email, password, role');
        return;
      }
      const rows = lines.slice(1).map(line => {
        const cols = line.split(',').map(c => c.trim());
        const role = (cols[idx.role] || 'Student') as UserRole;
        return { name: cols[idx.name] || '', email: cols[idx.email] || '', password: cols[idx.password] || '', role };
      });
      const result: any = await usersApi.importUsers(rows);
      alert(`Import complete: ${result.created} created, ${result.skippedCount} skipped.`);
      loadUsers();
    } catch (err: any) {
      alert(err.response?.data?.message || 'Failed to import users.');
    } finally {
      setImporting(false);
    }
  };

  const handleResetBinding = async (userId: string, userName: string) => {
    setActiveMenuId(null);
    if (window.confirm(`Reset device hardware binding for ${userName}?`)) {
      try {
        await usersApi.resetDeviceBinding(userId);
        alert('Device binding successfully reset.');
        loadUsers();
      } catch (err) {
        console.error(err);
        alert('Failed to reset device binding.');
      }
    }
  };

  const handleForceLogout = async (userId: string) => {
    setActiveMenuId(null);
    try {
      await usersApi.forceLogout(userId);
      alert('Active sessions revoked.');
      loadUsers();
    } catch (err) {
      console.error(err);
      alert('Failed to revoke sessions.');
    }
  };

  // Filter logic
  const filteredUsers = users.filter((u) => {
    const matchesTab = activeTab === 'All' || u.role === activeTab;
    const matchesSearch = 
      u.name.toLowerCase().includes(search.toLowerCase()) ||
      u.email.toLowerCase().includes(search.toLowerCase());
    return matchesTab && matchesSearch;
  });

  const getRoleBadge = (role: UserRole) => {
    switch (role) {
      case 'Student':
        return <span className="px-2 py-1 rounded-full bg-blue-50 text-blue-700 text-[11px] font-bold uppercase tracking-wider border border-blue-100">Student</span>;
      case 'Teacher':
        return <span className="px-2 py-1 rounded-full bg-purple-50 text-purple-700 text-[11px] font-bold uppercase tracking-wider border border-purple-100">Teacher</span>;
      case 'Invigilator':
        return <span className="px-2 py-1 rounded-full bg-amber-50 text-amber-700 text-[11px] font-bold uppercase tracking-wider border border-amber-100">Invigilator</span>;
      default:
        return <span className="px-2 py-1 rounded-full bg-slate-50 text-slate-700 text-[11px] font-bold uppercase tracking-wider border border-slate-100">Admin</span>;
    }
  };

  return (
    <AppLayout title="User Management">
      {/* Viewport Content */}
      <div className="space-y-6">
        {/* Tabs Bar */}
        <div className="flex border-b border-slate-200">
          {(['All', 'Student', 'Teacher', 'Invigilator', 'Admin'] as const).map((tab) => (
            <button
              key={tab}
              role="tab"
              onClick={() => {
                setActiveTab(tab);
                setActiveMenuId(null);
              }}
              className={`px-6 py-3 text-sm font-semibold relative transition-colors ${
                activeTab === tab ? 'text-primary-500 font-bold border-b-2 border-primary-500' : 'text-slate-500 hover:text-slate-800'
              }`}
            >
              {tab === 'All' ? 'All Users' : tab + 's'}
            </button>
          ))}
        </div>

        {/* Action Bar */}
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
          <div className="relative max-w-md w-full">
            <svg className="w-5 h-5 absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
            </svg>
            <input
              className="w-full h-10 pl-10 pr-4 bg-white border border-slate-200 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-transparent outline-none transition-all"
              placeholder="Search by name or email..."
              type="text"
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                setActiveMenuId(null);
              }}
            />
          </div>
          <div className="flex items-center gap-3">
            <input
              ref={fileInputRef}
              type="file"
              accept=".csv,text/csv"
              className="hidden"
              onChange={handleCsvFile}
            />
            <button
              onClick={() => fileInputRef.current?.click()}
              disabled={importing}
              title="CSV columns: name, email, password, role"
              className="px-4 h-10 border border-primary-500 text-primary-500 font-semibold text-sm rounded-lg hover:bg-blue-50 transition-colors flex items-center gap-2 disabled:opacity-50"
            >
              {importing ? (
                <div className="w-4 h-4 border-2 border-primary-500 border-t-transparent rounded-full animate-spin" />
              ) : (
                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
                </svg>
              )}
              Upload CSV
            </button>
            <button
              onClick={openCreatePanel}
              className="px-4 h-10 bg-primary-500 text-white font-semibold text-sm rounded-lg hover:bg-primary-600 transition-colors shadow-sm flex items-center gap-2"
            >
              <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 4v16m8-8H4" />
              </svg>
              Add User
            </button>
          </div>
        </div>

        {/* Table Container */}
        {loading ? (
          <div className="min-h-[300px] flex items-center justify-center">
            <div className="w-8 h-8 border-4 border-primary-500 border-t-transparent rounded-full animate-spin" />
          </div>
        ) : (
          <div className="bg-white border border-slate-200 rounded-xl overflow-hidden shadow-sm">
            <div className="overflow-x-auto overflow-y-visible min-h-[300px]">
              <table className="w-full text-left border-collapse">
                <thead>
                  <tr className="bg-slate-50 border-b border-slate-200">
                    <th className="p-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Name</th>
                    <th className="p-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Email</th>
                    <th className="p-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Role</th>
                    <th className="p-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Status</th>
                    <th className="p-4 text-xs font-bold tracking-wider text-slate-400 uppercase text-center">Device Bound</th>
                    <th className="p-4 text-xs font-bold tracking-wider text-slate-400 uppercase">Created</th>
                    <th className="p-4 w-16"></th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {filteredUsers.length === 0 ? (
                    <tr>
                      <td colSpan={7} className="px-6 py-12 text-center text-sm text-slate-400">
                        No users match the active filters.
                      </td>
                    </tr>
                  ) : (
                    filteredUsers.map((user) => (
                      <tr key={user.userId} className="hover:bg-slate-50/50 transition-colors">
                        <td className="p-4">
                          <div className="flex items-center gap-3">
                            <div className="w-8 h-8 rounded-full bg-blue-100 text-primary-600 flex items-center justify-center font-bold text-xs">
                              {user.name.split(' ').map((n) => n[0]).join('')}
                            </div>
                            <span className="text-sm font-semibold text-slate-700">{user.name}</span>
                          </div>
                        </td>
                        <td className="p-4 text-sm text-slate-500">{user.email}</td>
                        <td className="p-4">{getRoleBadge(user.role)}</td>
                        <td className="p-4">
                          {user.isActive ? (
                            <span className="flex items-center gap-1.5 text-xs font-semibold text-green-700">
                              <span className="w-1.5 h-1.5 rounded-full bg-green-500"></span>
                              Active
                            </span>
                          ) : (
                            <span className="flex items-center gap-1.5 text-xs font-semibold text-slate-400">
                              <span className="w-1.5 h-1.5 rounded-full bg-slate-300"></span>
                              Inactive
                            </span>
                          )}
                        </td>
                        <td className="p-4 text-center">
                          {user.role === 'Student' && (
                            user.deviceBound ? (
                              <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded bg-blue-50 text-blue-700 text-xs font-semibold border border-blue-100">
                                <svg className="w-3.5 h-3.5 text-blue-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
                                </svg>
                                Bound
                              </span>
                            ) : (
                              <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded bg-slate-50 text-slate-400 text-xs font-medium border border-slate-100">
                                <svg className="w-3.5 h-3.5 text-slate-300" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M8 11V7a4 4 0 118 0m-4 8v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2z" />
                                </svg>
                                Unbound
                              </span>
                            )
                          )}
                        </td>
                        <td className="p-4 text-sm text-slate-500">
                          {new Date(user.createdAt).toLocaleDateString('en-US', {
                            month: 'short',
                            day: 'numeric',
                            year: 'numeric'
                          })}
                        </td>
                        <td className="p-4 relative">
                          <button
                            onClick={() => setActiveMenuId(activeMenuId === user.userId ? null : user.userId)}
                            className="p-1 rounded hover:bg-slate-100 text-slate-400 hover:text-slate-600 transition-colors"
                          >
                            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 5v.01M12 12v.01M12 19v.01M12 6a1 1 0 110-2 1 1 0 010 2zm0 7a1 1 0 110-2 1 1 0 010 2zm0 7a1 1 0 110-2 1 1 0 010 2z" />
                            </svg>
                          </button>

                          {activeMenuId === user.userId && (
                            <div
                              ref={menuRef}
                              className="absolute right-4 top-10 w-44 bg-white border border-slate-200 rounded-lg shadow-lg py-1 z-50 text-left"
                            >
                              {user.role === 'Student' && (
                                <>
                                  <button
                                    onClick={() => handleResetBinding(user.userId, user.name)}
                                    className="w-full text-left px-4 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-50 flex items-center gap-2"
                                  >
                                    Reset Binding
                                  </button>
                                  <button
                                    onClick={() => handleForceLogout(user.userId)}
                                    className="w-full text-left px-4 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-50 flex items-center gap-2"
                                  >
                                    Force Logout
                                  </button>
                                </>
                              )}
                              <button
                                onClick={() => openEditPanel(user)}
                                className="w-full text-left px-4 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-50 flex items-center gap-2"
                              >
                                Edit User
                              </button>
                              <button
                                onClick={() => handleToggleActive(user)}
                                className="w-full text-left px-4 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-50 flex items-center gap-2"
                              >
                                {user.isActive ? 'Deactivate' : 'Activate'}
                              </button>
                              <div className="my-1 border-t border-slate-100" />
                              <button
                                onClick={() => handleDelete(user)}
                                className="w-full text-left px-4 py-2 text-xs font-semibold text-red-600 hover:bg-red-50 flex items-center gap-2"
                              >
                                Delete User
                              </button>
                            </div>
                          )}
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

      {/* Slide-over Panel Overlay */}
      {showPanel && (
        <div
          className="fixed inset-0 bg-slate-900/40 backdrop-blur-sm z-[60] transition-opacity duration-300"
          onClick={() => setShowPanel(false)}
        />
      )}

      {/* Slide-over Panel */}
      <div
        className={`fixed top-0 right-0 h-screen w-full max-w-[480px] bg-white z-[70] shadow-2xl transition-transform duration-300 ease-in-out flex flex-col ${
          showPanel ? 'translate-x-0' : 'translate-x-full invisible'
        }`}
      >
        {/* Panel Header */}
        <div className="p-6 border-b border-slate-200 flex items-center justify-between">
          <h2 className="text-lg font-bold text-slate-800">{editingUser ? 'Edit User' : 'Add New User'}</h2>
          <button
            className="w-10 h-10 flex items-center justify-center rounded-full hover:bg-slate-100 text-slate-500 transition-colors"
            onClick={() => setShowPanel(false)}
          >
            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>
        {/* Panel Body */}
        <form onSubmit={handleSubmitUser} className="flex-1 flex flex-col h-full overflow-hidden">
          <div className="flex-1 overflow-y-auto p-6 space-y-6">
            {formError && (
              <div className="p-3 bg-red-50 border border-red-200 text-red-600 text-xs rounded-lg">
                {formError}
              </div>
            )}
            
            {/* Form Field */}
            <div className="space-y-1.5">
              <label htmlFor="fullName" className="text-xs font-semibold tracking-wider text-slate-400 uppercase">Full Name</label>
              <input
                id="fullName"
                className="w-full h-10 px-4 border border-slate-200 rounded-lg focus:ring-2 focus:ring-primary-500 focus:border-transparent outline-none transition-all text-sm"
                placeholder="e.g. John Doe"
                type="text"
                value={formName}
                onChange={(e) => setFormName(e.target.value)}
                required
              />
            </div>
            {/* Form Field */}
            <div className="space-y-1.5">
              <label htmlFor="email" className="text-xs font-semibold tracking-wider text-slate-400 uppercase">Email Address</label>
              <input
                id="email"
                className="w-full h-10 px-4 border border-slate-200 rounded-lg focus:ring-2 focus:ring-primary-500 focus:border-transparent outline-none transition-all text-sm"
                placeholder="john.doe@university.edu"
                type="email"
                value={formEmail}
                onChange={(e) => setFormEmail(e.target.value)}
                required
              />
            </div>
            {/* Form Field */}
            <div className="space-y-1.5">
              <label htmlFor="password" className="text-xs font-semibold tracking-wider text-slate-400 uppercase">
                {editingUser ? 'New Password (optional)' : 'Password'}
              </label>
              <input
                id="password"
                className="w-full h-10 px-4 border border-slate-200 rounded-lg focus:ring-2 focus:ring-primary-500 focus:border-transparent outline-none transition-all text-sm"
                placeholder={editingUser ? 'Leave blank to keep current password' : 'Password (min 8 chars)'}
                type="password"
                value={formPassword}
                onChange={(e) => setFormPassword(e.target.value)}
                required={!editingUser}
              />
            </div>
            {/* Form Field */}
            <div className="space-y-1.5">
              <label htmlFor="role" className="text-xs font-semibold tracking-wider text-slate-400 uppercase">Role</label>
              <div className="relative">
                <select
                  id="role"
                  className="w-full h-10 px-4 border border-slate-200 rounded-lg bg-transparent focus:ring-2 focus:ring-primary-500 focus:border-transparent outline-none transition-all cursor-pointer text-sm"
                  value={formRole}
                  onChange={(e) => setFormRole(e.target.value as UserRole)}
                >
                  <option value="Student">Student</option>
                  <option value="Teacher">Teacher</option>
                  <option value="Invigilator">Invigilator</option>
                  <option value="Admin">Administrator</option>
                </select>
              </div>
            </div>
            <div className="p-4 bg-slate-50 rounded-xl border border-slate-200">
              <div className="flex gap-3">
                <svg className="w-5 h-5 text-blue-500 flex-shrink-0 mt-0.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
                <div>
                  <h4 className="text-sm font-semibold text-slate-700">Auto-Binding</h4>
                  <p className="text-xs text-slate-400 mt-1 leading-relaxed">
                    New students will have their accounts automatically bound to their workstation HWID fingerprint upon first login on the WPF student application.
                  </p>
                </div>
              </div>
            </div>
          </div>
          {/* Panel Footer */}
          <div className="p-6 border-t border-slate-200 flex items-center justify-end gap-3 bg-slate-50">
            <button
              type="button"
              className="px-6 h-10 border border-slate-200 text-slate-500 font-semibold text-sm rounded-lg hover:bg-slate-100 transition-colors"
              onClick={() => setShowPanel(false)}
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={formLoading}
              className="px-6 h-10 bg-primary-500 text-white font-semibold text-sm rounded-lg hover:bg-primary-600 transition-colors shadow-sm disabled:opacity-50 flex items-center justify-center gap-2"
            >
              {formLoading ? (
                <div className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
              ) : (
                editingUser ? 'Save Changes' : 'Create User'
              )}
            </button>
          </div>
        </form>
      </div>
    </AppLayout>
  );
}
