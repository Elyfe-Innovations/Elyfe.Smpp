# Modernize Elyfe.Smpp: TFMs, CPM, and APIs

## Context

`lib/Elyfe.Smpp` is a git submodule (branch `feat/recent-session-updates`) carrying a 2007-era
codebase (JamaaSMPP) that has been incrementally patched rather than modernized. Three concrete
problems today:

1. **TFM sprawl.** The two packable libs target `net48;netstandard2.0;net8.0;net10.0`. Four other
   projects each pick their own TFM (`net48`, `net8.0`, `net9.0`, `net8.0;net10.0`). `net48` and
   `netstandard2.0` are what block modern BCL APIs — e.g. `TcpIpSession.SendAsync` accepts a
   `CancellationToken` and **silently ignores it** because the `ReadOnlyMemory<byte>` + CT socket
   overload doesn't exist on `netstandard2.0`.
2. **No CPM.** `common.props` uses `PackageReference Include` with stale versions
   (`Microsoft.Extensions.Logging` 9.0.0, `MinVer` 4.3.0, `SourceLink.Create.CommandLine` 2.8.3),
   which each csproj then overrides with `PackageReference Update ... Version="10.0.11"`. Versions
   live in three files. Sibling submodules (`lib/Elyfe.Orleans.Marten.Persistence`) already use CPM.
3. **Legacy APIs.** A hand-rolled 130-line `Common.Logging` shim
   (`JamaaTech.Smpp.Net.Lib/Logging/CommonLoggingAdapter.cs`) exists only to keep 2007 logging calls
   compiling; `RunningComponent` spawns raw `Thread`s and still catches `ThreadAbortException` /
   calls `Thread.ResetAbort()`; `PduProcessor` is `Queue<T>` + `ManualResetEvent`; APM
   `BeginSendPdu`/`EndSendPdu` and `BeginSendMessage`/`EndSendMessage` wrap async work in
   `GetAwaiter().GetResult()`; a superseded v1 `ResponseHandler`/`PDUWaitContext` (`AutoResetEvent`)
   still ships alongside the TCS-based `ResponseHandlerV2` that the factory actually returns.

Outcome: two TFMs (`netstandard2.1;net10.0`) on the packable libs, `net10.0` everywhere else, all
package versions in one `Directory.Packages.props`, and the legacy threading/logging/socket paths
replaced with current APIs. Code *structure* (namespaces, the `JamaaTech.*` → `Elyfe.*` rename, the
parallel `Client/` rewrite) is explicitly out of scope for this pass.

### Consumers that must keep working

| Consumer | TFM | Surface used |
|---|---|---|
| `src/Core/src/Tech231.Platform.Core` | net10.0 | ProjectReference to both libs |
| `src/Sms/src/Tech231.Platform.Sms.Orleans/Grains/SmscConnectorGrain.cs` | net10.0 | `new SmppClient { AutoReconnectDelay, KeepAliveInterval, Properties = {…} }`, `.Start()`, `.Shutdown()`, **sync** `.SendMessage(msg, timeoutMs)`, events `ConnectionStateChanged` / `MessageReceived` / `MessageDelivered` |
| `lib/Elyfe.Smpp.Server` (separate submodule) | net10.0 | ProjectReference to `Smpp.Net.Lib` |
| Public NuGet `Elyfe.Smpp` 2026.0.1 | — | the only reason to keep a `netstandard` TFM |

---

## Stage 1 — Standardize target frameworks

**Packable libs** — `JamaaTech.Smpp.Net.Lib/Smpp.Net.Lib.csproj`,
`JamaaTech.Smpp.Net.Client/Smpp.Net.Client.csproj`:

```xml
<TargetFrameworks>netstandard2.1;net10.0</TargetFrameworks>
```

`netstandard2.1` (not 2.0) is the choice that honours "standardize on netstandard" while still
unlocking `ReadOnlyMemory<byte>` socket overloads with `CancellationToken`, `ValueTask`,
`IAsyncDisposable`, and `IAsyncEnumerable` — the APIs Stage 3 depends on. It also drops .NET
Framework by definition, which is the point. `net10.0` is the current LTS.

> Alternative if you'd rather have zero conditional compilation: **`net10.0` only**. Every known
> consumer is already net10.0; the sole cost is breaking unknown netstandard consumers of the public
> package. Say the word and I'll collapse Stage 1 to a single TFM and delete the `Compat/` work below.

