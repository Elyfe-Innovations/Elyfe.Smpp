# Elyfe.Smpp

[![NuGet](https://img.shields.io/nuget/v/Elyfe.Smpp.svg?logo=nuget)](https://www.nuget.org/packages/Elyfe.Smpp)
[![Downloads](https://img.shields.io/nuget/dt/Elyfe.Smpp.svg?logo=nuget)](https://www.nuget.org/packages/Elyfe.Smpp)
[![Build](https://github.com/Elyfe-Innovations/Elyfe.Smpp/actions/workflows/nuget-publish.yml/badge.svg)](https://github.com/Elyfe-Innovations/Elyfe.Smpp/actions/workflows/nuget-publish.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![Targets](https://img.shields.io/badge/targets-netstandard2.1%20%7C%20net10.0-512BD4)

A .NET implementation of the **SMPP 3.4** protocol, aimed at developers integrating SMS into their applications and at
anyone learning the protocol. It handles binding, keep-alives, reconnection, concatenated messages, delivery receipts
and the encoding rules that SMSCs expect, so you work with messages rather than PDUs.

```csharp
var client = new SmppClient(loggerFactory);
client.Properties.Host = "smsc.example.com";
client.Properties.Port = 2775;
client.Properties.SystemId = "systemid";
client.Properties.Password = "password";

client.MessageReceived += (_, e) => Console.WriteLine(e.ShortMessage);
client.Start();

await client.SendMessageAsync(new TextMessage
{
    SourceAddress = "5750",
    DestinationAddress = "255700000000",
    Text = "Hello from Elyfe.Smpp",
    RegisterDeliveryNotification = true
});
```

## Install

```shell
dotnet add package Elyfe.Smpp
```

Targets `netstandard2.1` and `net10.0`. Applications on .NET Framework 4.x are **not** supported — see
[Upgrading](#upgrading).

## Getting started

### Configure and start a client

`SmppClient` owns the connection. Set its `Properties` before calling `Start()`, and it binds, keeps the session alive
and reconnects on its own.

```csharp
var client = new SmppClient(loggerFactory);

SmppConnectionProperties properties = client.Properties;
properties.Host = "smsc.example.com";
properties.Port = 2775;
properties.SystemId = "systemid";
properties.Password = "password";
properties.SystemType = "";
properties.DefaultServiceType = "5750";
properties.DefaultEncoding = DataCoding.UCS2;
properties.AddressTon = TypeOfNumber.International;
properties.AddressNpi = NumberingPlanIndicator.ISDN;

// Resume a lost connection after 30 seconds
client.AutoReconnectDelay = 30000;
// Send an EnquireLink PDU every 15 seconds
client.KeepAliveInterval = 15000;

client.Start();
```

`Start()` returns immediately and connects in the background. Use `ForceConnect(timeout)` when you need the bind to have
completed before the call returns.

### Handle events

```csharp
client.MessageReceived    += (_, e) => { /* an MO message arrived */ };
client.MessageDelivered   += (_, e) => { /* a delivery receipt arrived */ };
client.MessageSent        += (_, e) => { /* the SMSC accepted a submission */ };
client.ConnectionStateChanged += (_, e) => { /* Closed / Connecting / Connected */ };
```

### Send messages

`SendMessageAsync` is the primary API. The synchronous `SendMessage` overloads block the calling thread until the SMSC
responds and are kept for compatibility.

```csharp
try
{
    await client.SendMessageAsync(message, timeout: 30000, cancellationToken);
}
catch (SmppException ex)
{
    // ex.ErrorCode carries the SMPP status returned by the SMSC
}
```

Text longer than one segment is split automatically and concatenated with a UDH header — the segment id comes from
`SegmentIdGeneratorFactory.Generator`. `MultiPartTextMessage` splits identically but waits for a single
`submit_sm_resp` covering the whole message rather than one per segment.

### Correlate submissions with receipts

Set `UserMessageReference` on a message and read it back on the `MessageSent` and `MessageDelivered` events:

```csharp
msg.UserMessageReference = Guid.NewGuid().ToString();
```

To use the reference only inside your application without submitting it to the SMSC, set
`ShortMessage.SubmitUserMessageReference` to `false`.

### Customise the outgoing PDU

Override `CreateSubmitSm()` to reach fields the message model does not expose:

```csharp
class MyTextMessage : TextMessage
{
    protected override SubmitSm CreateSubmitSm(
        SmppEncodingService encodingService, SmppAddress destAddress = null, SmppAddress srcAddress = null)
    {
        var sm = base.CreateSubmitSm(encodingService, destAddress, srcAddress);
        sm.SourceAddress.Ton = TypeOfNumber.Alphanumeric;
        return sm;
    }
}
```

`RequestPDU` and `ResponsePDU` are also reachable directly through `SendPduAsync` when you need a command the client
does not wrap.

## Logging

The library logs through `Microsoft.Extensions.Logging` and depends on the abstractions package only — you choose the
sink. Pass an `ILoggerFactory` to the constructor, and register one with `SmppLog` to capture diagnostics from the
protocol layer as well:

```csharp
using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole().SetMinimumLevel(LogLevel.Debug));

SmppLog.SetLoggerFactory(loggerFactory);   // JamaaTech.Smpp.Net.Lib.Logging
var client = new SmppClient(loggerFactory);
```

## Encoding

SMPP data coding is the usual source of surprises. Two knobs cover most cases.

**Per-client encoding** — the default is `Encoding.BigEndianUnicode`:

```csharp
client.SmppEncodingService = new SmppEncodingService(Encoding.UTF8);
```

**SMSC default alphabet** — `SMSCDefaultEncoding` (in `JamaaTech.Smpp.Net.Lib.Util`) implements the GSM 03.38 alphabet
by default. Set `UseGsmEncoding` to `false` for the simpler original Jamaa mapping, which omits the Greek characters:

```csharp
SMSCDefaultEncoding.UseGsmEncoding = false;
```

See the [encoding notes](https://github.com/AdhamAwadhi/JamaaSMPP/wiki/Smpp-Encoding) for background.

## Separate transmit and receive sessions

`SmppConnectionProperties.UseSeparateConnections` chooses the bind strategy:

| Value | Behaviour |
| --- | --- |
| `null` (default) | `true` for `InterfaceVersion.v33`, otherwise `false` |
| `true` | Two sessions — `BindReceiver` and `BindTransmitter` |
| `false` | One session — `BindTransceiver` |

## Testing against a simulator

[SMPPSim](http://www.seleniumsoftware.com/downloads.html) is a convenient local SMSC — see its
[user guide](http://www.seleniumsoftware.com/user-guide.htm). The `DemoClient` project in this repository is a working
console client you can point at it.

## Repository layout

| Path | Contents |
| --- | --- |
| `JamaaTech.Smpp.Net.Lib` | Protocol layer: PDUs, TLVs, sessions, encoding |
| `JamaaTech.Smpp.Net.Client` | `SmppClient` and the message model |
| `Client` | `ISmscClient` and `AddSmscClient()` DI wiring — in-repo, not part of the package |
| `Tests`, `Client.Tests` | xUnit test suites |
| `DemoClient` | Sample console client |
| `Benchmarks` | BenchmarkDotNet suite |
| `docs` | Architecture and component notes |

`JamaaTech.Smpp.Net.Lib` and `JamaaTech.Smpp.Net.Client` ship together inside the single `Elyfe.Smpp` package; the
other projects are development-only.

## Upgrading

`2026.1.0` is a breaking release for anyone coming from `2026.0.x`:

- **Dropped `net48` and `netstandard2.0`.** The package now targets `netstandard2.1` and `net10.0`.
- **Removed the bundled Common.Logging shim.** Logging goes through `Microsoft.Extensions.Logging` only.
- **Removed the APM methods** `BeginSendPdu`/`EndSendPdu` and `BeginSendMessage`/`EndSendMessage`. Use `SendPduAsync`
  and `SendMessageAsync`.

It also fixes a response-queue leak that grew for the lifetime of a session, a race that could leave a caller blocked
until its timeout, and `TcpIpSession` ignoring the `CancellationToken` it accepted.

Full history: [Releases](https://github.com/Elyfe-Innovations/Elyfe.Smpp/releases).

## Documentation

- [Documentation index](docs/README.md)
- [Architecture overview](docs/Architecture_Overview.md)
- [`SmppClient`](docs/SmppClient.md) · [`SmppClientSession`](docs/SmppClientSession.md) ·
  [`ShortMessage`](docs/ShortMessage.md)
- [`PDUTransmitter`](docs/PDUTransmitter.md) · [`StreamParser`](docs/StreamParser.md) ·
  [`SmppEncodingService`](docs/SmppEncodingService.md)
- Upstream [JamaaSMPP wiki](https://github.com/AdhamAwadhi/JamaaSMPP/wiki)

## Credits

Elyfe.Smpp continues [JamaaSMPP](https://github.com/AdhamAwadhi/JamaaSMPP) by Adham Awadhi, itself built on the Jamaa
SMPP Library by Jamaa Technologies (Benedict J. Tesha).

## License

MIT — see [LICENSE](LICENSE).

Files carrying a Jamaa Technologies copyright header remain under the Microsoft Reciprocal License, retained in
[LICENSE.Jamaa-Ms-RL](LICENSE.Jamaa-Ms-RL).
