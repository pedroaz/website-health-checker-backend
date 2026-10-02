# Website Health Checker Backend — Summary

A lightweight, single-instance health-monitoring showcase built with ASP.NET Core 10, PostgreSQL, and EF Core. Monitors website health every five minutes, captures results with email notifications via Mailpit, and presents a dashboard interface via Next.js.

## Architecture Overview

```mermaid
graph TB
    subgraph "Frontend Layer"
        Dashboard["📊 Next.js Dashboard<br/>(port 3000)"]
    end
    
    subgraph "Backend Services"
        API["🌐 ASP.NET Core 10 API<br/>(port 8080)<br/>- GET/POST /api/monitors<br/>- PATCH /api/monitors/{id}<br/>- POST /api/monitors/{id}/checks"]
        Scheduler["⏱️ Hosted Scheduler<br/>Runs every 5 seconds<br/>Triggers checks every 5 minutes"]
        Checker["🔍 WebsiteChecker<br/>- HTTP validation<br/>- Redirect following<br/>- DNS pinning<br/>- Timeout handling"]
        Mailer["📧 Email Service<br/>Sends results via SMTP<br/>Independent of health status"]
    end
    
    subgraph "Data Layer"
        DB["🗄️ PostgreSQL<br/>- Monitors<br/>- Check History<br/>- Email Status"]
    end
    
    subgraph "External Services"
        Mailpit["📮 Mailpit<br/>(port 8025)<br/>Email capture & testing"]
        Websites["🌍 Target Websites<br/>HTTP/HTTPS endpoints"]
    end
    
    Dashboard -->|API calls| API
    API -->|Query/Insert| DB
    API -->|Trigger checks| Checker
    Scheduler -->|Poll for due| API
    Checker -->|HTTP requests| Websites
    Mailer -->|SMTP| Mailpit
    API -->|Save results| DB
    API -->|Trigger email| Mailer
    
    style Dashboard fill:#e1f5ff
    style API fill:#fff3e0
    style Scheduler fill:#f3e5f5
    style Checker fill:#e8f5e9
    style Mailer fill:#fce4ec
    style DB fill:#ede7f6
    style Mailpit fill:#fff9c4
    style Websites fill:#eceff1
```

## Data Flow

```mermaid
sequenceDiagram
    participant User
    participant API as ASP.NET Core<br/>API
    participant Checker as WebsiteChecker
    participant DB as PostgreSQL
    participant SMTP as Mailpit
    
    User->>API: POST /api/monitors<br/>{url, email}
    API->>DB: Create monitor
    API->>Checker: Execute first check
    
    Checker->>Checker: Validate URL & DNS
    Checker->>Checker: GET request with<br/>5-redirect limit
    Checker->>Checker: Measure response time<br/>(10s deadline)
    Checker->>DB: Save check result
    Checker->>SMTP: Send result email
    SMTP->>DB: Update email status
    
    API-->>User: Return saved result
    
    Note over Scheduler: Every 5 seconds
    Scheduler->>API: Poll monitors
    Scheduler->>API: Find due monitors<br/>(5 min intervals)
    API->>Checker: Trigger check
    Checker->>DB: Save result & email status
```

## Component Details

### API Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| **GET** | `/api/monitors` | List all monitors with latest health |
| **POST** | `/api/monitors` | Create monitor with first check |
| **GET** | `/api/monitors/{id}` | Get monitor details & last 50 checks |
| **DELETE** | `/api/monitors/{id}` | Delete monitor and history |
| **PATCH** | `/api/monitors/{id}` | Pause/resume monitoring |
| **POST** | `/api/monitors/{id}/checks` | Manual check (immediate) |
| **GET** | `/api/monitors/{id}/checks` | Get check history |
| **GET** | `/health/live` | Liveness probe |
| **GET** | `/health/ready` | Readiness (PostgreSQL required) |

### Database Schema

```mermaid
erDiagram
    MONITORS ||--o{ CHECKS : has
    
    MONITORS {
        guid id PK
        string url
        string email
        bool paused
        timestamp due_at
        timestamp created_at
        timestamp updated_at
    }
    
    CHECKS {
        guid id PK
        guid monitor_id FK
        int status_code
        int response_time_ms
        string health_status
        string email_status
        string error_message
        timestamp checked_at
    }
```

### Validation & Security

```mermaid
graph LR
    Input["User Input<br/>(URL, Email)"]
    URLValidation["✓ URL Format<br/>✓ No Credentials<br/>✓ HTTP/HTTPS only"]
    DNSValidation["✓ DNS Resolution<br/>✓ Pin to resolved IP<br/>✓ Block private IPs"]
    ConnectionValidation["✓ 10s timeout<br/>✓ Follow max 5 redirects<br/>✓ Validate each hop"]
    EmailValidation["✓ Email Format<br/>✓ Standard validation"]
    
    Input --> URLValidation
    URLValidation --> DNSValidation
    DNSValidation --> ConnectionValidation
    EmailValidation
    
    ConnectionValidation --> Accept["✅ Accept for<br/>Monitoring"]
    URLValidation -->|Fail| Reject["❌ HTTP 400<br/>Validation Problem"]
    EmailValidation -->|Fail| Reject
    
    style Reject fill:#ffcdd2
    style Accept fill:#c8e6c9
```

### Scheduling Logic