**Everything else** → `net10.0` single TFM:

- `Benchmarks/Benchmarks.csproj` (`net8.0` → `net10.0`, bump BenchmarkDotNet off 0.13.12)
- `Client/Client.csproj`, `Client.Tests/Client.Tests.csproj` (`net9.0` — non-LTS — → `net10.0`)
- `Tests/JamaaTech.Smpp.Net.Lib.Tests/…csproj` (`net8.0;net10.0` → `net10.0`)
- `DemoClient/DemoClient.csproj` (`net48` → `net10.0`; it is the only net48-only project and is a
  console sample. Retarget rather than delete, and drop its `Common.Logging` usage in Stage 3.)

**Bugs to fix while here:**

- `Client/Client.csproj:11` and `Elyfe.Smpp.slnx:2-3` reference `JamaaTech.SMPP.Net.Lib` — wrong
  casing for the on-disk `JamaaTech.Smpp.Net.Lib`, which fails on case-sensitive filesystems.
- `Elyfe.Smpp.slnx` lists only 3 of 8 projects. Make it list all of them and delete `jamaasmpp.sln`.
- Delete `build.cake`, `build.ps1`, `build.bat` — Cake + `vswhere` + `MSBuildToolVersion.VS2022` +
  AppVeyor env vars, and they reference a `./src/**` layout that doesn't exist. CI
  (`.github/workflows/nuget-deploy.yml`) already invokes `dotnet` directly.
- Change the workflow to restore/build/pack the whole solution (`Elyfe.Smpp.slnx`) and add a
  `dotnet test` step — today it only touches `Smpp.Net.Client.csproj`, so the test projects never run
  in CI.

---

## Stage 2 — Central Package Management

Follow the pattern already in `lib/Elyfe.Orleans.Marten.Persistence` (its own
`Directory.Build.props` + `Directory.Packages.props` at the submodule root, since the submodule also
builds standalone and publishes to NuGet). Note `Directory.Packages.props` in the Platform lives at
`src/`, so it does **not** cover `lib/`.

