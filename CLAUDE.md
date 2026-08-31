# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this is

`Abm.ProviderDirectory` — Australian FHIR Provider Directory tools and applications.

The solution talks to a FHIR provider directory (currently the ADHA **Provider Connect Australia**
SIT server) over the **HL7 FHIR Bulk Data Export** (Flat FHIR) interface, downloads the exported
NDJSON output files, and streams the resources out as Firely `Hl7.Fhir.Model.Resource` POCOs ready
to be consumed into a local repository.

**Current state:** only the *bulk export* half is written — kick-off, poll, delete, and streamed
download of the manifest's output files. The operations that **insert/persist the downloaded
resources into a local repository have not been written yet**. There is no local store, no
persistence layer and no resource-loading pipeline in the solution at this stage. `ConsoleApplication`
currently stands in for that by writing each downloaded resource to a JSON file under
`C:\Temp\Abm.ProviderDirectory\Output\`.

There are no test projects yet.

## Layout

```
src/Abm.PD/
  Abm.PD.slnx                  Solution (new XML .slnx format, not .sln)
  Abm.PD.Console/              Entry point — the runnable tool
    Program.cs                 Generic Host bootstrap, Serilog, DI wiring
    ConsoleApplication.cs      The scenario being run: builds $export Parameters, drives the export
    Settings/                  ConsoleApplicationSettings
    appsettings.json           Config incl. FhirNavigator repositories + Serilog
  Abm.PD.Domain/               All the reusable logic
    FhirBulkExport/            IFhirBulkExporter / FhirBulkExporter — the core of the solution
    Models/Manifest/           POCOs for the Bulk Data "Output Manifest" JSON
    NdJsonSupport/             NdJsonReader — streaming NDJSON line reader
    FhirSupport/               OperationOutcomeSupport
    DateTimeSupport/           IDateTimeProvider, DateTimeSupport, TimeSpanSupport.ToNarrative()
    Exceptions/                FhirBulkExportException, NdJsonException
    Settings/                  ApplicationSettings, ServiceDefaultTimeZoneSettings
    DependencyInjection/       ServiceCollectionExtension.AddProviderDirectoryServices()
