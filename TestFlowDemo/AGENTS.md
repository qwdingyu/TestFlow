# Repository Guidelines

## Project Structure & Module Organization
- `Engine/`: Core orchestration and execution logic.
- `Workflow/`: Workflow types and runtime helpers.
- `Flows/`: Example or reusable flow definitions.
- `Devices/` + `Devices.json`: Device adapters and their configuration.
- `Models/`: Shared DTOs and domain models.
- `Storage/`: Local persistence (e.g., cached state, logs, or artifacts).
- `MainForm.cs` and `Program.cs`: WinForms entry/UI; `docs/` for ancillary notes.

## Build, Test, and Development Commands
- `dotnet restore` — Restore NuGet packages.
- `dotnet build -c Debug` — Build the solution.
- `dotnet run` — Launch the WinForms app from the project directory.
- Optional: `dotnet build -c Release` for production artifacts.

Run commands from the repo root (or the `.csproj` folder for `dotnet run`).

## Coding Style & Naming Conventions
- C# with 4‑space indentation; UTF‑8 encoding.
- Use `PascalCase` for types/methods; `camelCase` for locals/parameters.
- Keep methods focused; prefer small, testable units in `Engine/` and `Workflow/`.
- Match existing patterns in `MainForm.cs`, `ConfigManager.cs`, and neighbors.
- Optional: run `dotnet format` if available to standardize spacing and usings.

## Testing Guidelines
- This repo currently has no dedicated test project.
- Add targeted checks in `TestRunner.cs` for lightweight verification of flows and devices.
- For broader coverage, add an xUnit project `TestFlowDemo.Tests/` and exercise `Engine/` and `Workflow/` units. Aim to cover critical branches (success/error paths and device timeouts).
- Prefer deterministic tests with fake devices over hardware‑dependent runs.

## Commit & Pull Request Guidelines
- Messages: concise imperative subject, e.g., `Add retry logic to Engine step runner`.
- Include context: why the change, notable trade‑offs, and risk areas.
- PRs: link issues, list test strategy, attach screenshots for UI changes, and include config notes if `Devices.json` or `Storage/` behavior changes.

## Security & Configuration Tips
- Do not commit real device credentials or environment‑specific configs; provide sanitized samples for `Devices.json`.
- Keep device I/O behind adapters in `Devices/`. Validate inputs at boundaries and log failures to `Storage/` without sensitive data.
