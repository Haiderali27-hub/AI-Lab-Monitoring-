/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{js,ts,jsx,tsx}"],
  theme: {
    extend: {
      colors: {
        primary: {
          50:  '#eff5ff',
          100: '#dbe9fe',
          200: '#bfd8fe',
          300: '#93bdfd',
          400: '#6098fa',
          500: '#2f74f0',
          600: '#2563eb',
          700: '#1d4ed8',
          800: '#1e40af',
          900: '#1e3a8a',
        },
      },
      fontFamily: {
        sans: ['Inter', 'system-ui', '-apple-system', 'Segoe UI', 'sans-serif'],
      },
      borderRadius: {
        xl: '0.85rem',
        '2xl': '1.1rem',
      },
      boxShadow: {
        // Softer, layered, slate-tinted shadows for a calmer, more premium depth
        xs:  '0 1px 2px 0 rgb(15 23 42 / 0.05)',
        sm:  '0 1px 2px -1px rgb(15 23 42 / 0.08), 0 1px 3px 0 rgb(15 23 42 / 0.05)',
        DEFAULT: '0 2px 4px -2px rgb(15 23 42 / 0.06), 0 4px 14px -3px rgb(15 23 42 / 0.08)',
        md:  '0 4px 8px -3px rgb(15 23 42 / 0.07), 0 12px 26px -8px rgb(15 23 42 / 0.10)',
        lg:  '0 8px 16px -6px rgb(15 23 42 / 0.09), 0 22px 44px -14px rgb(15 23 42 / 0.14)',
        xl:  '0 28px 52px -14px rgb(15 23 42 / 0.18)',
      },
    },
  },
  plugins: [],
}