**New `lib/Elyfe.Smpp/Directory.Packages.props`** — `ManagePackageVersionsCentrally=true` plus
`PackageVersion` entries. Match the Platform's `src/Directory.Packages.props` versions so the two
trees agree: `Microsoft.Extensions.Logging.Abstractions` 10.0.11, `Microsoft.NET.Test.Sdk` 18.8.1,
`xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.5, `coverlet.collector` 10.0.1, `Moq` 4.20.72,
`Microsoft.Extensions.DependencyInjection.Abstractions` / `Options.ConfigurationExtensions` 10.0.11,
`MinVer` 7.0.0, plus `BenchmarkDotNet` and `PolySharp` (latest).

**New `lib/Elyfe.Smpp/Directory.Build.props`** — absorbs all of `common.props` (package metadata,
`LangVersion`, `SignAssembly`/`AssemblyOriginatorKeyFile`, `README.md` packing, `NoWarn`) and adds
`ImplicitUsings`, `Nullable`, `GenerateDocumentationFile`. Then **delete `common.props`** and its two
`<Import Project="..\common.props" />` lines.

**Package-reference cleanup driven by CPM:**

- Strip every `Version=` from `PackageReference`s and convert the `Update`-with-Version items in the
  two lib csprojs to plain `Include`s.
- **Drop `SourceLink.Create.CommandLine` 2.8.3.** SourceLink is built into the SDK; replace with
  `PublishRepositoryUrl`, `EmbedUntrackedSources`, `DebugType=portable`,
  `ContinuousIntegrationBuild` (CI-only) in `Directory.Build.props`.
- **Replace `Microsoft.Extensions.Logging` + `Microsoft.Extensions.Logging.Console` with
  `Microsoft.Extensions.Logging.Abstractions`** in both libs. A library should not drag in a logging
  implementation or a console sink; today it does both. `Console` moves to `DemoClient` only.
- Add `PolySharp` (`PrivateAssets=all`) so `required`/`init`/nullable attributes work under
  `LangVersion latest` on `netstandard2.1`.
- Root `Directory.Build.props` at the Platform level already sets `NoWarn=NU1507`; the submodule's
  own props must repeat it for standalone builds.

Also update `src/Directory.Packages.props:34` (`Elyfe.Smpp` 2026.0.1) when a new version is cut, and
add a `MinVerTagPrefix`-consistent release-notes entry — `common.props` currently advertises
`v2023.10.0` notes.

---

## Stage 3 — Modernize the APIs

### 3a. Logging: delete the `Common.Logging` shim

`JamaaTech.Smpp.Net.Lib/Logging/CommonLoggingAdapter.cs` defines a fake `Common.Logging` namespace
(`ILog`, `LogManager`, `MicrosoftExtensionsLogAdapter`) purely to keep old call sites compiling. Every
declaration site does
`LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType)`.

- Delete the file. Replace with `Microsoft.Extensions.Logging.Abstractions` `ILogger` throughout.
- 33 call sites across 6 files: `JamaaTech.Smpp.Net.Client/SmppClient.cs` (11),
  `JamaaTech.Smpp.Net.Lib/StreamParser.cs` (10), `SmppClientSession.cs` (6),
  `Protocol/SendSmPDU.cs` (3), `Logging/LoggingExtensions.cs` (2), `Util/RunningComponent.cs` (1),
  `DemoClient/Program.cs` (5).
- `_Log.DebugFormat("… {0} …", x)` → `_logger.LogDebug("… {Thing} …", x)` with named structured
  placeholders (the shim passed `{0}`-style formats straight into `LogTrace`, so today's structured
  templates are broken anyway).
- Ambient-static access is what the shim provided and what `RunningComponent`/`SendSmPDU` rely on.
  Preserve that shape but on the modern abstraction: an internal `SmppLog` holder with
  `public static void SetLoggerFactory(ILoggerFactory)` defaulting to `NullLoggerFactory.Instance`.
  Prefer a constructor-injected `ILoggerFactory`/`ILogger` where the type already has a constructor
  the caller controls (`SmppClient`, `SmppClientSession`, `StreamParser`), falling back to the
  static. **Do not** default to `LoggerFactory.Create(… SetMinimumLevel(Trace))` as the shim does —
  that silently formats every trace message in a library with no configured sink.
- Nothing in the Platform calls `Common.Logging.LogManager.SetLoggerFactory`, so removing the
  namespace is safe for our tree (it is technically a break for public-package consumers → major
  version bump).

### 3b. Threading

- **`Util/RunningComponent.cs`** — remove the `ThreadAbortException` catch and `Thread.ResetAbort()`
  (both are `PlatformNotSupportedException` on .NET Core; dead code that reads as live). Replace the
  raw `Thread` + `IsBackground` + `Join(5000)` with a long-running `Task` +
  `CancellationTokenSource`, and expose `StopAsync`/`IAsyncDisposable` instead of the blocking
  `Stop(bool allowCompleteCycle)`. Note the existing `Stop(allowCompleteCycle: true)` branch never
  clears `vRunning` — fix as part of the rewrite. Replace
  `System.Diagnostics.Debug.WriteLine` diagnostics with `ILogger`.
- **`Util/PduProcessor.cs`** — `Queue<T>` + `ManualResetEvent` + `while(true)` → a bounded
  `System.Threading.Channels.Channel<T>` with `await foreach (var pdu in reader.ReadAllAsync(ct))`.
  This removes the busy `WaitPdu()` handoff and gives real backpressure at `DEFAULT_CAPACITY = 256`.
  `Channel<T>` is in-box on both TFMs via the `System.Threading.Channels` package on netstandard2.1.
- **Delete the superseded v1 path**: `ResponseHandler.cs` (`AutoResetEvent` pair) and
  `PDUWaitContext.cs` (`AutoResetEvent`). `ResponseHandlerFactory.Create` defaults to `"v2"`, so v1
  is only reachable via the `"v1"`/`"legacy"` string in `ResponseHandlerOptions.Implementation`.
  Remove those cases and `Tests/…/ResponseHandler_Threading_Tests.cs` (the only caller).
  Keep `ResponseHandlerV2` and `ConcurrentResponseHandler`.
- **`ResponseHandlerFactory`** — replace the `switch` on a magic string with an enum, and replace the
  static mutable `_options` + `lock` + "already configured" throw with DI-friendly
  `IOptions<ResponseHandlerOptions>`. `SmppClientSession.cs:380` is the only production caller.
- **`SmppClient.cs:425-462`** — `Monitor.TryEnter`/`Enter`/`Exit` on a bare `object` with
  `Exit` in a `finally` that can run without a matching `Enter`. Replace with `SemaphoreSlim` (the
  guarded region is around connect, which is async). `System.Threading.Lock` is net9+ only, so it
  would need a `#if` — `SemaphoreSlim` works on both TFMs and is the right primitive for async anyway.
