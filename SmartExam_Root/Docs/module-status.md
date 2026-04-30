# Module Status (Initial Implementation)

## Module 1: Authentication and Device Binding

Implemented:
- institution bootstrap
- admin/teacher login
- student login with HWID submission
- first-login device binding and mismatch blocking
- session persistence and single active student session enforcement
- admin controls: reset device binding and force logout
- student batch CSV import

## Module 2: Exam Dashboard

Implemented:
- student current exam state endpoint
- student start exam endpoint
- desktop exam dashboard with timer and status
- admin live roster endpoint and web roster display

## Module 3: Live Monitoring Client Service

Implemented:
- desktop heartbeat sender
- foreground app signal and process list snapshot in heartbeat payload
- backend monitoring ingestion + persistence
- SignalR hub for live monitoring stream
- web panel polling view for live roster connectivity state

## Next Iteration Targets

- add teacher exam creation and scheduling UI
- add screenshot evidence upload pipeline
- add WebSocket listener in web panel (replace polling)
- add refresh-token rotation path in clients
- add integration tests for auth and device-binding edge cases
