# Design Brief — SmartExam Web Panel Redesign

## Your role
You are a senior product designer. I want you to **reimagine the UI** of my existing web app with real creative direction — a distinctive, modern, cohesive visual language — while keeping it appropriate for a serious, trustworthy exam-security product. Be bold with the *look*, conservative with the *behavior*: don't remove features, move data around, or change what each screen does.

## What the product is
**SmartExam** — an AI-driven lab-based examination monitoring and management system used by a university. It runs secure, proctored exams in a physical computer lab and gives staff live oversight, analytics, and communication tools. It should feel **secure, academic, precise, calm, and premium** — think "enterprise security console meets modern SaaS dashboard," not a playful consumer app.

## Who uses it (3 web workspaces + 1 desktop app)
- **Admin / SuperAdmin** — runs the institution: user management, labs & workstations, device bindings, system analytics, audit logs, notifications.
- **Teacher** — creates and runs exams, watches students live, reviews results, sends announcements.
- **Student** — a lightweight portal: sees assigned exams, takes them (via a separate secure desktop app), reviews graded results and performance.
- (There is also a Windows desktop client for students — **out of scope for this design; web only**.)

## The 6 functional modules (and the pages behind them)
1. **Auth & HWID Device Binding** — login screen (role-based redirect), device-binding management, force-logout.
2. **Exam Creation & Setup** — a **4-step wizard** (schedule → questions/test-cases → rules → assign students), plus an eligibility manager.
3. **Live Proctoring & Heartbeats** — a **real-time grid of active students** with status tiles (Normal / Warning / Suspicious / Violation), a per-student focus timeline, and teacher actions (send warning, force-submit). Data streams in live over WebSockets.
4. **Reporting & Analytics** — system-wide dashboards (counts, trends) and per-exam metrics (score distributions, per-question success rates, rosters), plus downloadable PDF proctor reports.
5. **Student Performance Portal** — student dashboard (exam roster, countdown, "start exam", secure-mode banner), detailed result page (their answers + AI grading justification + confidence), and a performance-trend line chart.
6. **Notification & Communication** — a notification bell with unread badge + dropdown feed, a notifications page, and a teacher "announcement broadcast" composer.

### Full page list to cover
- **Auth:** Login.
- **Admin:** Dashboard, Users (table + add-user), Labs & Workstations, Device Bindings, System Analytics, Notifications, Audit Logs.
- **Teacher:** Dashboard, Create Exam (4-step wizard), Live Monitor, Exam Analytics, Results, Eligibility, Reports, Announce, Notifications.
- **Student:** Dashboard, My Exams, Exam Result, Performance, Violations, Notifications.

## Tech constraints (important — the design must be implementable here)
- **Stack:** React 19 + TypeScript, Vite, **Tailwind CSS v3.4** (utility classes), `lucide-react` + inline SVG icons, `@microsoft/signalr` for realtime, `axios`, `react-router` v7.
- Styling is **Tailwind utility classes** in `.tsx` files, a central `tailwind.config.js` (design tokens: colors, fontFamily, borderRadius, boxShadow), and a small `index.css` base layer. Charts are **hand-built SVG** (no chart library), so please include a chart/data-viz style guide.
- **Do not** introduce heavy UI libraries or external runtime assets that break offline. Fonts via Google Fonts `@import` are fine; anything else should be inlineable.
- Must stay **responsive** (desktop-first for admin/teacher; the student portal should also be comfortable on a laptop). 
- Must meet **WCAG AA** contrast, have clear focus-visible states, and keep semantic markup.
- **Light theme is the baseline.** A well-considered **dark mode** is a welcome bonus, not required.

## Current design system (my finalized v1 — your *starting point to surpass*, not a cage)
- Light theme; **Inter** font; **blue** primary (~`#2563EB` family); soft slate neutrals.
- Layout: a fixed **240px sidebar + top navbar** for admin/teacher; a **top-nav** layout for students.
- **White cards**, `rounded-xl`, soft layered shadows; rounded "pill" active nav; gradient brand badge and avatars.
- It's clean and professional but a bit generic. I want you to **give it a real identity** — you may change the palette, typography, layout rhythm, component styling, iconography feel, motion, and overall art direction. Keep the blue-ish "trust/security" mood *or* propose a stronger alternative and justify it. (I'll attach screenshots of v1 for reference.)

## What I want from you (deliverables)
1. **A creative direction statement** — 3-5 sentences on the concept, mood, and why it fits an exam-security product.
2. **A complete design system:**
   - **Color palette** with semantic tokens: `primary`, `success`, `warning`, `danger`, `info`, plus a neutral scale — given as a ready-to-paste `tailwind.config.js` `theme.extend`.
   - **Typography scale** (families, sizes, weights, line-heights, tracking) and an `index.css` base layer.
   - **Spacing, radius, shadow, and elevation** scales.
   - An **accessible categorical + sequential chart palette** for the SVG analytics (with contrast notes).
3. **A component library spec** (with example Tailwind class strings): buttons (primary / secondary / ghost / danger), form inputs & the 4-step **wizard stepper**, cards, **KPI/stat tiles**, **data tables**, **status badges/pills** (Normal / Warning / Suspicious / Violation), tabs, modals, toasts, **empty states**, and **loading/skeleton states**.
4. **Three hero screens designed in high fidelity** to prove the direction — please build them as a **self-contained HTML + Tailwind (CDN) artifact** I can preview:
   - Admin **System Analytics** dashboard (KPIs + charts),
   - Teacher **Live Proctoring** grid (status tiles, one expanded student, real-time feel),
   - Student **Exam Dashboard** (countdown, secure-mode banner, "start exam").
5. **Rollout notes** — how to apply the tokens/components to the remaining pages, and anything that needs care (dense tables, the live grid, the wizard).

## Guardrails
- Keep every route, field, action, and piece of data that exists today — this is a **visual/layout** redesign, not a product redesign.
- Prioritize **legibility of dense data** (tables, live grids, analytics) over decoration.
- Make status/security signals **unmistakable** (a "Violation" tile must read instantly).
- Show, don't just tell: give concrete tokens and class strings I can drop into a Tailwind/React codebase.

Please start with the creative direction + design system, then the HTML artifact of the three hero screens.