```

## Build and run

```powershell
dotnet build src/Abm.PD/Abm.PD.slnx
dotnet run --project src/Abm.PD/Abm.PD.Console
```

- Target framework is **net10.0**; `ImplicitUsings` and `Nullable` are enabled on both projects.
- `Abm.PD.Console` sets `TreatWarningsAsErrors`, so a warning fails the build. `Abm.PD.Domain` does not.
- Running the console app makes **real HTTP calls to the live SIT provider directory** and starts a
  server-side export job that polls for minutes. Do not run it casually to "check something works" —
  use `dotnet build` for that.

## Key dependencies

- **FhirNavigator** (1.0.0) — supplies `IFhirHttpClientFactory` (Firely `FhirClient`) and a named
  `IHttpClientFactory`, both keyed by repository `Code`. Configured from the `FhirNavigator` section
  and registered by `AddProviderDirectoryServices`. It transitively brings in the Firely SDK
  (`Hl7.Fhir.R4` / `Hl7.Fhir.Base`) — there is no direct Firely package reference.
- **Microsoft.Extensions.Hosting** — Generic Host, options binding with
  `ValidateDataAnnotations().ValidateOnStart()`.
- **Serilog** — configured entirely from the `Serilog` config section; the default logging providers
  are cleared in `Program.cs` so Serilog is the only sink. Console + rolling daily file
  (`application.log`).

## The bulk export flow

`IFhirBulkExporter` is a **stateful, scoped** service. One instance == one export session; it holds
`JobId`, `CurrentSessionStatus`, `Manifest` and friends as private fields, so the methods must be
called in order:

1. `BeginExport(Parameters, ct)` — `POST [base]/$export` as a whole-system operation with
   `Prefer: respond-async`. Only legal from `NotStarted` or `Failed`. Expects **202 Accepted** and
   parses `_jobId` out of the response `Location` / `Content-Location` header. Status becomes
   `InProgress`.
2. `PollExport(ct)` — `GET [base]/$export-poll-status?_jobId=...`. **202** means the server is still
   building (carries `X-Progress`, and optionally `Retry-After`, surfaced as `RequestedPollDelay` —
   honour it). **200** means done: the body is the Output Manifest and status becomes `Completed`.
3. `GetExport(ct)` — only legal once `Completed`. Streams every `output` file listed in the manifest
   and yields `FhirBulkExportResource` (resource + manifest type + source URL + 1-based line number).
4. `DeleteExport(ct)` — `DELETE` on the poll-status URL to cancel/clean up the server-side job.

State is reported back through the `FhirBulkExportState` record; `FhirBulkExportSessionStatus` is
`NotStarted | InProgress | Completed | Deleted | Failed`. FHIR-level failures are **not** thrown —
they come back as a `Failed` state with `OperationOutcome` and `ErrorMessages` populated.
`FhirBulkExportException` is thrown only for protocol or programmer errors.

### Streaming is deliberate — do not break it

`GetExport` → `ReadOutputFile` → `NdJsonReader.ReadAsync` is an unbroken `IAsyncEnumerable` chain so
that an export of any size never holds more than one resource in memory. Specifically:

- `SendAsync` uses `HttpCompletionOption.ResponseHeadersRead` — without it the whole file buffers.
- `GetDecompressedStream` handles gzip/deflate/br for when the handler has not already decompressed.
- Do not introduce a `.ToList()`, collect into a list, or read an output file into a `string`.

### Manifest handling notes

- The models in `Models/Manifest/` follow the current Bulk Data ballot and also retain the v2.0.0
  names — `error` is the old name for `outcome`, and both properties exist so either shape
  deserializes.
- Unknown or server-specific data is kept as `JsonObject? Extension` rather than being dropped.
- The `outcome`/`error` and `deleted` files are **not** read by `GetExport` — it only reads `output`.
  Their presence is logged as a warning. Reading them is unimplemented work, not an oversight.
- Manifest deserialization uses `System.Text.Json` with `JsonSerializerDefaults.Web`; the FHIR
  resources inside the NDJSON need Firely's converters (`.ForFhir(typeof(ModelInfo).Assembly)`).
  The two serializer configurations are not interchangeable.

## Configuration and secrets

- `appsettings.json` / `appsettings.Development.json` hold the `FhirNavigator` repository list. The
  repository `Code` values must match the constants in `HttpClientSupport/HttpClientType.cs`
  (`ProviderConnectAustralia`, `AzurePyroFhirServer`) — that constant is the key passed to both
  client factories.
- `Abm.PD.Console` has a `UserSecretsId`; bearer tokens for the provider directory belong in **user
  secrets**, not in the committed `appsettings*.json`. Note that a real (expiring) SIT bearer token
  is currently checked in to both appsettings files — do not copy that pattern into new config, and
  never add a fresh token to a tracked file.

## Code style in this repo

Match the surrounding code:

- File-scoped namespaces; primary constructors for DI (`public class Foo(ILogger<Foo> logger, ...)`).
- Explicit types in preference to `var`; `record` / `sealed record` for data, `readonly record struct`
  for small values.
- Multi-line method signatures with one parameter per line; named arguments at call sites where the
  meaning of a positional value would not be obvious.
- Comments are `//` with no leading space and explain *why* (a spec rule, a gotcha), not *what*.
  XML doc comments on public domain types, citing the Bulk Data spec where relevant.
- **Australian English** in comments and log messages ("organised", "initialise").
- Structured Serilog logging with named placeholders, `JobId` first on export-related messages.

## Domain background

The FHIR Bulk Data Export specification: https://build.fhir.org/ig/HL7/bulk-data/en/export.html

Provider directory resource load order (by reference dependency), noted at the top of
`ConsoleApplication.cs`: `Practitioner` → `Endpoint` → `Organization` → `Location`,
`HealthcareService`, `PractitionerRole`. This order matters for the not-yet-written local
repository insert step.

An `fhir-expert` agent is available for FHIR specification questions.
