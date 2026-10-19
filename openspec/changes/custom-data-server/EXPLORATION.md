# Custom Data Server Feature - Exploration

**Change Type:** Feature

**Date:** 2024-12-19

## Executive Summary

This exploration investigates the requirements and feasibility of implementing a Custom Data Server feature for the Syncfusion Pivot Table. The feature is inspired by the architecture and capabilities of the Flexmonster Data Server and will enable advanced data management, multi-source connectivity, and server-side optimizations for enterprise-grade pivot table deployments.

## Current State Analysis

### Existing Architecture

**Repository Context:** `/home/user/server-side-pivot-engine-for-pivot-table`

**Key Components:**
1. **PivotController (ASP.NET Core)** - Located at `PivotController/`
   - Language: C#, Framework: ASP.NET Core (net10.0)
   - Main endpoint: `PivotController.cs` - handles pivot data requests
   - Serves single data source via `GetData()` method
   - Currently supports: JSON, CSV, DataTable, and Collections
   - Caching mechanism: Memory cache with 60-minute TTL

2. **Data Source Layer** - Located at `PivotController/DataSource/`
   - `DataSource.cs` contains:
     - `PivotCSVData` - CSV file/URL loading
     - `PivotJSONData` - JSON file/URL loading
     - `PivotViewData` - In-memory collection data
     - `BusinessObjectsDataView` - DataTable support
   - Current limitation: Loads entire data into memory during initialization

3. **Client Sample** - Located at `Sample/`
   - HTML/JavaScript frontend for testing
   - Communicates with pivot endpoint

4. **Pivot Engine** - Syncfusion library `Syncfusion.Pivot.Engine`
   - Performs server-side calculations, filtering, sorting
   - Reduces client-side network traffic
   - Supports virtual scrolling

### Current Data Flow

```
Client Request
  ↓
PivotController.Post()
  ├→ GetData() - Load entire source into memory
  ├→ PivotEngine - Process and calculate
  └→ Return processed results (JSON)
```

### Existing Pivot Endpoint Contract

**Request Actions Supported:**
- `fetchFieldMembers` - Get field values
- `fetchRawData` - Get raw dataset
- `onExcelExport` / `onCsvExport` - Export data
- `onPivotExcelExport` / `onPivotCsvExport` - Export pivot results
- Default action - Get pivot values

**Response Format:** Serialized `EngineProperties` via `PivotEngine.GetSerializedPivotValues()`

## Requirement Analysis

### Flexmonster Data Server Capabilities (Reference)

Based on industry standards for data server architecture, the Flexmonster Data Server provides:

1. **General Settings**
   - Server identifier and metadata
   - Performance tuning options
   - Connection pooling settings

2. **Data Storage Settings**
   - Multiple data source types (Database, API, File System, Cloud Storage)
   - Data caching strategies
   - Incremental load support

3. **Database Query Settings**
   - Direct SQL query support
   - Query optimization and parameterization
   - Connection string management
   - Query result set limits and pagination

4. **HTTPS Settings**
   - SSL/TLS certificate configuration
   - Encryption for data in transit
   - Certificate validation policies

5. **Security Settings**
   - Authentication mechanisms (OAuth, Basic, API Key)
   - Authorization and role-based access control (RBAC)
   - Data masking and field-level security
   - Audit logging

6. **Report Sharing Settings**
   - Report template management
   - Scheduled report generation
   - Report distribution and notifications

### Integration Points with Syncfusion Pivot Table

**Current Adapter Points:**
- `PivotController/Controllers/PivotController.cs` - Main request handler
- `PivotController/DataSource/DataSource.cs` - Data loading layer
- `Program.cs` - Service configuration

**Observable Request/Response Pattern:**
- Request body contains `FetchData` object with:
  - Action type (string)
  - Member name (for field members)
  - Configuration hash
  - Grouping information
  - Export properties
- Response is serialized engine properties (JSON)

## Scope Definition

### What Will Be Implemented

1. **Custom Data Server Configuration Layer**
   - Settings classes for all six categories
   - Configuration management (load, save, validate)
   - Default configurations with overrides

2. **Data Adapter Pattern**
   - Abstract base for pluggable data sources
   - Implementations for common types (Database, REST API, File-based)
   - Query builder for parameterized access

3. **Security & Authentication Middleware**
   - Request validation and authentication
   - Authorization checks based on user roles
   - Field-level access control

4. **Query Optimization Engine**
   - Lazy loading and pagination support
   - Query planning and caching
   - Performance metrics collection

5. **Server-side Aggregation**
   - Server-driven grouping and filtering
   - Reduced data transfer
   - Enhanced performance for large datasets