- **`Task.Factory.StartNew(…, TaskCreationOptions.DenyChildAttach)`** in
  `Networking/TcpIpSession.cs` event raisers → `Task.Run`, and log handler exceptions via `ILogger`
  instead of `Debug.WriteLine`.

### 3c. Sockets and buffers

In `JamaaTech.Smpp.Net.Lib/Networking/TcpIpSession.cs`:

- `SendAsync(byte[], CancellationToken)` and `SendAsync(byte[], int, int, CancellationToken)`
  currently call `vSocket.SendAsync(new ArraySegment<byte>(…), SocketFlags.None)` and **discard the
  token**. Switch to `SendAsync(ReadOnlyMemory<byte>, SocketFlags, CancellationToken)` (returns
  `ValueTask<int>`, available on netstandard2.1) and pass the token through. Same for the receive path.
- `Socket.ConnectAsync(EndPoint, CancellationToken)` is net5+ only → this is the one genuine TFM gap.
  Put it behind a single internal `Compat/SocketCompat.cs` helper rather than `#if`-ing the call site.
- Keep the WinSock `NativeErrorCode` switches in `HandleConnectionException`/`WrapAndThrow` but
  express them as `SocketError` enum values (`SocketError.NetworkDown` etc.) instead of bare
  `10050`/`10051` integers — the numeric codes are Windows-specific and the enum is portable.
- `socket.LingerState.Enabled = false` in `CreateClientSocket` mutates a returned copy on some
  runtimes; assign `new LingerOption(false, 0)` instead.

### 3d. Remove APM / sync-over-async shims

- `SmppClientSession.cs:253-278` — `BeginSendPdu`/`EndSendPdu` (`EndSendPdu` does
  `task.GetAwaiter().GetResult()`).
- `SmppClient.cs:315-352` — `BeginSendMessage`/`EndSendMessage` (same pattern).

Delete both pairs; the `…Async` methods they wrap already exist and are public. Also delete the
commented-out `WaitResponseAsync` call at `SmppClientSession.cs:231`.

**Consumer impact:** `SmscConnectorGrain.cs:500` calls the **sync** `_smppClient.SendMessage(textMessage, timeoutMs)`,
not the APM pair, so removing Begin/End does not touch the Platform. Converting that call site to
`SendMessageAsync` is a worthwhile follow-up (a grain blocking a thread on a network round-trip is
bad), but it changes grain control flow — I'll do it only if you want it in this pass.

### 3e. Smaller cleanups

- `Util/Latin1Encoding.cs:31` — `Encoding.GetEncoding(28591)` in a static ctor → `Encoding.Latin1`
  (net5+; keep `GetEncoding(28591)` on the netstandard leg via `Compat`).
- `[Serializable]` on 6 exception types (`SessionBindInfo`, `TcpIpSessionClosedException`,
  `PDUException`, `TcpIpException`, `TcpIpConnectionException`, `SmppClientException`) — audit for
  the obsolete `(SerializationInfo, StreamingContext)` ctor pattern (SYSLIB0051). Remove the
  BinaryFormatter-era ctors; keep or drop `[Serializable]` per type.
- `Logging/LoggingExtensions.cs` — `DumpStringDefault` reflects over every property on every PDU with
  an empty `catch {}`, on every log call. Gate the whole thing behind
  `_logger.IsEnabled(LogLevel.Debug)` at the call sites and replace `AppendFormat` with interpolated
  handlers; `BytesToStringHex` → `Convert.ToHexString` (net5+, via `Compat` on netstandard).
- `ArgumentNullException("socket")` string literals → `nameof`, and `ArgumentNullException.ThrowIfNull`
  (net6+, via `Compat`).
- `GlobalUsings.cs` currently globals `System.Collections` (non-generic) — drop it; verify nothing
  depends on it.
- Enable `Nullable=enable` on `Smpp.Net.Lib` (currently explicitly `disable`) — do this **last**, as
  a separate commit, since it will surface a wave of warnings. Don't set
  `TreatWarningsAsErrors=true` until that lands.

---

## Files to touch

**New:** `lib/Elyfe.Smpp/Directory.Packages.props`, `lib/Elyfe.Smpp/Directory.Build.props`,
`JamaaTech.Smpp.Net.Lib/Compat/` (SocketCompat, ThrowHelper, encoding/hex shims),
`JamaaTech.Smpp.Net.Lib/Logging/SmppLog.cs`.

