# Design Brief — SmartExam Student Desktop App (WPF)

## Your role
You are a senior product designer. Design a **beautiful, modern, distinctive UI** for a **Windows desktop application** (the student client of an exam-monitoring system). Apply real creative direction — but everything must be implementable in **WPF / XAML**, and you must keep every field, control, and action that exists today.

## What the app is
**SmartExam Student Client** — a secure Windows desktop app a university student runs on a lab PC to sit a proctored exam. It logs in, binds to the machine, shows the assigned exam + a countdown, and launches into a locked-down exam session while the server monitors activity. Mood: **secure, trustworthy, calm, premium, focused** — a serious exam/security tool, not a playful consumer app. Think "modern secure kiosk / exam console."

## Platform & technical constraints (must be implementable here)
- **WPF, .NET 9, C#/XAML.** No web tech. The design must be expressible with WPF primitives:
  - Layout: `Grid` / `StackPanel`; cards = `Border` with `CornerRadius`, `Background`, `BorderBrush`.
  - Color: `SolidColorBrush` and `LinearGradientBrush` (gradients are fine).
  - Depth: `DropShadowEffect` (soft shadows, glows). Blur is possible but expensive — use sparingly.
  - Styling: define reusable `Style` + `ControlTemplate` resources in `App.xaml` for `TextBox`, `PasswordBox`, and `Button` (WPF default controls look dated — they MUST be re-templated: rounded corners, padding, hover/focus states).
  - Icons: **Segoe MDL2 Assets** / **Segoe Fluent Icons** glyph font (built into Windows) or vector `Path` data. No external icon libraries.
  - Fonts: **Segoe UI / Segoe UI Variable** (safe, native, modern). Do not assume Inter is installed.
- **Custom window chrome:** the window is borderless (`WindowStyle=None`, `AllowsTransparency=True`) with a rounded outer `Border`. So you must design a **custom title bar** that includes a **Minimize** and **Close** button and a draggable area. (This is a hard requirement — the current version was criticized for missing window controls.)
- Two window sizes: **Login ≈ 460×620**, **Exam Dashboard ≈ 1120×700**. Target 1080p lab monitors. Not resizable is acceptable.
- Accessibility: strong contrast (WCAG AA), clear focus states on inputs, legible sizes.

## The two screens and the EXACT content each must contain (keep all of it)

### Screen 1 — Login
- Custom title bar with app label + **minimize** + **close** buttons (draggable).
- Brand block (logo mark + "SmartExam" + short tagline).
- **Email / username** text field.
- **Password** field (masked).
- A small **device-binding note** (e.g. "This device will be securely bound to your account").
- **Primary "Sign In" button.**
- A **connection-status line** (e.g. "Connected to server" / "Server unreachable").
- A **status/error message line** (e.g. "Invalid username or password", "This device is bound to another account").

### Screen 2 — Exam Dashboard (shown after login)
- Custom title bar with brand + **minimize** + **close** (draggable).
- Header: screen title ("Exam Dashboard"), the **student's name**, and two meta chips: **Lab** and **Proctor** name.
- A prominent **"Time Remaining" countdown** (large, glanceable).
- **Exam name / title.**
- Two status tiles: **Exam Status** (e.g. Not Started / In Progress) and **Monitoring Status** (e.g. "Heartbeat OK").
- An **Instructions** panel (multi-line text).
- A **Session Status** message (e.g. "You can proceed", "Exam has not started yet").
- Three action buttons: **Refresh**, **Start Exam** (primary), **Logout** (destructive).

> Do not add features or remove any of the above data points — this is a **visual redesign** of the same functionality.

## What I want from you (deliverables)
1. **Creative direction** (3-5 sentences): the concept, mood, and why it suits a secure exam client.
2. **A design system**, given concretely so it maps to WPF:
   - **Color palette** with semantic roles (primary, primary-dark, success, warning, danger, ink/text, muted, border, surface, field/background) as hex values — plus any gradients (start/end stops).
   - **Typography**: font (Segoe UI family), sizes, weights, and letter-spacing for headings/labels/body.
   - **Spacing, corner-radius, and shadow/elevation** scales.
   - **Component specs** (as WPF-style guidance): text input (rounded, focus ring), password box, primary/secondary/danger buttons (with hover + disabled states), title-bar buttons (minimize/close, close hovers red), cards, status tiles, chips/badges.
3. **High-fidelity mockups of BOTH screens** as a **self-contained HTML + CSS artifact** I can preview (use solid colors, gradients, rounded corners, soft shadows, and Segoe UI so it visually matches what WPF can render — avoid web-only effects like backdrop blur, CSS grid tricks, or SVG filters that don't translate to WPF).
4. **A short "WPF implementation map"**: for each component, note the WPF equivalent (e.g. "input = `TextBox` with a custom `ControlTemplate`: rounded `Border`, `PART_ContentHost`, focus trigger sets border to primary"). This makes it drop-in for the developer.

## Guardrails
- Keep every field, control, action, and piece of data listed above.
- The **Minimize and Close buttons are mandatory** and must be clearly visible in the title bar.
- Make security/status signals unmistakable (connection lost, monitoring active, time running out).
- Prefer clean legibility over decoration; this is a high-stakes exam tool.
- Everything must be renderable in WPF/XAML with the primitives listed — no dependencies.

Start with the creative direction + design system, then give me the HTML artifact showing both screens, then the WPF implementation map.
