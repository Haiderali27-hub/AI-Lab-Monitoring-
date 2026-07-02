import type { ReactNode } from 'react';
import StudentNavbar from './StudentNavbar';

interface StudentLayoutProps {
  children: ReactNode;
}

export default function StudentLayout({ children }: StudentLayoutProps) {
  return (
    <div className="min-h-screen bg-slate-50 flex flex-col font-sans">
      <StudentNavbar />
      <main className="flex-1 max-w-[1100px] w-full mx-auto px-6 py-8">
        {children}
      </main>
    </div>
  );
}