### What Will NOT Be Implemented (Out of Scope)

- Standalone data server deployment (integrated into existing controller)
- UI configuration tools (API-only)
- Distributed caching (will use existing Memory Cache)
- Real-time data streaming
- Data warehouse analytics features beyond pivot table support

## Constraints & Considerations

### Technical Constraints

1. **Backward Compatibility**
   - Must maintain existing pivot endpoint contract
   - Existing sample applications must continue working
   - Cannot break current data loading mechanisms

2. **Performance**
   - Must support datasets with millions of rows
   - Lazy loading required for large result sets
   - Query optimization must reduce processing time

3. **Platform Constraints**
   - ASP.NET Core (net10.0) - use only compatible libraries
   - Cross-platform considerations
   - No legacy .NET Framework code

4. **Integration Constraints**
   - Must work with Syncfusion Pivot Engine
   - Response format must remain compatible with client expectations
   - Caching strategy must integrate with existing Memory Cache

### Security Constraints

1. No hardcoded credentials in configuration files
2. HTTPS enforcement for production
3. Audit logging for all data access
4. Input validation to prevent injection attacks
5. Principle of least privilege for database access

### Operational Constraints

1. Configuration must be environment-specific (Development, Staging, Production)
2. No data loss during configuration changes
3. Graceful degradation if data source becomes unavailable

## Questions for Clarification

### Architecture Questions

1. **Multi-Tenancy:** Should the Custom Data Server support multi-tenant deployments where different clients have different data sources?
   - *Implication:* Affects configuration storage and security context isolation

2. **Real-time Data Sync:** Should the server support real-time data updates, or should it support query-time data freshness?
   - *Implication:* Determines caching and invalidation strategy

3. **Query Language:** Should custom SQL queries be directly exposable in the API, or only through predefined queries?
   - *Implication:* Security and usability tradeoff

### Feature Scope Questions

4. **Report Sharing:** Should report sharing include scheduled generation and email delivery?
   - *Implication:* Adds background job infrastructure requirement

5. **Field-Level Security:** Should the system support column-level masking (e.g., redacting sensitive data)?
   - *Implication:* Requires field metadata and access policy framework

6. **Data Persistence:** Should the Custom Data Server cache query results locally, or always query the source?
   - *Implication:* Affects data freshness requirements and storage needs

### Integration Questions

7. **Authentication Integration:** Should the system integrate with external identity providers (Azure AD, OAuth), or is local API key sufficient?
   - *Implication:* Determines identity infrastructure

8. **Existing Data Sources:** Should the feature support gradual migration from existing data sources, or require full cutover?
   - *Implication:* Migration path and compatibility strategy

### Performance Questions

9. **Pagination:** What is the maximum recommended page size, and should it be configurable?
   - *Implication:* Memory usage and network transfer limits

10. **Query Timeout:** What should be the default query timeout, and should it be configurable per data source?
    - *Implication:* User experience and resource protection

## Recommended Approach

### Phase 1: Foundation (This Specification)
- Define complete configuration model for all six categories
- Create abstract data adapter pattern
- Design security framework
- Specify request/response contracts

### Phase 2: Implementation
- Implement configuration layer with validation
- Build core data adapters (Database, REST API)
- Add authentication and authorization middleware
- Integrate with existing PivotController
- Comprehensive testing and documentation

### Phase 3: Enhancement (Future)
- Additional adapters (Cloud Storage, GraphQL)
- Advanced analytics features
- Performance optimization and monitoring
- CLI and configuration UI tools

## Success Criteria

1. **Specification completeness:** All six configuration categories are fully documented with examples
2. **Backward compatibility:** Existing sample applications continue working without modifications
3. **Extensibility:** New data source types can be added through adapter pattern
4. **Security:** All authenticated requests are logged; unauthorized access is prevented
5. **Performance:** Large dataset queries complete within acceptable timeframes
6. **Documentation:** Implementation guide and API reference are comprehensive

## References

- **Project Structure:** `/home/user/server-side-pivot-engine-for-pivot-table`
- **Main Controller:** `PivotController/Controllers/PivotController.cs`
- **Data Source:** `PivotController/DataSource/DataSource.cs`
- **Configuration:** `PivotController/appsettings.json`
- **Syncfusion Reference:** Syncfusion.Pivot.Engine (used in existing code)

## Next Steps

1. User review and approval of this exploration
2. Clarification questions to be answered
3. Proceed to Design phase with answered clarifications
4. Create detailed technical specifications
5. Define implementation tasks and acceptance criteria
