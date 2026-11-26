# Emergency Services – Test Suite

One command runs every test for the whole backend:

```
dotnet test tests/MyApplication.Api.Tests
```

Run it from the top folder of the repo. **No Docker, SQL Server or tokens needed** – the tests start
the API in memory with an in-memory SQL database (SQLite), a fake login, and fakes for other teams' services.

## What's in here

```
tests/MyApplication.Api.Tests/
  Unit/          Tests one class at a time (rules inside a service or model)
  Integration/   Starts the real API and calls endpoints over HTTP, as a real user would
    EmergencyApiFactory.cs        Starts the API for tests (SQLite DB, fake login, fakes for other services)
    SqliteTestDbContext.cs        The team's real database model, tweaked only so it runs on SQLite
    TestAuthHandler.cs            Fake login – pick a role (or a service account), no JWT needed
    TestData.cs                   Shortcuts: create stations/units/incidents, check responses
    EndpointSecurityTests.cs      WHOLE-BACKEND checks (see below)
    IncidentsApiTests.cs          Incidents: create, roles, status rules, full lifecycle, UTC dates
    DispatchApiTests.cs           Assigning/releasing units, suggested units
    UnitsAndStationsApiTests.cs   Units and stations reference data
    HospitalsApiTests.cs          Hospitals list/detail/summary
    StatsAndExportApiTests.cs     Dashboard stats and CSV export
    ExternalServicesApiTests.cs   Budget, HR and Maintenance road closures (through the fake gateway)
    WeatherWarningsApiTests.cs    Weather & public warnings
  Fakes/
    FakeGateway.cs                Pretend Budget/HR/Maintenance – each test sets what they reply
    FakeTransitClient.cs          Pretend Public Transport
    FakeServiceTokenProvider.cs   Pretend service-account token
```

### Testing code that calls another team's service

Budget, HR and Maintenance calls go to `FakeGateway` instead of the network. Tell it what to reply,
call our endpoint, then check what we sent:

```csharp
_factory.Gateway.Respond(HttpMethod.Get, "/api/budget/v1/allocations/emergency",
    HttpStatusCode.OK, new { allocated = 500000 });

var response = await _viewer.GetAsync("/api/emergency/v1/budget");

var sent = _factory.Gateway.Requests.Last();   // path, auth header and body we sent
```

Anything not set up replies **503**, like a service that's down – so you can test failure handling too.
If you add a new client for another service, point it at the gateway in `EmergencyApiFactory` the same
way `BudgetFinanceClient` is.

### Setting up data

Use the shortcuts in `TestData.cs` – they go through the real API:

```csharp
var station  = await _admin.CreateStationAsync();
var unit     = await _admin.CreateUnitAsync(station.Id, "Ambulance");
var incident = await _dispatcher.CreateIncidentAsync(type: "Medical");
await _dispatcher.ResolveAsync(incident.Id, unit.Id);     // assign -> EnRoute -> OnScene -> Resolved
```

For data the API has no endpoint for yet (e.g. hospitals), put it straight in the database:
`await _factory.SeedAsync(db => db.Hospitals.Add(new Hospital { ... }));`

`response.ExpectAsync<T>(HttpStatusCode.OK)` checks the status code and shows the API's actual reply
in the failure message if it's wrong; `ExpectErrorAsync` also checks the `{ error, message, traceId }` shape.

### What SQLite can't test

SQLite runs one request at a time, so a true "two dispatchers at the same instant" race (ES-NFR-006)
can't be reproduced here. The tests check the rule (a busy unit can't be assigned again, and a failed
multi-unit assignment rolls back); the race itself is protected by SQL Server's filtered unique index.

### Whole-backend checks (cover everyone's code automatically)

`EndpointSecurityTests` finds **every controller endpoint** in the API by itself, so new endpoints are
tested the moment they're added – no extra work:

| Test | Checks | Requirement |
|---|---|---|
| TC-API-ALL-01 | Every endpoint has `[Authorize]` (or a deliberate `[AllowAnonymous]`) | SH-S03 |
| TC-API-ALL-02 | Every protected endpoint returns 401 when not logged in | SH-S03 |
| TC-API-ALL-03 | Every list endpoint (GET, no `{id}`) works for Admin – no 500 crash | ES-NFR-003 |
| TC-API-ALL-04 | Every route starts with `/api/emergency/v1/` | SH-I01 |
| TC-API-ALL-05 | API docs are published at `/openapi/v1.json` | SH-I02 |
| TC-API-ALL-06 | Unknown routes return 404 | – |

If one fails, the message lists exactly which endpoint broke the rule and how to fix it.

## Running only some tests

```
dotnet test tests/MyApplication.Api.Tests --filter "Category=Unit"
dotnet test tests/MyApplication.Api.Tests --filter "Category=Integration"
dotnet test tests/MyApplication.Api.Tests --filter "Area=Weather warnings"
dotnet test tests/MyApplication.Api.Tests --filter "Requirement=ES-FR-021"
```

## Adding tests for your feature

1. Create `Integration/<YourFeature>ApiTests.cs`, e.g. `HospitalsApiTests.cs`.
2. Copy this starting point and change the names:

```csharp
using System.Net;
using System.Net.Http.Json;
using MyApplication.Api.Models;

namespace MyApplication.Api.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Area", "Hospitals")]                       // your feature
public class HospitalsApiTests : IClassFixture<EmergencyApiFactory>
{
    private const string Url = "/api/emergency/v1/hospitals";
    private readonly HttpClient _admin;
    private readonly HttpClient _viewer;

    public HospitalsApiTests(EmergencyApiFactory factory)
    {
        _admin = factory.CreateClientAs(Roles.Admin);      // logged in as Admin
        _viewer = factory.CreateClientAs(Roles.Viewer);    // logged in as Viewer
    }

    // TC-API-HOS-01
    [Fact]
    [Trait("Requirement", "ES-FR-0XX")]                // the SRS row this proves
    public async Task Admin_CanAddAHospital()
    {
        var response = await _admin.PostAsJsonAsync(Url, new { name = "General", /* ...fields... */ });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
```

3. Write tests for: the happy path, each role that should be **refused** (403), bad input (400),
   unknown id (404), and any conflict rule (409). `WeatherWarningsApiTests.cs` has an example of each.

**Rules that keep the suite reliable**
- Each test creates its own data. Tests in a class share one database, so never assert
  "there are exactly 3 hospitals" – assert "the one I just made is in the list".
- Never call another team's real service. If your code calls one, add a fake in `Fakes/`
  and swap it in `EmergencyApiFactory` the way `FakeTransitClient` is.
- Name tests as sentences: `Viewer_CannotDeleteAUnit_Gets403`.
- Put the test ID in a comment above the test and the SRS requirement in `[Trait("Requirement", ...)]`,
  and add a matching row to the **Test Cases** sheet in the SRS.

## Regression run – every sprint, before the release

1. Pull the latest `main`, then run the whole suite and save the results as evidence:
   ```
   dotnet test tests/MyApplication.Api.Tests --logger "trx;LogFileName=sprint-N.trx" --results-directory TestResults
   ```
   (Replace N with the sprint number.) This also prints a summary like `total: 119, failed: 0`.
2. Screenshot the summary and put it in the **Software Release Note**, with the number of tests,
   passed/failed, and any known failures with their work item number.
3. Update the **Test results** column in the SRS Test Cases sheet.
4. Tag the release in Git (see below).

## Tagging a release

```
git checkout main
git pull
git tag -a sprint-N -m "Sprint N release"
git push origin sprint-N
```

The tag then shows under **Repos → Tags** in Azure DevOps (or **Tags** on GitHub).
