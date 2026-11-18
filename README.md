# SmartStudyPlanner

A study planner that **builds your schedule for you**. Add your deadlines and how much time you
have; SmartStudy spreads the work across your week, warns you before you fall behind, and
re-plans the moment anything changes.

Built with Blazor WebAssembly (.NET 10): the whole app runs in the browser and publishes as
static files. Data is stored privately in the browser's `localStorage`, so there's no account,
no database, no server and nothing to configure.

## Features

| | |
|---|---|
| **Automatic scheduling** | Distributes each task's remaining work over your available time before its deadline. Work that can't be postponed without missing a deadline is scheduled first (reverse earliest-deadline-first), and the rest is spread by urgency, priority and grade weight. Exams are spaced out (distributed practice) and tasks are interleaved. |
| **Deadline forecast** | Every task is marked *on track*, *tight*, *short by Xh* or *overdue*, so you see problems days in advance. |
| **Learns from you** | Compares your estimates with the time you actually logged and corrects future plans (e.g. "tasks take you 120% of your estimate"). |
| **Natural-language quick add** | `Lab report #bio due fri 4h !high`, `Calc midterm dec 12 8h`, `Read ch 4 tomorrow`. |
| **Focus timer** | Pomodoro-style timer that keeps running across pages and reloads, logs time against the task, chimes and can send desktop notifications. |
| **Spaced-repetition flashcards** | SM-2 style scheduling with Again/Hard/Good/Easy, keyboard shortcuts, bulk import, and **exam-aware intervals** so every card comes back before the exam. Review time is reserved in the daily plan. |
| **Grade tracking** | Give tasks a weight and a score to see your current grade, projected grade, and the average you need on the rest of the term to hit your target. |
| **Planner** | Week view with planned blocks and logged sessions, per-day availability overrides ("I'm busy Thursday"), a 4-week workload chart, and `.ics` export for Google/Apple/Outlook calendars. |
| **Insights** | Streaks, a 20-week consistency heatmap, time by subject vs. weekly goals, estimate accuracy, and personalised suggestions. |
| **Everything else** | Subjects with colours and weekly goals, subtasks, light/dark/system theme, mobile layout, JSON backup/restore, and a sample semester to explore. |

## Running locally

```bash
dotnet run --launch-profile http
```

Then open http://localhost:5069 and choose **Explore with a sample semester**.

To produce the static site:

```bash
dotnet publish SmartStudyPlanner.csproj -c Release -o publish
```

Serve `publish/wwwroot` from any static host, falling back to `index.html` for unknown paths.

## Tests

```bash
dotnet test tests/SmartStudyPlanner.Tests
```

The tests cover the scheduler (capacity, feasibility, starvation, spacing, calibration), the
quick-add parser, spaced repetition, grade maths, streaks, backups and the calendar export.

## Project layout

```
Models/            Data model (subjects, tasks, logs, flashcards, settings)
Services/
  Scheduler.cs       The planning algorithm
  PlannerStore.cs    Per-session state, persisted to localStorage via JS interop
  QuickAddParser.cs  Natural-language task entry
  SpacedRepetition.cs, Analytics.cs, IcsExporter.cs, SampleData.cs
Components/Pages/  Today, Tasks, Planner, Focus, Review, Subjects, Insights, Settings
Components/Shared/ Reusable UI (task editor, modal, progress ring, timer host, …)
wwwroot/js/ssp.js  Small browser helpers (storage, downloads, sound, notifications)
tests/             xUnit tests
```

## Deployment

**Vercel**: import the repository. No settings are needed, because `vercel.json` handles them:
- `scripts/vercel-install.sh` installs the .NET 10 SDK, which Vercel's build image doesn't include.
- `scripts/vercel-build.sh` publishes the static site to `publish/wwwroot`.
- Every route falls back to `index.html`, so deep links like `/planner` work.

**Azure App Service**: `.github/workflows/master_smartstudyplanner.yml` builds, tests and deploys
on every push to `master`. The publish output includes a `web.config` for IIS.
