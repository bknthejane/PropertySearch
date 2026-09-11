# PropertySearch

A paginated property search over 250,000 rows in ASP.NET Core MVC, built to
**measure rather than guess**.

Every optimisation in this repo was made after profiling the query it fixes,
and each one records the execution plan before and after, the timing, and what
the plan actually revealed.

---

## Why this exists

I spent several weeks building a layered multi-tenant API without a framework,
which taught me a lot about architecture and correctness — and nothing about
performance, because every query in it ran against a handful of rows.

This repo is the other half. One narrow question, answered properly:

> Given a quarter of a million property listings and a search form with six
> filters, where does the time go, and what actually fixes it?

The interesting part is not the indexes. It's the discipline of capturing the
slow version first, so there is something to compare against and a reason to
believe the fix worked.

---

## Status

Early. Building in order:

- [x] MVC project scaffolded with EF Core and SQL logging
- [ ] Property domain model and migration
- [ ] 250,000 rows seeded via bulk insert
- [ ] Search view model, filters and Razor form
- [ ] Pagination with a bounded page and capped page size
- [ ] **Baseline measurements, before any optimisation**
- [ ] Indexing, with column order measured both ways
- [ ] Caching, measured

---

## Findings

The summary. Each row is a query, its timing before and after, and the plan
operator that explained it.

| Query | Before | After | What the plan showed |
|---|---|---|---|
| Unfiltered first page | — | — | — |
| Suburb filter | — | — | — |
| Suburb + price range | — | — | — |
| Sorted by price | — | — | — |
| Deep page (page 500) | — | — | — |

Timings are the median of three runs, since the first run pays for query
compilation. Logical reads are captured alongside elapsed time — elapsed time
alone is noisy, logical reads are not.

---

## Stack

ASP.NET Core MVC · Razor views · Entity Framework Core · SQL Server · Bootstrap

Server-rendered throughout, no separate frontend. That's deliberate: a full
request per interaction makes the request lifecycle visible, which is the point
of using MVC here rather than an API with a JavaScript client.

---

## Domain model

```
Province
└── Suburb
    └── Property   (type, listing type, price, bedrooms, bathrooms,
                    garages, floor area, erf size, listed date)
```

Three entities and no more. Suburbs and provinces are entities rather than free
text for two reasons: a free-text suburb filter would be a `LIKE`, which
behaves differently under indexing and muddies the comparison, and an integer
`SuburbId` is the clean case for demonstrating composite index column order.

---

## Deliberate differences from my other project

Both are ASP.NET Core and EF Core, but the goals are opposite and so are some
of the decisions. Worth naming, because they look like inconsistencies and
aren't.

**`int` keys, not `Guid`.** No multi-tenancy here, so no concern about ID
guessing across tenants. And `int` is four bytes against sixteen — every
nonclustered index carries the clustering key, so a wider key inflates every
index on the table. On 250,000 rows with several indexes that is measurable.

**No base entity, no audit fields, no soft delete.** Every abstraction between
the code and the SQL makes a measurement harder to read.

**Inline model configuration, not one file per entity.** Three entities, and
keeping the configuration in one place makes "what is indexed" answerable at a
glance — which matters when the whole repo is about what is indexed.

---

## Running locally

**Prerequisites:** .NET 10 SDK · SQL Server or LocalDB

```bash
git clone https://github.com/bknthejane/PropertySearch.git
cd PropertySearch
dotnet restore
```

Set your connection string in
`PropertySearch.Web/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=PropertySearch;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```

Then:

```bash
dotnet ef database update --project PropertySearch.Web
dotnet run --project PropertySearch.Web
```

Generated SQL is logged to the console in Development, with real parameter
values rather than placeholders — `EnableSensitiveDataLogging` is on behind an
environment guard, because reading the actual SQL is the point of this project
and `@p0` tells you nothing.

---

## Reproducing the measurements

The seeding is deterministic, so the row distribution is the same on any
machine and timings are comparable.

```sql
SET STATISTICS IO ON;
SET STATISTICS TIME ON;
```

Then run the query from the logged output directly in SSMS with **Include
Actual Execution Plan** enabled. Logical reads matter more than elapsed time —
elapsed time varies with cache state and machine load, logical reads do not.

---

## Licence

MIT. See [LICENSE](LICENSE).