```mermaid
graph TD
    Start["Scheduler runs<br/>every 5 seconds"]
    Query["Query monitors<br/>where due_at <= now"]
    Lock["Acquire lock for<br/>monitor ID"]
    Check["Execute WebsiteChecker"]
    SaveResult["Save result & email status<br/>to PostgreSQL"]
    Email["Send email via Mailpit"]
    UpdateDue["Set due_at = now + 5min"]
    Unlock["Release lock"]
    
    Start --> Query
    Query -->|Found due monitor| Lock
    Lock -->|Already locked| Skip["Skip<br/>Check in progress"]
    Lock -->|Acquired| Check
    Check --> SaveResult
    SaveResult --> Email
    Email --> UpdateDue
    UpdateDue --> Unlock
    Query -->|No due monitors| Wait["Wait next poll"]
    Unlock --> Wait
    
    style Lock fill:#fff9c4
    style Check fill:#e8f5e9
    style SaveResult fill:#ede7f6
    style Email fill:#fce4ec
```

## Health Check Logic

```mermaid
graph TD
    Start["GET {url}<br/>with 10s deadline"]
    Status{HTTP Status<br/>200-299?}
    Redirect{Follows<br/>redirect?}
    Timeout{Within<br/>timeout?}
    
    Start --> Status
    Status -->|Yes| Timeout
    Status -->|No| Unhealthy["❌ UNHEALTHY"]
    
    Timeout -->|Yes| Success["✅ HEALTHY<br/>Record response time"]
    Timeout -->|No| TimedOut["❌ UNHEALTHY<br/>TIMEOUT"]
    
    Redirect -->|>5 redirects| TooMany["❌ UNHEALTHY<br/>REDIRECT LIMIT"]
    Redirect -->|Valid| Status
    Redirect -->|Invalid target| InvalidTarget["❌ UNHEALTHY<br/>INVALID REDIRECT"]
    
    style Success fill:#c8e6c9
    style Unhealthy fill:#ffcdd2
    style TimedOut fill:#ffcdd2
    style TooMany fill:#ffcdd2
    style InvalidTarget fill:#ffcdd2
```

## Container Architecture

```mermaid
graph TB
    subgraph compose["Docker Compose (openhands-demo)"]
        subgraph backend["Backend Container"]
            dotnet["ASP.NET Core 10<br/>Port 8080<br/>Health checks"]
            efs["EF Core Migrations<br/>Run at startup"]
        end
        
        subgraph frontend["Frontend Container"]
            nextjs["Next.js App Router<br/>Port 3000<br/>Proxies API calls"]
        end
        
        subgraph mailpit["Mailpit Container"]
            smtp["SMTP Server<br/>Internal port 1025"]
            webui["Web UI<br/>Port 8025"]
        end
        
        subgraph postgres["PostgreSQL Container"]
            db["monitors, checks<br/>Named volume"]
        end
    end
    
    dotnet -->|Reads/Writes| db
    dotnet -->|SMTP| smtp
    nextjs -->|API calls| dotnet
    smtp -->|Stores| webui
    
    style dotnet fill:#fff3e0
    style nextjs fill:#e1f5ff
    style smtp fill:#fce4ec
    style db fill:#ede7f6
```

## Environment & Configuration

### Key Environment Variables

```
# API Configuration
ASPNETCORE_ENVIRONMENT=Development|Production
ASPNETCORE_URLS=http://+:8080

# Database
ConnectionStrings__MonitorDb=Server=postgres;...

# SMTP
SmtpOptions__Server=mailpit
SmtpOptions__Port=1025

# Security (Development only)
AllowDemoTarget=true  # Enables http://demo-target:8080 exception

# Ports
MAILPIT_PORT=8025  # Inbox UI
```

### Development Workflow

```mermaid
graph LR
    Code["Modify C# code"]
    Build["dotnet build"]
    FastTest["dotnet test<br/>--filter Category!=Integration"]
    Compose["docker compose up<br/>--build"]
    IntegrationTest["./scripts/test.sh<br/>Full isolated suite"]
    Format["dotnet format<br/>--verify-no-changes"]
    
    Code --> Build
    Build --> FastTest
    FastTest -->|Local dev| Compose
    Compose --> IntegrationTest
    Code --> Format
    Format -->|Pre-commit| Build
    
    style Build fill:#fff3e0
    style FastTest fill:#e8f5e9
    style IntegrationTest fill:#c8e6c9
    style Format fill:#f3e5f5
```

## Key Design Decisions

### Single-Instance Scheduler
- In-process scheduler using hosted service
- Lock-based mutual exclusion for checks
- Prevents overlapping check execution
- Not suitable for distributed deployments

### Email Independence
- Email status (Pending/Sent/Failed) tracked separately from health
- Mailpit failure does not mark websites unhealthy
- No delivery retries — design limitation for showcase

### DNS Pinning & Connection Validation
- Validates DNS on every HTTP hop
- Pins connections to resolved IP addresses
- Blocks private/reserved network ranges
- Development exception for `demo-target` origin only

### 10-Second HTTP Deadline
- Strict overall timeout per check
- Prevents slow checks from blocking scheduler
- Measures time-to-response-headers only
- Bodies not downloaded

## Security Boundaries

| Check | Purpose |
|-------|---------|
| No URL credentials | Prevents accidental password exposure in logs |
| Private network rejection | Prevents SSRF exploitation (except demo target) |
| DNS validation on each hop | Prevents DNS rebinding attacks |
| TLS certificate validation | Blocks MITM attacks |
| No logging of URLs/emails | Reduces sensitive data exposure |

## Deployment Considerations

### Single-User, Local Showcase
- No authentication/authorization
- No tenant isolation
- No rate limits or quotas
- Unauthenticated API access

### Production Gaps
- Distributed scheduling (current: single instance only)
- Durable notification retries (current: fire-and-forget)
- Backup/disaster recovery (named volumes only)
- Operational alerting & monitoring
- Secret management & HTTPS termination
- Bounded retention policies
- Comprehensive SSRF & network isolation review

See [AGENTS.md](AGENTS.md) and [PLAN.md](PLAN.md) for implementation details and roadmap.
