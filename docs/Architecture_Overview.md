# Elyfe.Smpp - Architecture Overview

## Table of Contents
1. [Library Overview](#library-overview)
2. [System Architecture](#system-architecture)
3. [Component Relationships](#component-relationships)
4. [Data Flow](#data-flow)
5. [Protocol Stack](#protocol-stack)
6. [Threading Model](#threading-model)
7. [Error Handling Strategy](#error-handling-strategy)
8. [Configuration Management](#configuration-management)
9. [Performance Characteristics](#performance-characteristics)
10. [Deployment Considerations](#deployment-considerations)

## Library Overview

Elyfe.Smpp is a .NET implementation of the SMPP (Short Message Peer-to-Peer) protocol, designed to provide robust SMS communication capabilities for .NET applications. The library follows a layered architecture that separates concerns between high-level application interfaces, protocol handling, and low-level network communication.

### Key Features
- **Complete SMPP 3.4 Support**: Full implementation of SMPP protocol specification
- **Target Frameworks**: `netstandard2.1` and `net10.0`
- **Task-Based API**: `SendMessageAsync`/`SendPduAsync` alongside the blocking overloads
- **Automatic Reconnection**: Built-in connection management with automatic reconnection
- **Multi-Part Message Support**: Automatic handling of concatenated SMS messages
- **Event-Driven Architecture**: Comprehensive event system for message and connection handling
- **Thread-Safe Design**: Safe for use in multi-threaded environments
- **Extensible Design**: Modular architecture allowing for customization and extension
- **Pluggable Logging**: Diagnostics go through `Microsoft.Extensions.Logging`; the library takes the abstractions package only

## System Architecture

### High-Level Architecture
```mermaid
graph TB
    subgraph "Application Layer"
        APP[Application Code]
        CONFIG[Configuration]
    end
    
    subgraph "Client Layer"
        SC[SmppClient]
        CP[SmppConnectionProperties]
        SM[ShortMessage]
    end
    
    subgraph "Session Layer"
        SCS[SmppClientSession]
        RH[ResponseHandlerV2]
        PT[PDUTransmitter]
    end
    
    subgraph "Protocol Layer"
        SP[StreamParser]
        PDU[PDU Objects]
        ES[SmppEncodingService]
    end
    
    subgraph "Network Layer"
        TCP[TcpIpSession]
        SOCKET[Socket]
    end
    
    subgraph "External Systems"
        SMSC[SMSC Server]
    end
    
    APP --> SC
    CONFIG --> CP
    SC --> SCS
    SC --> SM
    SCS --> RH
    SCS --> PT
    SCS --> SP
    SP --> PDU
    SP --> ES
    PT --> TCP
    SP --> TCP
    TCP --> SOCKET
    SOCKET --> SMSC
```

### Component Layers
1. **Application Layer**: User code and configuration
2. **Client Layer**: High-level SMPP client interface
3. **Session Layer**: Session management and coordination
4. **Protocol Layer**: SMPP protocol implementation
5. **Network Layer**: TCP/IP communication
6. **External Systems**: SMSC servers and network infrastructure

## Component Relationships

### Core Component Dependencies
```mermaid
graph TD
    subgraph "Core Components"
        SC[SmppClient]
        SCS[SmppClientSession]
        SP[StreamParser]
        PT[PDUTransmitter]
        RH[ResponseHandlerV2]
        TCP[TcpIpSession]
        ES[SmppEncodingService]
    end
    
    subgraph "Message Components"
        SM[ShortMessage]
        TM[TextMessage]
        MPTM[MultiPartTextMessage]
    end
    
    subgraph "Protocol Components"
        PDU[PDU Base]
        REQ[RequestPDU]
        RESP[ResponsePDU]
        SSM[SubmitSm]
        DSM[DeliverSm]
    end
    
    SC --> SCS
    SC --> SM
    SCS --> SP
    SCS --> PT
    SCS --> RH
    SCS --> TCP
    SP --> PDU
    PT --> PDU
    RH --> RESP
    SM --> TM
    SM --> MPTM
    TM --> SSM
    MPTM --> SSM
    PDU --> REQ
    PDU --> RESP
    REQ --> SSM
    REQ --> DSM
```

### Component Responsibilities Matrix
| Component | Primary Responsibility | Dependencies | Events |
|-----------|----------------------|--------------|--------|
| SmppClient | High-level interface, connection management | SmppClientSession, ShortMessage | ConnectionStateChanged, MessageReceived, MessageSent |
| SmppClientSession | Session coordination, PDU processing | StreamParser, PDUTransmitter, IResponseHandler | PduReceived, SessionClosed |
| StreamParser | Byte stream parsing, PDU creation | TcpIpSession, SmppEncodingService | PDUError, ParserException |
| PDUTransmitter | PDU transmission | TcpIpSession | None |
| ResponseHandlerV2 | Matching responses to waiters, timeouts, expiry of unclaimed responses | None | None |
| TcpIpSession | TCP/IP communication | Socket | SessionClosed, SessionException |
| SmppEncodingService | Character encoding/decoding | None | None |

## Data Flow

### Outgoing Message Flow
```mermaid
sequenceDiagram
    participant APP as Application
    participant SC as SmppClient
    participant SM as ShortMessage
    participant SCS as SmppClientSession
    participant PT as PDUTransmitter
    participant TCP as TcpIpSession
    participant SMSC as SMSC Server
    
    APP->>SC: SendMessageAsync(message)
    SC->>SM: GetMessagePDUs()
    SM->>SM: Create SubmitSm PDUs
    SM->>SC: PDU Collection
    SC->>SCS: SendPdu(pdu)
    SCS->>PT: Send(pdu)
    PT->>TCP: Send(bytes)
    TCP->>SMSC: TCP Data
    SMSC->>TCP: SubmitSmResp
    TCP->>SCS: Response
    SCS->>SC: ResponsePDU
    SC->>APP: MessageSent Event
```

### Incoming Message Flow
```mermaid
sequenceDiagram
    participant SMSC as SMSC Server
    participant TCP as TcpIpSession
    participant SP as StreamParser
    participant SCS as SmppClientSession
    participant SC as SmppClient
    participant APP as Application
    
    SMSC->>TCP: TCP Data
    TCP->>SP: Raw Bytes
    SP->>SP: Parse PDU
    SP->>SCS: PduRequestProcessorCallback
    SCS->>SC: Raise PduReceived Event
    SC->>SC: CreateMessage()
    SC->>APP: MessageReceived Event
    SC->>SMSC: DeliverSmResp
```

### Connection Establishment Flow
```mermaid
sequenceDiagram
    participant APP as Application
    participant SC as SmppClient
    participant SCS as SmppClientSession
    participant TCP as TcpIpSession
    participant SMSC as SMSC Server
    
    APP->>SC: Start()
    SC->>SCS: Bind(bindInfo)
    SCS->>TCP: CreateTcpIpSession()
    TCP->>SMSC: TCP Connect
    SCS->>SMSC: Bind Request
    SMSC->>SCS: Bind Response
    SCS->>SC: Session Ready
    SC->>APP: ConnectionStateChanged(Connected)
```

## Protocol Stack

### SMPP Protocol Layers
```mermaid
graph TB
    subgraph "SMPP Protocol Stack"
        subgraph "Application Layer"
            SMS[SMS Application]
        end
        
        subgraph "SMPP Layer"
            SMPP[SMPP Protocol]
            BIND[Bind Operations]
            SUBMIT[Submit Operations]
            DELIVER[Deliver Operations]
            ENQUIRE[Enquire Link]
        end
        
        subgraph "Transport Layer"
            TCP[TCP/IP]
            SOCKET[Socket]
        end
        
        subgraph "Network Layer"
            IP[IP Protocol]
            ETHERNET[Ethernet]
        end
    end
    
    SMS --> SMPP
    SMPP --> BIND
    SMPP --> SUBMIT
    SMPP --> DELIVER
    SMPP --> ENQUIRE
    SMPP --> TCP
    TCP --> SOCKET
    SOCKET --> IP
    IP --> ETHERNET
```

### PDU Structure
```mermaid
graph TB
    subgraph "SMPP PDU Structure"
        HEADER[PDU Header - 16 bytes]
        BODY[PDU Body - Variable]
        TLV[Optional Parameters - Variable]
    end
    
    subgraph "Header Fields"
        LEN[Command Length - 4 bytes]
        TYPE[Command Type - 4 bytes]
        STATUS[Command Status - 4 bytes]
        SEQ[Sequence Number - 4 bytes]
    end
    
    HEADER --> LEN
    HEADER --> TYPE
    HEADER --> STATUS
    HEADER --> SEQ
```

## Threading Model

### Thread Architecture
```mermaid
graph TB
    subgraph "Threading Model"
        subgraph "Main Thread"
            MT[Main Application Thread]
        end
        
        subgraph "Client Threads"
            CT[Client Management Thread]
            RT[Reconnection Thread]
        end
        
        subgraph "Session Threads"
            ST[Session Thread]
            KT[Keep-Alive Thread]
        end
        
        subgraph "Parser Threads"
            PT[StreamParser Thread]
            ET[Event Threads]
        end
        
        subgraph "Processing Threads"
            PTT[PDU Processing Threads]
            MTT[Message Processing Threads]
        end
    end
    
    MT --> CT
    CT --> RT
    CT --> ST
    ST --> KT
    ST --> PT
    PT --> ET
    PT --> PTT
    PTT --> MTT
```

### Thread Safety
- **SmppClient**: Thread-safe for multiple operations
- **SmppClientSession**: Thread-safe with proper synchronization
- **StreamParser**: Runs in dedicated background thread
- **PDUTransmitter**: Thread-safe (stateless)
- **ResponseHandlerV2**: Thread-safe; a single lock covers waiter registration and response retention
- **TcpIpSession**: Thread-safe with connection state management

## Error Handling Strategy

### Error Hierarchy
```mermaid
graph TD
    EX[System.Exception]

    EX --> SE[SmppException]
    SE --> SBE[SmppBindException]
    SE --> STO[SmppResponseTimedOutException]

    EX --> PE[PDUException]
    PE --> PPE[PDUParseException]
    PE --> PFE[PDUFormatException]

    EX --> TIE[TcpIpException]
    TIE --> TCE[TcpIpConnectionException]
    TIE --> TSE[TcpIpSessionClosedException]

    EX --> SCE[SmppClientException]
```

There are four independent roots, all deriving directly from `System.Exception`:

| Root | Raised by | Notable subclasses |
|------|-----------|--------------------|
| `SmppException` | Protocol-level failures; carries the SMSC's `ErrorCode` | `SmppBindException`, `SmppResponseTimedOutException` |
| `PDUException` | Malformed or unparseable PDUs | `PDUParseException`, `PDUFormatException` |
| `TcpIpException` | Transport failures | `TcpIpConnectionException`, `TcpIpSessionClosedException` |
| `SmppClientException` | `SmppClient`-level misuse and state errors | — |

`SmppException` is the one to catch around a send: `ErrorCode` holds the SMPP status the SMSC returned.

### Error Recovery Mechanisms
1. **Connection Errors**: Automatic reconnection with configurable delays
2. **Protocol Errors**: Error response generation and session continuation
3. **Message Errors**: Individual message failure handling without affecting connection
4. **Parser Errors**: Malformed PDU handling with error reporting

## Configuration Management

### Configuration Hierarchy
```mermaid
graph TB
    subgraph "Configuration Layers"
        subgraph "Application Configuration"
            AC[Application Settings]
            CC[Connection Configuration]
        end
        
        subgraph "Client Configuration"
            CP[SmppConnectionProperties]
            TO[Timeout Settings]
            ENC[Encoding Settings]
        end
        
        subgraph "Session Configuration"
            SC[Session Properties]
            KA[Keep-Alive Settings]
            BUF[Buffer Settings]
        end
        
        subgraph "Network Configuration"
            NC[Network Settings]
            SOCK[Socket Options]
        end
    end
    
    AC --> CC
    CC --> CP
    CP --> TO
    CP --> ENC
    CP --> SC
    SC --> KA
    SC --> BUF
    SC --> NC
    NC --> SOCK
```

### Configuration Properties
| Level | Property | Description | Default |
|-------|----------|-------------|---------|
| Client | AutoReconnectDelay | Reconnection delay | 10000ms |
| Client | KeepAliveInterval | EnquireLink interval | 30000ms |
| Client | ConnectionTimeout | Connection timeout | 5000ms |
| Session | DefaultResponseTimeout | PDU response timeout | 5000ms |
| Session | EnquireLinkInterval | Keep-alive interval | set by `SmppClient` from `KeepAliveInterval` |
| Network | SendBufferSize | TCP send buffer | OS default (proxies `Socket.SendBufferSize`) |
| Network | ReceiveBufferSize | TCP receive buffer | OS default (proxies `Socket.ReceiveBufferSize`) |

## Performance Characteristics

### Where the time goes

End-to-end message latency is dominated by the SMSC round trip and the network path to it, not by this library — a
send blocks until the SMSC returns a `submit_sm_resp`, and how long that takes is the SMSC's business. What the
library controls is what happens either side of that wait:

- **Response matching** — `ResponseHandlerV2` is the hot path shared by every in-flight request. `Benchmarks/`
  measures it directly; run `dotnet run -c Release --project Benchmarks` for numbers on your own hardware.
- **Concatenation** — a message split into *n* segments becomes *n* `submit_sm` round trips, so its latency is
  roughly *n* times a single send.
- **Encoding** — `SmppEncodingService` allocates a byte array per conversion. UCS2 doubles the byte count of the
  text and halves the characters that fit in a segment, which is usually the more significant effect.

Throughput is bounded by the SMSC's window size and any rate limit it imposes, and by whether you use one
transceiver session or separate transmit/receive sessions. Measure against your own SMSC rather than assuming a
figure.

### Scalability Considerations
1. **Connection Pooling**: Multiple client instances for high throughput
2. **Message Queuing**: Implement queuing for burst handling
3. **Resource Management**: Monitor memory and connection usage
4. **Load Balancing**: Distribute load across multiple SMSC connections

## Deployment Considerations

### Production Deployment
```mermaid
graph TB
    subgraph "Production Environment"
        subgraph "Application Tier"
            APP[Application Server]
            LB[Load Balancer]
        end
        
        subgraph "SMPP Tier"
            SC1[SmppClient 1]
            SC2[SmppClient 2]
            SC3[SmppClient N]
        end
        
        subgraph "Network Tier"
            FW[Firewall]
            PROXY[Proxy Server]
        end
        
        subgraph "SMSC Tier"
            SMSC1[SMSC 1]
            SMSC2[SMSC 2]
            SMSC3[SMSC N]
        end
    end
    
    APP --> LB
    LB --> SC1
    LB --> SC2
    LB --> SC3
    SC1 --> FW
    SC2 --> FW
    SC3 --> FW
    FW --> PROXY
    PROXY --> SMSC1
    PROXY --> SMSC2
    PROXY --> SMSC3
```

### Deployment Best Practices
1. **High Availability**: Multiple client instances with failover
2. **Monitoring**: Comprehensive logging and metrics collection
3. **Security**: Secure credential management and network security
4. **Scalability**: Horizontal scaling with load balancing
5. **Backup**: Multiple SMSC providers for redundancy

### Monitoring and Alerting
- **Connection Health**: Monitor connection state and reconnection events
- **Message Throughput**: Track message send/receive rates
- **Error Rates**: Monitor error frequencies and types
- **Resource Usage**: Monitor memory, CPU, and network usage
- **Delivery Rates**: Track message delivery success rates

Elyfe.Smpp provides a foundation for SMS communication in .NET applications, covering the SMPP protocol operations an ESME needs along with the connection management, encoding and concatenation concerns that surround them.