**Deleted:** `common.props`, `jamaasmpp.sln`, `build.cake`, `build.ps1`, `build.bat`,
`JamaaTech.Smpp.Net.Lib/Logging/CommonLoggingAdapter.cs`,
`JamaaTech.Smpp.Net.Lib/ResponseHandler.cs`, `JamaaTech.Smpp.Net.Lib/PDUWaitContext.cs`,
`Tests/JamaaTech.Smpp.Net.Lib.Tests/ResponseHandler_Threading_Tests.cs`.

**Modified:** all 8 `.csproj`, `Elyfe.Smpp.slnx`, `.github/workflows/nuget-deploy.yml`, and the
source files named per stage above (`SmppClient.cs`, `SmppClientSession.cs`, `StreamParser.cs`,
`TcpIpSession.cs`, `Util/RunningComponent.cs`, `Util/PduProcessor.cs`, `Util/Latin1Encoding.cs`,
`ResponseHandlerFactory.cs`, `ResponseHandlerOptions.cs`, `Logging/LoggingExtensions.cs`,
`Protocol/SendSmPDU.cs`, the 6 `[Serializable]` types, `DemoClient/Program.cs`, `GlobalUsings.cs`).

## Commit sequencing

The submodule is on `feat/recent-session-updates` with 3 already-modified csprojs (the net10.0
bumps), which Stage 1/2 rewrite — no conflict. Commit per stage inside the submodule, then bump the
submodule pointer in the Platform repo. `lib/Elyfe.Smpp.Server` is a **separate submodule** that
project-references `Smpp.Net.Lib`; if 3a/3b changes its build, that's its own commit there.

## Verification

Run from `lib/Elyfe.Smpp` unless noted.

1. `dotnet restore Elyfe.Smpp.slnx` — must succeed with no `NU1008`/`NU1010` (proves CPM is
   complete: no stray inline `Version=`, no missing `PackageVersion`).
2. `dotnet build Elyfe.Smpp.slnx -c Release` — confirm `bin/Release/` contains exactly
   `netstandard2.1/` and `net10.0/`, and no `net48/` or `netstandard2.0/` (delete stale `bin`/`obj`
   first, since `net48`/`netstandard2.0` outputs exist today and would mask a regression).
3. `dotnet test Elyfe.Smpp.slnx` — `Tests/JamaaTech.Smpp.Net.Lib.Tests` (`PDUWaitContextAsyncTests`,
   `ResponseHandlerTests`, `RunningComponentTests`, `SmppClientSessionThreadingTests`) and
   `Client.Tests/SmscClientTests`. `RunningComponentTests` and `SmppClientSessionThreadingTests` are
   the regression gate for 3b — read them before rewriting `RunningComponent` and keep their
   assertions passing (adjust only where the API intentionally changed from `Stop` to `StopAsync`).
4. `dotnet pack JamaaTech.Smpp.Net.Client/Smpp.Net.Client.csproj -c Release -o nupkgs`, then
   `unzip -l nupkgs/*.nupkg` — verify `lib/netstandard2.1/` + `lib/net10.0/` each contain both
   `JamaaTech.Smpp.Net.Client.dll` and `JamaaTech.Smpp.Net.Lib.dll` (the `CopyProjectReferencesToPackage`
   target packs the Lib into the Client package — do not break it), plus a `.snupkg`/embedded PDB
   with SourceLink metadata.
5. Whole-Platform build: `cd /home/alfkonee/Code/Platform && dotnet build src/Sms/src/Tech231.Platform.Sms.Orleans`
   and `dotnet build lib/Elyfe.Smpp.Server` — the real test that `SmscConnectorGrain`'s
   `new SmppClient { … }` / `Start` / `Shutdown` / `SendMessage` surface and the Server's use of
   `Smpp.Net.Lib` both still compile.
6. `dotnet test src/Sms/test/Tech231.Platform.Sms.Orleans.Tests` — covers `DeliveryReceiptTests`,
   which parses receipts via `JamaaTech.Smpp.Net.Client`.
7. End-to-end smoke: `dotnet run --project DemoClient` against a local SMSC to confirm bind →
   `submit_sm` → `deliver_sm` still works after the socket/threading rewrite, with
   `SmppLog.SetLoggerFactory` wired to a console logger to verify logging output survived 3a.
