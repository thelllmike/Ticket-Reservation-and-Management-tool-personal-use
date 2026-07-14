# RailPlan — Train Ticket Reservation & Management Tool

A first-iteration prototype of a personal train reservation and management tool, built with
**ASP.NET Core MVC on .NET 8**. It runs offline with a single command, stores everything in memory,
and makes no network calls of any kind.

---

## Running it

```bash
cd "path/to/cwk 1"
dotnet run --project src/TrainReservation.Web
```

Then open **<http://localhost:5255>** (the port is also printed in the console on startup).

> **If `dotnet` is not found:** the .NET 8 SDK is installed under `~/.dotnet`, which is not on the
> default `PATH`. Either prefix the command with the full path, or add it to your shell:
>
> ```bash
> export PATH="$HOME/.dotnet:$PATH"
> ```
>
> A `global.json` pins the project to the .NET 8 SDK, so it will not accidentally build against a
> newer SDK that happens to be installed.

There is no database to create, no migration to run, no configuration and no secrets. The store is
seeded at startup and **resets every time the app restarts** — that is by design for this iteration.

---

## Architecture

Four projects, each depending only on the one beneath it. The dependency arrows never point
backwards, which is what makes the storage swappable later.

```
TrainReservation.Web        ASP.NET Core MVC — controllers, Razor views, view models
        ↓
TrainReservation.Services   business logic — booking rules, recurrence, reporting, prediction
        ↓
TrainReservation.Data       repositories behind interfaces + the in-memory store + seeder
        ↓
TrainReservation.Domain     entities, enums, fare rules, seat map
```

### Layers

| Layer | Holds | Key types |
| --- | --- | --- |
| **Domain** | Entities and enums, plus the two rules everything else has to agree on: how a seat is priced and what seats a train physically has. No dependencies at all. | `Booking`, `Seat`, `TrainService`, `Route`, `SpecialRequest`, `RecurrencePattern`, `PricingCalculator`, `SeatMap` |
| **Data** | The only layer that knows storage is in-memory. Repositories expose interfaces; the store behind them is a set of dictionaries. | `InMemoryDataStore`, `IBookingRepository`, `ITrainServiceRepository`, `ISpecialRequestRepository`, …, `DataSeeder` |
| **Services** | All business logic. Depends on the repository *interfaces*, never on the store. | `IBookingService`, `IScheduleService`, `ISpecialRequestService`, `IReportService`, `IPredictionService`, `RecurrenceExpander`, `QuestionParser` |
| **Web** | Controllers, Razor views, Bootstrap 5 UI. | `BookingsController`, `ScheduleController`, `WeeklyController`, `ChatController`, … |

### Why this is ready for a database later

Everything above the Data layer talks to `IBookingRepository`, `IRouteRepository` and friends.
Adding persistence means writing EF Core implementations of those same interfaces and changing the
registrations in `Program.cs` — **no service, controller or view changes.**

Dependency injection is wired in `src/TrainReservation.Web/Program.cs`. The store and repositories
are **singletons**, because they *are* the application's state and must outlive a request; a scoped
repository would hand every request an empty database. The services are stateless, so they are
scoped.

---

## Where things live

| What you're looking for | File |
| --- | --- |
| **Seed data** | `src/TrainReservation.Data/DataSeeder.cs` |
| **In-memory store** (dictionaries, locking, id generation) | `src/TrainReservation.Data/InMemoryDataStore.cs` |
| **Repository interfaces** | `src/TrainReservation.Data/Repositories/IRepositories.cs` |
| **Prediction logic** (pricing trend + availability) | `src/TrainReservation.Services/PredictionService.cs` |
| **Chatbot language parsing** | `src/TrainReservation.Services/QuestionParser.cs` |
| **Recurring-booking expansion** | `src/TrainReservation.Services/RecurrenceExpander.cs` |
| **Booking rules & validation** | `src/TrainReservation.Services/BookingService.cs` |
| **Weekly view / report assembly** | `src/TrainReservation.Services/ReportService.cs` |
| **DI registrations** | `src/TrainReservation.Web/Program.cs` |

---

## The five features

### 1. GUI
Bootstrap 5, served from `wwwroot/lib` rather than a CDN so the app genuinely works with no network.
Navbar across Bookings / Schedule / Special requests / Weekly view / Predictions, with list, detail,
create, edit and delete pages throughout, and validation feedback on every form.

### 2. CRUD — bookings, schedule, special requests
Full create / read / update / delete for all three.

