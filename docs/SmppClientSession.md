# SmppClientSession Documentation

## Table of Contents
1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Class Structure](#class-structure)
4. [Properties](#properties)
5. [Events](#events)
6. [Methods](#methods)
7. [Session Lifecycle](#session-lifecycle)
8. [Component Assembly](#component-assembly)
9. [PDU Processing](#pdu-processing)
10. [Keep-Alive Mechanism](#keep-alive-mechanism)
11. [Error Handling](#error-handling)
12. [Usage Examples](#usage-examples)

## Overview

The `SmppClientSession` is a core component that manages individual SMPP sessions with SMSC servers. It serves as the bridge between the high-level `SmppClient` and the low-level protocol components, handling session binding, PDU transmission, response management, and session lifecycle.

### Key Responsibilities
- **Session Binding**: Establishes SMPP sessions with SMSC servers
- **Component Coordination**: Manages StreamParser, PDUTransmitter, and the IResponseHandler
- **PDU Processing**: Handles incoming request PDUs and outgoing responses
- **Keep-Alive Management**: Maintains session health with EnquireLink PDUs
- **Session State Management**: Tracks and manages session states
- **Error Recovery**: Handles session-level errors and cleanup

## Architecture

The SmppClientSession operates as a coordinator for multiple lower-level components:

```mermaid
graph TB
    subgraph "SmppClientSession"
        SCS[SmppClientSession]
        TIMER[Keep-Alive Timer]
        SYNC[SyncRoot]
    end
    
    subgraph "Core Components"
        SP[StreamParser]
        PT[PDUTransmitter]
        RH[ResponseHandlerV2]
        TCP[TcpIpSession]
    end
    
    subgraph "Protocol Layer"
        PDU[PDU Objects]
        REQ[RequestPDU]
        RESP[ResponsePDU]
    end
    
    subgraph "External Dependencies"
        SMSC[SMSC Server]
        APP[Application]
    end
    
    SCS --> TIMER
    SCS --> SYNC
    SCS --> SP
    SCS --> PT
    SCS --> RH
    SCS --> TCP
    SP --> PDU
    PT --> PDU
    RH --> RESP
    TCP --> SMSC
    SCS --> APP
    PDU --> REQ
    PDU --> RESP
```

## Class Structure

### Class Hierarchy
```mermaid
classDiagram
    class SmppClientSession {
        -Timer vTimer
        -PDUTransmitter vTrans
        -IResponseHandler vRespHandler
        -StreamParser vStreamParser
        -TcpIpSession vTcpIpSession
        -object vSyncRoot
        -bool vIsAlive
        -SmppSessionState vState
        -int vDefaultResponseTimeout
        -string vSmscId
        -string vSystemId
        -string vPassword
        -TypeOfNumber vAddressTon
        -NumberingPlanIndicator vAddressNpi
        -SmppEncodingService vSmppEncodingService
        -SendPduCallback vCallback
        +PduReceived event
        +SessionClosed event
        +SendPdu(RequestPDU pdu)
        +SendPdu(RequestPDU pdu, int timeout)
        +SendPduAsync(RequestPDU pdu)
        +SendPduAsync(RequestPDU pdu, int timeout, CancellationToken ct)
        +EndSession()
        +Bind(SessionBindInfo, int, SmppEncodingService)
    }
```

### Core Dependencies
- **StreamParser**: Parses incoming byte streams into PDUs
- **PDUTransmitter**: Sends PDUs to the SMSC
- **ResponseHandlerV2**: Matches responses to their waiters and enforces timeouts
- **TcpIpSession**: Provides TCP/IP communication
- **SmppEncodingService**: Handles character encoding/decoding

## Properties

### Session State Properties
| Property | Type | Description |
|----------|------|-------------|
| `IsAlive` | `bool` | Indicates if the session is active and bound |
| `State` | `SmppSessionState` | Current session state (Open, Transmitter, Receiver, Transceiver, Closed) |
| `SmscID` | `string` | SMSC identifier received during binding |
| `SystemID` | `string` | System identifier used for binding |
| `Password` | `string` | Password used for authentication |

### Configuration Properties
| Property | Type | Description |
|----------|------|-------------|
| `EnquireLinkInterval` | `int` | Interval for keep-alive EnquireLink PDUs (minimum 1000ms) |
| `DefaultResponseTimeout` | `int` | Default timeout for PDU responses |
| `TcpIpProperties` | `TcpIpSessionProperties` | TCP/IP session properties |
| `SyncRoot` | `object` | Synchronization object for thread safety |
| `SmppEncodingService` | `SmppEncodingService` | Encoding service for character handling |

### Address Properties
| Property | Type | Description |
|----------|------|-------------|
| `AddressTon` | `TypeOfNumber` | Type of Number for addresses |
| `AddressNpi` | `NumberingPlanIndicator` | Numbering Plan Indicator for addresses |

### Session States
```mermaid
stateDiagram-v2
    [*] --> Open
    Open --> Transmitter : BindTransmitter
    Open --> Receiver : BindReceiver
    Open --> Transceiver : BindTransceiver
    Transmitter --> Closed : Unbind/Error
    Receiver --> Closed : Unbind/Error
    Transceiver --> Closed : Unbind/Error
    Closed --> [*]
```

## Events

### Event Types
```mermaid
graph LR
    subgraph "Session Events"
        SC[SessionClosed]
    end
    
    subgraph "PDU Events"
        PR[PduReceived]
    end
    
    SC --> PR
```

### Event Details

#### PduReceived
```csharp
public event EventHandler<PduReceivedEventArgs> PduReceived;
```
**Purpose**: Raised when a request PDU is received from the SMSC
**Event Args**: `PduReceivedEventArgs`
- `Request`: The received request PDU
- `Response`: Optional response PDU to send back

#### SessionClosed
```csharp
public event EventHandler<SmppSessionClosedEventArgs> SessionClosed;
```
**Purpose**: Raised when the session is closed
**Event Args**: `SmppSessionClosedEventArgs`
- `Reason`: Reason for session closure
- `Exception`: Exception that caused closure (if any)

## Methods

### PDU Operations

#### Send PDU (Synchronous)
```csharp
public ResponsePDU SendPdu(RequestPDU pdu)
public ResponsePDU SendPdu(RequestPDU pdu, int timeout)
```
**Purpose**: Sends a request PDU and waits for response
**Parameters**:
- `pdu`: The request PDU to send
- `timeout`: Timeout for response (optional, uses default if not specified)
**Returns**: `ResponsePDU` from SMSC
**Behavior**:
- Validates session state and PDU compatibility
- Sends PDU via PDUTransmitter
- Waits for response via the IResponseHandler
- Handles timeout scenarios

#### Send PDU (Asynchronous)
```csharp
public Task<ResponsePDU> SendPduAsync(RequestPDU pdu)
public Task<ResponsePDU> SendPduAsync(RequestPDU pdu, int timeout, CancellationToken cancellationToken = default)
```
**Purpose**: Sends a request PDU without blocking the calling thread
**Parameters**:
- `pdu`: The request PDU to send
- `timeout`: Response timeout in milliseconds; the parameterless overload uses `DefaultResponseTimeout`
- `cancellationToken`: Cancels the wait for the response
**Returns**: A `Task<ResponsePDU>` completing with the SMSC's response

> `BeginSendPdu`/`EndSendPdu` were removed in `2026.1.0`. Use these `Task`-based methods instead.

### Session Management

#### End Session
```csharp
public void EndSession()
```
**Purpose**: Gracefully closes the session
**Behavior**:
- Sends Unbind PDU to SMSC
- Waits for UnbindResp
- Cleans up all components
- Raises SessionClosed event

#### Static Bind Method
```csharp
public static SmppClientSession Bind(SessionBindInfo bindInfo, int timeOut, SmppEncodingService smppEncodingService)
```
**Purpose**: Creates and binds a new session
**Parameters**:
- `bindInfo`: Binding information (credentials, server details)
- `timeOut`: Timeout for bind operation
- `smppEncodingService`: Encoding service to use
**Returns**: Bound `SmppClientSession` instance
**Behavior**:
- Creates TCP/IP session
- Assembles components
- Performs SMPP bind operation
- Starts keep-alive timer

## Session Lifecycle

### Binding Process
```mermaid
sequenceDiagram
    participant App as Application
    participant SCS as SmppClientSession
    participant TCP as TcpIpSession
    participant SMSC as SMSC Server
    
    App->>SCS: Bind(bindInfo, timeout, encoding)
    SCS->>TCP: CreateTcpIpSession()
    TCP->>SMSC: TCP Connect
    SMSC->>TCP: TCP Accept
    SCS->>SCS: AssembleComponents()
    SCS->>SCS: BindSession()
    SCS->>SMSC: Bind Request
    SMSC->>SCS: Bind Response
    SCS->>SCS: Start Timer
    SCS->>App: Session Ready
```

### Component Assembly
```mermaid
flowchart TD
    A[Create TcpIpSession] --> B[Create PDUTransmitter]
    B --> C[Create IResponseHandler]
    C --> D[Create StreamParser]
    D --> E[Wire Event Handlers]
    E --> F[Start StreamParser]
    F --> G[Components Ready]
```

### Session Termination
```mermaid
sequenceDiagram
    participant SCS as SmppClientSession
    participant SMSC as SMSC Server
    participant SP as StreamParser
    participant PT as PDUTransmitter
    
    SCS->>SMSC: Unbind Request
    SMSC->>SCS: Unbind Response
    SCS->>SP: Stop Parser
    SCS->>PT: Cleanup
    SCS->>SCS: Destroy Components
    SCS->>SCS: Raise SessionClosed Event
```

## Component Assembly

### Component Creation Order
1. **TcpIpSession**: Establishes TCP connection
2. **PDUTransmitter**: Handles PDU transmission
3. **IResponseHandler**: Created through `ResponseHandlerFactory.Create()`; matches responses to waiters
4. **StreamParser**: Parses incoming data
5. **Event Wiring**: Connects component events
6. **Parser Start**: Begins parsing incoming data

### Component Dependencies
```mermaid
graph TD
    subgraph "Component Dependencies"
        TCP[TcpIpSession]
        PT[PDUTransmitter]
        RH[ResponseHandlerV2]
        SP[StreamParser]
        ES[SmppEncodingService]
    end
    
    PT --> TCP
    SP --> TCP
    SP --> RH
    SP --> ES
    RH --> PT
```

### Event Handler Wiring
```csharp
private void AssembleComponents()
{
    vTrans = new PDUTransmitter(vTcpIpSession);
    vRespHandler = new ResponseHandler();
    vStreamParser = new StreamParser(
        vTcpIpSession, vRespHandler, new PduProcessorCallback(PduRequestProcessorCallback), vSmppEncodingService);
    
    // Wire event handlers
    vStreamParser.ParserException += ParserExceptionEventHandler;
    vStreamParser.PDUError += PduErrorEventHandler;
    
    // Start stream parser
    vStreamParser.Start();
}
```

## PDU Processing

### Incoming PDU Flow
```mermaid
sequenceDiagram
    participant SMSC as SMSC
    participant SP as StreamParser
    participant SCS as SmppClientSession
    participant APP as Application
    
    SMSC->>SP: Raw Bytes
    SP->>SP: Parse PDU
    SP->>SCS: PduRequestProcessorCallback
    SCS->>SCS: Check PDU Type
    
    alt Unbind Request
        SCS->>SMSC: Unbind Response
        SCS->>SCS: End Session
    else Other Request
        SCS->>APP: Raise PduReceived Event
        APP->>SCS: Response PDU (optional)
        SCS->>SMSC: Send Response
    end
```

### PDU Type Handling
```mermaid
flowchart TD
    A[PDU Received] --> B{PDU Type}
    B -->|Unbind| C[Send UnbindResp]
    B -->|EnquireLink| D[Send EnquireLinkResp]
    B -->|Other| E[Raise PduReceived Event]
    
    C --> F[End Session]
    D --> G[Continue Session]
    E --> H{Has Response?}
    H -->|Yes| I[Send Response]
    H -->|No| J[Send Default Response]
    I --> G
    J --> G
```

### Response Generation
```csharp
private void PduRequestProcessorCallback(RequestPDU pdu)
{
    ResponsePDU resp = null;
    
    if (pdu is Unbind)
    {
        // Handle unbind request
        resp = pdu.CreateDefaultResponce();
        try { SendPduBase(resp); }
        catch {/*silent catch*/}
        EndSession(SmppSessionCloseReason.UnbindRequested, null);
        return;
    }
    
    // Raise event for other PDUs
    resp = RaisePduReceivedEvent(pdu);
    
    if (resp == null)
    {
        if (pdu.HasResponse)
        {
            resp = pdu.CreateDefaultResponce();
        }
    }
    
    if (resp != null)
    { 
        try { SendPduBase(resp); } 
        catch {/*Silent catch*/} 
    }
}
```

## Keep-Alive Mechanism

### EnquireLink Timer
```mermaid
sequenceDiagram
    participant Timer as Keep-Alive Timer
    participant SCS as SmppClientSession
    participant SMSC as SMSC Server
    
    Timer->>SCS: Timer Elapsed
    SCS->>SMSC: EnquireLink
    SMSC->>SCS: EnquireLinkResp
    
    Note over Timer,SMSC: Session Healthy
    
    Timer->>SCS: Timer Elapsed
    SCS->>SMSC: EnquireLink
    Note over SMSC: No Response
    SCS->>SCS: Timeout
    SCS->>SCS: End Session
```

### Timer Configuration
```csharp
private void InitializeTimer()
{
    vTimer = new Timer(DEFAULT_DELAY); // 60 seconds default
    vTimer.Elapsed += new ElapsedEventHandler(TimerCallback);
}

public int EnquireLinkInterval
{
    get { return (int)vTimer.Interval; }
    set
    {
        if (value < 1000) // Minimum 1 second
        {
            throw new ArgumentException("EnqureLink interval cannot be less than 1000 millseconds (1 second)");
        }
        vTimer.Interval = (double)value;
    }
}
```

### Keep-Alive Logic
```csharp
private void TimerCallback(object sender, ElapsedEventArgs e)
{
    EnquireLink enqLink = new EnquireLink(SmppEncodingService);
    
    try
    {
        // Send EnquireLink with 5 seconds response timeout
        EnquireLinkResp resp = (EnquireLinkResp)SendPdu(enqLink, 5000);
    }
    catch (SmppResponseTimedOutException)
    {
        // If there was no response, session is no longer active
        EndSession(SmppSessionCloseReason.EnquireLinkTimeout, null);
    }
    catch (SmppException) { /*Silent catch*/ } // Incorrect bind status
    catch (TcpIpException) {/*Silent catch*/ }
}
```

## Error Handling

### Error Types and Handling
```mermaid
graph TD
    subgraph "Session Errors"
        SE[SmppException]
        SBE[SmppBindException]
        SRTE[SmppResponseTimedOutException]
    end
    
    subgraph "Network Errors"
        TCE[TcpIpConnectionException]
        TSE[TcpIpSessionClosedException]
        TE[TcpIpException]
    end
    
    subgraph "Parser Errors"
        PE[PDUException]
        PEE[ParserException]
    end
    
    SE --> SBE
    SE --> SRTE
    TCE --> TE
    TSE --> TE
    PE --> PEE
```

### Error Handling Strategy
1. **Bind Errors**: Throw `SmppBindException` with error details
2. **Timeout Errors**: Handle gracefully with session cleanup
3. **Network Errors**: Close session and raise events
4. **Parser Errors**: Send error responses and continue processing

### Error Recovery
```mermaid
flowchart TD
    A[Error Occurs] --> B{Error Type}
    B -->|Bind Error| C[Throw SmppBindException]
    B -->|Timeout| D[End Session]
    B -->|Network| E[Close TCP Session]
    B -->|Parser| F[Send Error Response]
    
    C --> G[Session Creation Failed]
    D --> H[Raise SessionClosed Event]
    E --> H
    F --> I[Continue Processing]
```

### Exception Event Handling
```csharp
private void ParserExceptionEventHandler(object sender, ParserExceptionEventArgs e)
{
    EndSession(SmppSessionCloseReason.TcpIpSessionError, e.Exception);
}

private void PduErrorEventHandler(object sender, PDUErrorEventArgs e)
{
    ResponsePDU resp = null;
    
    if (e.Pdu is RequestPDU)
    {
        RequestPDU req = (RequestPDU)e.Pdu;
        resp = req.CreateDefaultResponce();
        resp.Header.ErrorCode = e.Exception.ErrorCode;
    }
    else
    {
        resp = new GenericNack(e.Header, vSmppEncodingService);
        resp.Header.ErrorCode = e.Exception.ErrorCode;
    }
    
    try { SendPduBase(resp); }
    catch {/*silent catch*/}
}
```

## Usage Examples

### Creating and Binding a Session
```csharp
// Create binding information
SessionBindInfo bindInfo = new SessionBindInfo();
bindInfo.SystemID = "your_system_id";
bindInfo.Password = "your_password";
bindInfo.ServerName = "smpp.provider.com";
bindInfo.Port = 2775;
bindInfo.InterfaceVersion = InterfaceVersion.v34;
bindInfo.AddressTon = TypeOfNumber.International;
bindInfo.AddressNpi = NumberingPlanIndicator.ISDN;
bindInfo.SystemType = "SMS";

// Create encoding service
SmppEncodingService encodingService = new SmppEncodingService();

// Bind session
SmppClientSession session = SmppClientSession.Bind(bindInfo, 5000, encodingService);

// Configure session
session.EnquireLinkInterval = 30000; // 30 seconds
session.DefaultResponseTimeout = 5000; // 5 seconds

// Subscribe to events
session.PduReceived += (sender, e) => {
    Console.WriteLine($"Received PDU: {e.Request.Header.CommandType}");
    
    // Handle specific PDU types
    if (e.Request is DeliverSm)
    {
        // Process incoming message
        DeliverSm deliverSm = (DeliverSm)e.Request;
        Console.WriteLine($"Message from: {deliverSm.SourceAddress.Address}");
        
        // Send response
        e.Response = deliverSm.CreateDefaultResponce();
    }
};

session.SessionClosed += (sender, e) => {
    Console.WriteLine($"Session closed: {e.Reason}");
};
```

### Sending PDUs
```csharp
// Send a SubmitSm PDU
SubmitSm submitSm = new SubmitSm(encodingService);
submitSm.ServiceType = "SMS";
submitSm.SourceAddress = new SmppAddress("12345");
submitSm.DestinationAddress = new SmppAddress("1234567890");
submitSm.ShortMessage = "Hello, World!";

try
{
    SubmitSmResp response = (SubmitSmResp)session.SendPdu(submitSm, 10000);
    Console.WriteLine($"Message ID: {response.MessageID}");
}
catch (SmppResponseTimedOutException)
{
    Console.WriteLine("Request timed out");
}
catch (SmppException ex)
{
    Console.WriteLine($"SMPP Error: {ex.ErrorCode}");
}
```

### Asynchronous PDU Sending
```csharp
// Send PDU asynchronously
SubmitSm submitSm = new SubmitSm(encodingService);
// ... configure PDU ...

try
{
    ResponsePDU response = await session.SendPduAsync(submitSm, 10000, cancellationToken);
    Console.WriteLine("PDU sent successfully");
}
catch (SmppResponseTimedOutException)
{
    Console.WriteLine("The SMSC did not respond in time");
}
catch (SmppException ex)
{
    Console.WriteLine($"SMPP Error: {ex.ErrorCode}");
}
```

### Session Management
```csharp
// Check session state
if (session.IsAlive)
{
    Console.WriteLine($"Session state: {session.State}");
    Console.WriteLine($"SMSC ID: {session.SmscID}");
    Console.WriteLine($"System ID: {session.SystemID}");
}

// Gracefully close session
session.EndSession();
```

### Error Handling Example
```csharp
session.SessionClosed += (sender, e) => {
    switch (e.Reason)
    {
        case SmppSessionCloseReason.EndSessionCalled:
            Console.WriteLine("Session closed normally");
            break;
        case SmppSessionCloseReason.EnquireLinkTimeout:
            Console.WriteLine("Keep-alive timeout - connection lost");
            break;
        case SmppSessionCloseReason.TcpIpSessionError:
            Console.WriteLine($"Network error: {e.Exception?.Message}");
            break;
        case SmppSessionCloseReason.UnbindRequested:
            Console.WriteLine("SMSC requested unbind");
            break;
    }
};

session.PduReceived += (sender, e) => {
    try
    {
        // Process PDU
        ProcessPdu(e.Request);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error processing PDU: {ex.Message}");
        
        // Send error response
        if (e.Request.HasResponse)
        {
            e.Response = e.Request.CreateDefaultResponce();
            e.Response.Header.ErrorCode = SmppErrorCode.ESME_RX_P_APPN;
        }
    }
};
```

### Advanced Configuration
```csharp
// Configure for high-volume scenarios
session.DefaultResponseTimeout = 3000; // 3 seconds
session.EnquireLinkInterval = 60000;   // 1 minute

// Configure encoding service
SmppEncodingService customEncoding = new SmppEncodingService(Encoding.UTF8);
session.SmppEncodingService = customEncoding;

// Thread-safe operations
lock (session.SyncRoot)
{
    if (session.IsAlive)
    {
        // Perform thread-safe operations
    }
}
```

The SmppClientSession provides a robust foundation for SMPP communication, handling the complexities of session management, PDU processing, and error recovery while maintaining thread safety and providing comprehensive event notifications for application integration.
