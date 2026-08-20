# Elyfe.Smpp documentation

Component-level notes on the library. For installation and everyday usage, start with the
[project README](../README.md).

| Document | Covers |
| --- | --- |
| [Architecture overview](Architecture_Overview.md) | Layering, data flow, threading, error handling, configuration |
| [`SmppClient`](SmppClient.md) | The high-level client: connection lifecycle, events, sending messages |
| [`SmppClientSession`](SmppClientSession.md) | Binding, keep-alive, PDU dispatch, component assembly |
| [`ShortMessage`](ShortMessage.md) | The message model, concatenation and UDH generation |
| [`StreamParser`](StreamParser.md) | Turning the inbound byte stream into PDUs |
| [`PDUTransmitter`](PDUTransmitter.md) | Serialising and writing PDUs |
| [`SmppEncodingService`](SmppEncodingService.md) | Data codings, C-strings, numeric conversions |

These describe the library as of `2026.1.0` — `netstandard2.1` and `net10.0`, logging through
`Microsoft.Extensions.Logging`, and `Task`-based send APIs. The APM `Begin*`/`End*` pairs documented in earlier
revisions no longer exist.