The **add-booking flow** follows the brief: pick a route → travel date → an available service (only
services that actually run that weekday appear) → seats from a live seat map → optional special
requests → confirm, with the total updating as you select seats.

**Validation** is enforced server-side in `BookingService.Validate`, not just in the browser:
- travel dates in the past are rejected;
- the same seat cannot be sold twice on one service and date — including against seats held by a
  recurring series that falls on that date;
- a route and a service are required, and the service must actually run on the chosen day;
- a service that still has bookings against it cannot be deleted from the schedule.

Prices are **always recomputed on the server** from route distance and seat class. The posted total
is never trusted.

**Recurring bookings** are stored once, as a `Booking` plus a `RecurrencePattern` (daily / weekly /
monthly, with an interval and days of the week). Occurrences are **generated on demand** by
`RecurrenceExpander` — a commute repeating for a year is one record, not fifty-two.

When you want to change a single date, the weekly view offers *"edit this one"* and *"skip"*. That
**materialises** just that occurrence into a `Booking` of its own pointing back at the series
(`ParentBookingId`), and expansion then skips that date for the parent — so the one date changes and
the rest of the series is untouched.

### 3. In-memory storage
Every entity type lives in a `Dictionary<int, T>` keyed by id for O(1) lookup (stations are keyed by
their natural key, the station code). Ordering, filtering and grouping are all LINQ. Ids come from an
`Interlocked` counter per entity type, and the store is guarded by a lock because it is a singleton
being hit by concurrent requests.

Seats and special requests are owned by their booking *and* indexed by id — the index holds the same
object references as the booking's collections, so there is one source of truth, not two copies.

### 4. Weekly view & report
- **Weekly view** — a Monday–Sunday grid with the bookings and special requests on each day,
  recurring occurrences expanded in (shown in purple), and previous/next-week paging.
- **Weekly report** — the same week summarised per day: trips, routes and times, special requests and
  total spend, plus routes travelled and requests by type. Printer-friendly (`@media print` strips the
  navigation and chrome), with a Print button.

### 5. Prediction chatbot
Ask in plain English, e.g.:

> *"What will a London to Manchester ticket cost next Friday?"*
> *"Will there be seats on the 08:00 to Leeds next Monday?"*

`QuestionParser` pulls out the route (by city, station name or code), the date ("next Friday",
"tomorrow", "2026-08-14", "12 August") and optionally a departure time. If only one end of the
journey is named and exactly one route serves it, it is inferred; if the route is genuinely
ambiguous the bot **asks a clarifying question**, and dropdowns are provided to disambiguate.

`PredictionService` then computes, **entirely from the seeded booking history in memory** — no model,
no training, no external API:

- **Pricing trend** — a moving average of what standard seats on that route have actually sold for,
  split into a recent window (4 weeks) and an earlier one to get a trend direction (rising / stable /
  easing), projected forward to the target date and adjusted for how expensive that day of the week
  usually is.
- **Availability** — historical occupancy (seats sold ÷ capacity) for that route on that day of the
  week, classified as *Likely available* / *Limited* / *Likely full*.
- **Confidence** — high / medium / low based on how many comparable days actually back the answer. A
  route with no history says so plainly and falls back to a best-effort estimate off the published
  fare.

The reply is natural language, with the supporting figures shown underneath so the answer can be
checked rather than taken on trust.

---

## A note on the seed data

`DataSeeder` seeds two distinct bodies of data, and the distinction is deliberate:

1. **The personal user's bookings** (~20, including three recurring series) spread across recent past,
   the current week and the coming weeks. These are what the Bookings pages, the weekly view and the
   report show, and what you edit.

2. **A network booking history** — roughly twelve weeks of completed bookings across the six busiest
   routes, owned by other seeded passengers. Availability is predicted from *occupancy*, which is only
   meaningful if the history represents everyone on the train rather than one person's own trips. The
   chatbot analyses this whole body; the CRUD screens deliberately ignore it so your own booking list
   stays readable.

The generator uses a **fixed random seed**, so the same history — and therefore the same predictions —
is produced on every run. Demand is shaped so there are real patterns to find: Fridays and Mondays are
the commuter peaks, and fares drift up on most corridors while easing on the Birmingham route.

---

## Constraints honoured

- Runs offline with `dotnet run`. No database, no network calls, no secrets, no external APIs.
- Predictions use **only** local seeded data.
- All storage sits behind repository interfaces, so the data layer can be swapped for a persistent
  one without touching the layers above it.
