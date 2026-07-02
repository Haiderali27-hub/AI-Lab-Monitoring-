# Admin_Web_Panel_v2_redesign — UI redesign workspace

This is a **duplicate** of `Admin_Web_Panel`, created as the sandbox for all further UI work.

- **`Admin_Web_Panel` = FROZEN / FINALIZED.** Do not change it. That is the approved v1 look.
- **`Admin_Web_Panel_v2_redesign` = WORKING COPY.** All new design experiments happen here.

## Run it (side-by-side with the frozen version)

```powershell
cd Admin_Web_Panel_v2_redesign
npm install          # first time only
npm run dev          # serves on http://localhost:5174
```

- Frozen v1 runs on **http://localhost:5173**
- This redesign runs on **http://localhost:5174**
- Both talk to the same backend API on **http://localhost:5050**, so you can compare them live.

Functionality, routes, API calls, and data are identical to v1 — only the visual/styling layer should change here.
