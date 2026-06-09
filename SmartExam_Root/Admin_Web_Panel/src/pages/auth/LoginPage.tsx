import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { authApi } from '../../api/auth.api';

export default function LoginPage() {
  const { login } = useAuth();
  const [email, setEmail] = useState('admin@smartexam.com');
  const [password, setPassword] = useState('Admin@123');
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const navigate = useNavigate();

  const togglePassword = () => {
    setShowPassword(!showPassword);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const data = await authApi.login({ email, password });
      login(data.token, {
        userId: data.userId,
        name: data.name,
        email: data.email,
        role: data.role,
      });
      // Redirect based on role
      if (data.role === 'Teacher') {
        navigate('/teacher/dashboard');
      } else if (data.role === 'Student') {
        navigate('/student/dashboard');
      } else {
        navigate('/admin/dashboard');
      }
    } catch (err: any) {
      console.error(err);
      setError(err.response?.data?.message || 'Invalid email or password');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="light text-[#131b2e] font-sans">
      <main className="grid grid-cols-1 md:grid-cols-[45%_55%] min-h-screen">
        {/* Left Side: Brand Identity */}
        <section className="bg-[#2563eb] [background-image:radial-gradient(at_0%_0%,_hsla(220,100%,40%,1)_0,_transparent_50%),_radial-gradient(at_100%_100%,_hsla(220,100%,60%,1)_0,_transparent_50%)] flex flex-col items-center justify-center p-8 text-white text-center relative overflow-hidden">
          <div className="flex flex-col items-center z-10">
            {/* Shield Icon */}
            <div className="mb-6 flex items-center justify-center w-[120px] h-[120px] rounded-full bg-white/10 backdrop-blur-sm">
              <svg className="w-16 h-16 text-white" fill="currentColor" viewBox="0 0 20 20">
                <path fillRule="evenodd" d="M10 1.944A11.954 11.954 0 012.166 5C2.056 5.649 2 6.319 2 7c0 5.224 3.08 9.711 7.5 11.77a11.956 11.956 0 007.5-11.77c0-.681-.056-1.351-.166-2A11.954 11.954 0 0110 1.944z" clipRule="evenodd" />
              </svg>
            </div>
            <h1 className="text-3xl font-bold mb-2">SmartExam</h1>
            <p className="text-lg text-white/80 max-w-[320px]">
              AI-Driven Lab Exam Monitoring System
            </p>
          </div>
          {/* Decorative Rings */}
          <div className="mt-12 opacity-20 pointer-events-none select-none">
            <div className="w-64 h-64 border-2 border-white rounded-xl rotate-12 absolute -z-10 blur-sm"></div>
            <div className="w-64 h-64 border-2 border-white rounded-xl -rotate-12 absolute -z-10 blur-[1px]"></div>
          </div>
        </section>

        {/* Right Side: Login Form */}
        <section className="bg-white flex flex-col items-center justify-center p-8 relative">
          <div className="w-full max-w-[400px]">
            <div className="mb-10">
              <span className="text-xs font-semibold tracking-wider text-slate-400 block mb-1">WELCOME BACK</span>
              <h2 className="text-2xl font-bold text-slate-800">Sign in to your account</h2>
            </div>
            
            <form className="space-y-6" onSubmit={handleSubmit}>
              {/* Email Field */}
              <div className="space-y-2">
                <label className="text-xs font-semibold tracking-wider text-slate-500 block" htmlFor="email">EMAIL ADDRESS</label>
                <div className="relative">
                  <input 
                    className="w-full h-[40px] px-4 rounded-lg border border-slate-200 bg-white text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-transparent transition-all" 
                    id="email" 
                    type="email" 
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="name@university.edu"
                    required
                  />
                </div>
              </div>
              
              {/* Password Field */}
              <div className="space-y-2">
                <label className="text-xs font-semibold tracking-wider text-slate-500 block" htmlFor="password">PASSWORD</label>
                <div className="relative group">
                  <input 
                    className="w-full h-[40px] px-4 rounded-lg border border-slate-200 bg-white text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-transparent transition-all" 
                    id="password" 
                    type={showPassword ? "text" : "password"} 
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    placeholder="Enter your password"
                    required
                  />
                  <button 
                    className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-400 hover:text-primary-500 transition-colors" 
                    onClick={togglePassword} 
                    type="button"
                  >
                    {showPassword ? (
                      <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 011.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.88 9.88l-3.29-3.29m7.532 7.532l3.29 3.29M3 3l3.59 3.59m0 0A9.953 9.953 0 0112 5c4.478 0 8.268 2.943 9.543 7a10.025 10.025 0 01-4.132 5.411m0 0L21 21" />
                      </svg>
                    ) : (
                      <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                      </svg>
                    )}
                  </button>
                </div>
              </div>

              {/* Error Message */}
              {error && (
                <div className="p-3 bg-red-50 border border-red-200 text-red-600 text-xs rounded-lg flex items-center gap-2">
                  <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
                    <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
                  </svg>
                  <span>{error}</span>
                </div>
              )}
              
              {/* Submit Button */}
              <button 
                className="w-full h-[44px] bg-[#2563eb] text-white font-semibold text-sm rounded-lg hover:bg-blue-600 transition-all active:scale-[0.98] shadow-sm disabled:opacity-50 flex items-center justify-center gap-2" 
                type="submit"
                disabled={loading}
              >
                {loading ? (
                  <div className="w-5 h-5 border-2 border-white border-t-transparent rounded-full animate-spin" />
                ) : (
                  "Sign In"
                )}
              </button>
            </form>
            
            {/* Status Feedback */}
            <div className="mt-8 p-4 bg-slate-50 rounded-lg border border-slate-200 flex items-start gap-3">
              <svg className="w-5 h-5 text-blue-500 mt-0.5 flex-shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
              <p className="text-xs text-slate-500 leading-relaxed">
                Authorized personnel only. All access attempts are logged and monitored.
              </p>
            </div>
          </div>
          {/* Footer */}
          <footer className="absolute bottom-8 w-full text-center px-8">
            <p className="text-xs text-slate-400">
              SmartExam v1.0 — University Internal System
            </p>
          </footer>
        </section>
      </main>
    </div>
  );
}
