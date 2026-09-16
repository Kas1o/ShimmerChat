# MCP Protocol Reference for a C#/.NET Client Implementer
## Revision `2025-11-25` (last handshake-era revision) and Revision `2024-11-05` (original)

All facts below were read from the local checkout only (no web access). Paths are relative to the
checkout root `Z:\Project\ShimmerChat\.mcp-spec-research\`.

Authoritative sources used:

| Kind | Path |
|---|---|
| Prose, 2025-11-25 | `docs/specification/2025-11-25/**` |
| Prose, 2025-06-18 | `docs/specification/2025-06-18/**` |
| Prose, 2025-03-26 | `docs/specification/2025-03-26/**` |
| Prose, 2024-11-05 | `docs/specification/2024-11-05/**` |
| Prose, 2026-07-28 | `docs/specification/2026-07-28/**` |
| Schema (source of truth), 2025-11-25 | `schema/2025-11-25/schema.ts` (2582 lines), `schema/2025-11-25/schema.json` |
| Schema (source of truth), 2025-06-18 | `schema/2025-06-18/schema.ts` (1613 lines) |
| Schema (source of truth), 2024-11-05 | `schema/2024-11-05/schema.ts` (1117 lines) |
| Version catalogue | `docs/docs.json` (`navigation.tabs[].versions[]`) |
| Release dates | `blog/content/posts/2025-09-26-mcp-next-version-update.md`, `blog/content/posts/2025-11-25-first-mcp-anniversary.md`, `blog/content/posts/2026-07-28-spec-ga/index.md` |

---

# PART 0 — Corrections to premises in the request (read this first)

Three of the premises in the task description are **not** supported by the local checkout. They must
not be coded against as if normative.

1. **"POST responses return `202 Accepted`" for the 2024-11-05 HTTP+SSE transport.**
   The string `202` / `Accepted` does **not** occur anywhere in
   `docs/specification/2024-11-05/**`. The 202-Accepted rule exists only in the *Streamable HTTP*
   transport, introduced in `2025-03-26`
   (`docs/specification/2025-03-26/basic/transports.mdx` line 99, and repeated in 2025-06-18
   line 93, 2025-11-25 line 97) and only for the case "if the input is a JSON-RPC *response* or
   *notification*". See §2.1.4.
2. **"whether the `endpoint` event may be relative".**
   The words `relative`, `relative URI`, `relative URL` do **not** occur anywhere in the checkout
   in connection with SSE transports (repo-wide grep). 2024-11-05 says only "an `endpoint` event
   containing a URI". See §2.1.5.
3. **"whether the server can send other events first".**
   2024-11-05 contains **no** ordering rule for the `endpoint` event. The "as the first event"
   wording appears only later, and only inside the *backwards-compatibility probe* clause of the
   Streamable HTTP transport (2025-03-26 line 274, 2025-06-18 line 283, 2025-11-25 line 306,
   2026-07-28 line 734). See §2.1.5.
4. **`tasks/status` does not exist as an RPC method.** There is no `tasks/status` request in either
   the prose or the schema. What exists is the **notification** `notifications/tasks/status`.
   See §1.10.6.
5. **`ElicitResult` is the schema type name, not `ElicitationResult`.** The task text says
   "`ElicitationResult` action values"; the actual interface is `ElicitResult`.
   See §1.9.7.
6. **2025-11-25 is the last handshake-era revision — confirmed.** `initialize` and
   `notifications/initialized` are still present in `2025-11-25`; they are removed only in
   `2026-07-28` (`docs/specification/2026-07-28/changelog.mdx` major change 2).
7. **Header casing changed in 2025-11-25**: `Mcp-Session-Id` (2025-03-26 and 2025-06-18) →
   `MCP-Session-Id` (2025-11-25). Because HTTP header names are case-insensitive this is
   cosmetic on the wire, but the spec text differs. See §1.13.5.

---

# PART 1 — Revision `2025-11-25`

## 1.0 Every change from `2025-06-18`, verbatim

Source: `docs/specification/2025-11-25/changelog.mdx`

> This document lists changes made to the Model Context Protocol (MCP) specification since
> the previous revision, [2025-06-18](/specification/2025-06-18).

### Major changes

1. Enhance authorization server discovery with support for [OpenID Connect Discovery 1.0](https://openid.net/specs/openid-connect-discovery-1_0.html). (PR #797)
2. Allow servers to expose icons as additional metadata for tools, resources, resource templates, and prompts (SEP-973).
3. Enhance authorization flows with incremental scope consent via `WWW-Authenticate` (SEP-835)
4. Provide guidance on tool names (SEP-986)
5. Update `ElicitResult` and `EnumSchema` to use a more standards-based approach and support titled, untitled, single-select, and multi-select enums (SEP-1330).
6. Added support for URL mode elicitation (SEP-1036)
7. Add tool calling support to sampling via `tools` and `toolChoice` parameters (SEP-1577)
8. Add support for OAuth Client ID Metadata Documents as a recommended client registration mechanism (SEP-991, PR #1296)
9. Add experimental support for tasks to enable tracking durable requests with polling and deferred result retrieval (SEP-1686).

### Minor changes

1. Clarify that servers using stdio transport may use stderr for all types of logging, not just error messages (PR #670).
2. Add optional `description` field to `Implementation` interface to align with MCP registry server.json format and provide human-readable context during initialization.
3. Clarify that servers must respond with HTTP 403 Forbidden for invalid Origin headers in Streamable HTTP transport. (PR #1439)
4. Updated the Security Best Practices guidance.
5. Clarify that input validation errors should be returned as Tool Execution Errors rather than Protocol Errors to enable model self-correction (SEP-1303).
6. Support polling SSE streams by allowing servers to disconnect at will (SEP-1699).
7. Clarify SEP-1699: GET streams support polling, resumption always via GET regardless of stream origin, event IDs should encode stream identity, disconnection includes server-initiated closure (Issue #1847).
8. Align OAuth 2.0 Protected Resource Metadata discovery with RFC 9728, making `WWW-Authenticate` header optional with fallback to `.well-known` endpoint (SEP-985).
9. Add support for default values in all primitive types (string, number, enum) for elicitation schemas (SEP-1034).
10. Establish JSON Schema 2020-12 as the default dialect for MCP schema definitions (SEP-1613).

### Other schema changes

1. Decouple request payloads from RPC method definitions into standalone parameter schemas. (SEP-1319, PR #1284)

### Governance and process updates

1. Formalize Model Context Protocol governance structure (SEP-932).
2. Establish shared communication practices and guidelines for the MCP community (SEP-994).
3. Formalize Working Groups and Interest Groups in MCP governance (SEP-1302).
4. Establish SDK tiering system with clear requirements for feature support and maintenance commitments (SEP-1730).

> Full changelog: `compare/2025-06-18...2025-11-25` on GitHub.

### 1.0.1 Changes present in the schema/prose but NOT listed in the changelog

(Found by diffing `schema/2025-06-18/schema.ts` against `schema/2025-11-25/schema.ts`.)

| Change | Evidence |
|---|---|
| `Icon` and `Icons` interfaces added; `Implementation`, `Tool`, `Resource`, `ResourceTemplate`, `Prompt` now `extends BaseMetadata, Icons` | 2025-11-25 schema.ts lines 464, 508, 550, 802, 845, 984, 1249; absent in 2025-06-18 (`grep` for `interface Icon` matches only 2025-11-25/2026-07-28/draft) |
| `Implementation.websiteUrl?: string` added | 2025-11-25 schema.ts line 567; absent in 2025-06-18 |
| `Implementation.description?: string` added | 2025-11-25 schema.ts line 560 (changelog minor #2) |
| `JSONRPCResponse` split into `JSONRPCResultResponse \| JSONRPCErrorResponse`; `JSONRPCError` renamed `JSONRPCErrorResponse`; `JSONRPCErrorResponse.id` becomes optional (`id?: RequestId`) | 2025-11-25 schema.ts lines 148–170, 159–163 |
| All RPC interfaces now `extends JSONRPCRequest` (were `extends Request`) and payloads moved to standalone `*RequestParams` types (`RequestParams`, `PaginatedRequestParams`, `CallToolRequestParams`, `ReadResourceRequestParams`, `SubscribeRequestParams`, `UnsubscribeRequestParams`, `GetPromptRequestParams`, `SetLevelRequestParams`, `CompleteRequestParams`, `ElicitRequestFormParams`, `ElicitRequestURLParams`, `TaskAugmentedRequestParams`, `ProgressNotificationParams`, `LoggingMessageNotificationParams`, `ResourceUpdatedNotificationParams`, `CancelledNotificationParams`, `RootsListChangedNotification` etc.) | SEP-1319; 2025-11-25 schema.ts lines 35–98, 215, 255, 588, 627, 691, 706, 743, 761, 945, 1137, 1507, 1529, 1578 |
| `CancelledNotificationParams.requestId` becomes **optional** (`requestId?: RequestId`) with new rules for tasks | 2025-11-25 schema.ts lines 215–229 |
| `PingRequest.params?: RequestParams` added | 2025-11-25 schema.ts line 578 |
| `InitializedNotification.params?: NotificationParams` added | 2025-11-25 schema.ts line 302 |
| `Annotations.lastModified` — carried over from 2025-06-18 (added there) | 2025-06-18 schema.ts line 1127 |
| `Annotated` base interface of 2024-11-05 is gone; `Annotations` is a standalone named type referenced per-field | 2024-11-05 schema.ts line 818 vs 2025-11-25 line 1705 |
| `Tool.inputSchema.$schema?` and `Tool.outputSchema.$schema?` added | 2025-11-25 schema.ts lines 1261, 1280 |
| `CompletionRequest.ref` type renamed `ResourceReference` → `ResourceTemplateReference` (2024-11-05 → 2025-06-18, kept in 2025-11-25) | 2024-11-05 line 997 vs 2025-11-25 line 2064 |
| `SamplingMessage.content` widened to `SamplingMessageContentBlock \| SamplingMessageContentBlock[]` | 2025-11-25 schema.ts line 1683 |
| `ToolUseContent`, `ToolResultContent`, `ToolChoice`, `ToolExecution`, `SamplingMessageContentBlock` types added | 2025-11-25 schema.ts lines 1629, 1693, 1834, 1868, 1229 |
| `CreateMessageResult.stopReason` gains the value `"toolUse"` | 2025-11-25 schema.ts line 1673 |
| Tasks: `Task`, `TaskStatus`, `TaskMetadata`, `RelatedTaskMetadata`, `TaskAugmentedRequestParams`, `CreateTaskResult`, `GetTaskRequest`, `GetTaskResult`, `GetTaskPayloadRequest`, `GetTaskPayloadResult`, `CancelTaskRequest`, `CancelTaskResult`, `ListTasksRequest`, `ListTasksResult`, `TaskStatusNotification`, `TaskStatusNotificationParams` | 2025-11-25 schema.ts lines 1299–1498 |
| Elicitation: `ElicitRequest`, `ElicitRequestFormParams`, `ElicitRequestURLParams`, `ElicitRequestParams`, `ElicitResult`, `ElicitationCompleteNotification`, `StringSchema`, `NumberSchema`, `BooleanSchema`, `UntitledSingleSelectEnumSchema`, `TitledSingleSelectEnumSchema`, `SingleSelectEnumSchema`, `UntitledMultiSelectEnumSchema`, `TitledMultiSelectEnumSchema`, `MultiSelectEnumSchema`, `LegacyTitledEnumSchema`, `EnumSchema`, `PrimitiveSchemaDefinition` | 2025-11-25 schema.ts lines 2150–2501 |
| `URL_ELICITATION_REQUIRED = -32042` and `URLElicitationRequiredError` | 2025-11-25 schema.ts lines 181, 188–199 |
| `ICONS`: `resources/templates/list` accepts **no params at all** in the prose example (2025-11-25 `server/resources.mdx` line 172–180 has no `cursor`) | prose only |

---

## 1.1 Sources for Part 1

| Section | File |
|---|---|
| Changelog | `docs/specification/2025-11-25/changelog.mdx` |
| Lifecycle / initialize | `docs/specification/2025-11-25/basic/lifecycle.mdx` |
| Base protocol / `_meta` / `icons` / JSON Schema | `docs/specification/2025-11-25/basic/index.mdx` |
| Transports | `docs/specification/2025-11-25/basic/transports.mdx` |
| Tools | `docs/specification/2025-11-25/server/tools.mdx` |
| Resources | `docs/specification/2025-11-25/server/resources.mdx` |
| Prompts | `docs/specification/2025-11-25/server/prompts.mdx` |
| Elicitation | `docs/specification/2025-11-25/client/elicitation.mdx` |
| Sampling | `docs/specification/2025-11-25/client/sampling.mdx` |
| Roots | `docs/specification/2025-11-25/client/roots.mdx` |
| Tasks | `docs/specification/2025-11-25/basic/utilities/tasks.mdx` |
| Schema (TS, authoritative) | `schema/2025-11-25/schema.ts` |
| Schema (JSON, generated) | `schema/2025-11-25/schema.json` |
| Schema reference (generated MDX) | `docs/specification/2025-11-25/schema.mdx` |

---

## 1.2 `initialize`, `InitializeResult`, `notifications/initialized`

Still present in 2025-11-25. Source: `schema/2025-11-25/schema.ts` lines 249–303;
`docs/specification/2025-11-25/basic/lifecycle.mdx`.

```typescript
export interface InitializeRequestParams extends RequestParams {
  protocolVersion: string;
  capabilities: ClientCapabilities;
  clientInfo: Implementation;
}

export interface InitializeRequest extends JSONRPCRequest {
  method: "initialize";
  params: InitializeRequestParams;
}

export interface InitializeResult extends Result {
  protocolVersion: string;
  capabilities: ServerCapabilities;
  serverInfo: Implementation;
  instructions?: string;
}

export interface InitializedNotification extends JSONRPCNotification {
  method: "notifications/initialized";
  params?: NotificationParams;
}
```

JSON-Schema required sets (`schema/2025-11-25/schema.json` `$defs`):

| Definition | `required` |
|---|---|
| `InitializeRequestParams` | `capabilities`, `clientInfo`, `protocolVersion` |
| `InitializeResult` | `capabilities`, `protocolVersion`, `serverInfo` |
| `InitializedNotification` | `jsonrpc`, `method` (**not** `params`) |
| `RequestParams` | (none; only optional `_meta`) |
| `Result` | (none; `_meta` optional, `additionalProperties: {}`) |

### 1.2.1 Wire example (from `basic/lifecycle.mdx` lines 53–155)

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "initialize",
  "params": {
    "protocolVersion": "2025-11-25",
    "capabilities": {
      "roots": { "listChanged": true },
      "sampling": {},
      "elicitation": { "form": {}, "url": {} },
      "tasks": {
        "requests": {
          "elicitation": { "create": {} },
          "sampling": { "createMessage": {} }
        }
      }
    },
    "clientInfo": {
      "name": "ExampleClient",
      "title": "Example Client Display Name",
      "version": "1.0.0",
      "description": "An example MCP client application",
      "icons": [
        { "src": "https://example.com/icon.png", "mimeType": "image/png", "sizes": ["48x48"] }
      ],
      "websiteUrl": "https://example.com"
    }
  }
}
```

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "protocolVersion": "2025-11-25",
    "capabilities": {
      "logging": {},
      "prompts": { "listChanged": true },
      "resources": { "subscribe": true, "listChanged": true },
      "tools": { "listChanged": true },
      "tasks": { "list": {}, "cancel": {}, "requests": { "tools": { "call": {} } } }
    },
    "serverInfo": {
      "name": "ExampleServer",
      "title": "Example Server Display Name",
      "version": "1.0.0",
      "description": "An example MCP server providing tools and resources",
      "icons": [
        { "src": "https://example.com/server-icon.svg", "mimeType": "image/svg+xml", "sizes": ["any"] }
      ],
      "websiteUrl": "https://example.com/server"
    },
    "instructions": "Optional instructions for the client"
  }
}
```

```json
{ "jsonrpc": "2.0", "method": "notifications/initialized" }
```

### 1.2.2 Lifecycle rules (verbatim highlights, `basic/lifecycle.mdx`)

- "The initialization phase **MUST** be the first interaction between client and server."
- "The client **MUST** initiate this phase by sending an `initialize` request containing: Protocol version supported; Client capabilities; Client implementation information"
- "After successful initialization, the client **MUST** send an `initialized` notification to indicate it is ready to begin normal operations"
- "The client **SHOULD NOT** send requests other than pings before the server has responded to the `initialize` request."
- "The server **SHOULD NOT** send requests other than pings and logging before receiving the `initialized` notification."
- Version negotiation: "If the server supports the requested protocol version, it **MUST** respond with the same version. Otherwise, the server **MUST** respond with another protocol version it supports. This **SHOULD** be the _latest_ version supported by the server." / "If the client does not support the version in the server's response, it **SHOULD** disconnect."
- Operation phase: "Both parties **MUST**: Respect the negotiated protocol version; Only use capabilities that were successfully negotiated".
  History: `2024-11-05` (`basic/lifecycle.mdx` line 162) and `2025-03-26` (line 168) both said
  "Both parties **SHOULD**"; the MUST wording was introduced by `2025-06-18`
  (`basic/lifecycle.mdx` line 175; changelog 2025-06-18 major change 9: "Change **SHOULD** to
  **MUST** in Lifecycle Operation") and is carried unchanged into `2025-11-25` (line 217).
- Shutdown: no dedicated messages; stdio → close stdin, wait, `SIGTERM`, `SIGKILL`; HTTP → close the connection(s).
- Timeouts: "Implementations **SHOULD** establish timeouts for all sent requests … the sender **SHOULD** issue a cancellation notification for that request and stop waiting for a response." Implementations **MAY** reset the timeout clock on `notifications/progress`; "**SHOULD** always enforce a maximum timeout".
- Init error example (`basic/lifecycle.mdx` lines 273–285):

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "error": {
    "code": -32602,
    "message": "Unsupported protocol version",
    "data": { "supported": ["2024-11-05"], "requested": "1.0.0" }
  }
}
```

---

## 1.3 Capabilities

Source: `schema/2025-11-25/schema.ts` lines 305–457.

```typescript
export interface ClientCapabilities {
  experimental?: { [key: string]: object };
  roots?: {
    listChanged?: boolean;
  };
  sampling?: {
    context?: object;
    tools?: object;
  };
  elicitation?: { form?: object; url?: object };

  tasks?: {
    list?: object;
    cancel?: object;
    requests?: {
      sampling?: {
        createMessage?: object;
      };
      elicitation?: {
        create?: object;
      };
    };
  };
}

export interface ServerCapabilities {
  experimental?: { [key: string]: object };
  logging?: object;
  completions?: object;
  prompts?: {
    listChanged?: boolean;
  };
  resources?: {
    subscribe?: boolean;
    listChanged?: boolean;
  };
  tools?: {
    listChanged?: boolean;
  };
  tasks?: {
    list?: object;
    cancel?: object;
    requests?: {
      tools?: {
        call?: object;
      };
    };
  };
}
```

### 1.3.1 Capability field table

| Side | Capability | Type | Meaning in 2025-11-25 |
|---|---|---|---|
| Client | `experimental` | `{ [key: string]: object }` | non-standard capabilities |
| Client | `roots.listChanged` | `boolean` | client emits `notifications/roots/list_changed` |
| Client | `sampling.context` | `object` | client supports `includeContext` non-`"none"` (soft-deprecated) |
| Client | `sampling.tools` | `object` | client supports `tools` / `toolChoice` in `sampling/createMessage` |
| Client | `elicitation.form` | `object` | form mode `elicitation/create` |
| Client | `elicitation.url` | `object` | URL mode `elicitation/create` |
| Client | `tasks.list` | `object` | client supports `tasks/list` |
| Client | `tasks.cancel` | `object` | client supports `tasks/cancel` |
| Client | `tasks.requests.sampling.createMessage` | `object` | client accepts task-augmented `sampling/createMessage` |
| Client | `tasks.requests.elicitation.create` | `object` | client accepts task-augmented `elicitation/create` |
| Server | `experimental` | `{ [key: string]: object }` | non-standard capabilities |
| Server | `logging` | `object` | server emits `notifications/message` |
| Server | `completions` | `object` | server supports `completion/complete` |
| Server | `prompts.listChanged` | `boolean` | server emits `notifications/prompts/list_changed` |
| Server | `resources.subscribe` | `boolean` | `resources/subscribe` / `resources/unsubscribe` |
| Server | `resources.listChanged` | `boolean` | server emits `notifications/resources/list_changed` |
| Server | `tools.listChanged` | `boolean` | server emits `notifications/tools/list_changed` |
| Server | `tasks.list` | `object` | server supports `tasks/list` |
| Server | `tasks.cancel` | `object` | server supports `tasks/cancel` |
| Server | `tasks.requests.tools.call` | `object` | server accepts task-augmented `tools/call` |

Elicitation capability semantics (`client/elicitation.mdx` lines 51–77, verbatim):

> For backwards compatibility, an empty capabilities object is equivalent to declaring support for `form` mode only:
> ```jsonc
> { "capabilities": { "elicitation": {}, // Equivalent to { "form": {} } } }
> ```
> Clients declaring the `elicitation` capability **MUST** support at least one mode (`form` or `url`).
> Servers **MUST NOT** send elicitation requests with modes that are not supported by the client.

Sampling capability (`client/sampling.mdx` lines 44–87): `sampling: {}` = basic; `sampling: { "tools": {} }` = tool use;
`sampling: { "context": {} }` = context inclusion (soft-deprecated).

---

## 1.4 `Implementation`, `BaseMetadata`, `Icon`, `Icons`, `Annotations`

Source: `schema/2025-11-25/schema.ts` lines 459–568, 1700–1735; `docs/specification/2025-11-25/basic/index.mdx` lines 217–267.

```typescript
export interface Icon {
  /** @format uri */
  src: string;
  mimeType?: string;
  sizes?: string[];
  theme?: "light" | "dark";
}

export interface Icons {
  icons?: Icon[];
}

export interface BaseMetadata {
  name: string;
  title?: string;
}

export interface Implementation extends BaseMetadata, Icons {
  version: string;
  description?: string;
  /** @format uri */
  websiteUrl?: string;
}

export interface Annotations {
  audience?: Role[];
  priority?: number;      // 0..1, @minimum 0 @maximum 1
  lastModified?: string;  // ISO 8601, e.g. "2025-01-12T15:00:58Z"
}

export type Role = "user" | "assistant";
```

JSON-Schema shapes:

| Definition | `properties` | `required` |
|---|---|---|
| `BaseMetadata` | `name`, `title` | `name` |
| `Implementation` | `description`, `icons`, `name`, `title`, `version`, `websiteUrl` | `name`, `version` |
| `Icon` | `mimeType`, `sizes`, `src`, `theme` | `src` |
| `Icons` | `icons` | (none) |
| `Annotations` | `audience`, `lastModified`, `priority` | (none) |

`Icon.theme` enum: `"light"` \| `"dark"`. `Icon.sizes` entries are `"WxH"` strings (e.g. `"48x48"`, `"96x96"`) or `"any"`.

`_meta` on `Icon`/`Implementation`: none. `_meta` appears on `Resource`, `ResourceTemplate`,
`ResourceContents`, `Prompt`, `Tool`, `TextContent`, `ImageContent`, `AudioContent`,
`EmbeddedResource`, `ToolUseContent`, `ToolResultContent`, `Root`, `SamplingMessage`, plus
`RequestParams`, `NotificationParams`, `Result`.

`basic/index.mdx` icon rules (verbatim requirements):

- Clients that support rendering icons **MUST** support `image/png` and `image/jpeg` (and `image/jpg`).
- Clients **SHOULD** also support `image/svg+xml` and `image/webp`.
- "Ensure that the icon URI is either a HTTPS or `data:` URI. Clients **MUST** reject icon URIs that use unsafe schemes and redirects, such as `javascript:`, `file:`, `ftp:`, `ws:`, or local app URI schemes." "Disallow scheme changes and redirects to hosts on different origins."
- "Fetch icons without credentials. Do not send cookies, `Authorization` headers, or client credentials."
- "Verify that icon URIs are from the same origin as the server."
- Icons can be attached to `Implementation`, `Tool`, `Prompt`, `Resource` (and `ResourceTemplate` — added by schema).

---

## 1.5 Tools

Source: `schema/2025-11-25/schema.ts` lines 1080–1297; `docs/specification/2025-11-25/server/tools.mdx`.

### 1.5.1 Types

```typescript
export interface ListToolsRequest extends PaginatedRequest {
  method: "tools/list";
}

export interface ListToolsResult extends PaginatedResult {
  tools: Tool[];
}

export interface Tool extends BaseMetadata, Icons {
  description?: string;

  inputSchema: {
    $schema?: string;
    type: "object";
    properties?: { [key: string]: object };
    required?: string[];
  };

  execution?: ToolExecution;

  outputSchema?: {
    $schema?: string;
    type: "object";
    properties?: { [key: string]: object };
    required?: string[];
  };

  annotations?: ToolAnnotations;

  _meta?: { [key: string]: unknown };
}

export interface ToolExecution {
  taskSupport?: "forbidden" | "optional" | "required";
}

export interface ToolAnnotations {
  title?: string;
  readOnlyHint?: boolean;      // Default: false
  destructiveHint?: boolean;   // Default: true
  idempotentHint?: boolean;    // Default: false
  openWorldHint?: boolean;     // Default: true
}

export interface CallToolRequestParams extends TaskAugmentedRequestParams {
  name: string;
  arguments?: { [key: string]: unknown };
}

export interface CallToolRequest extends JSONRPCRequest {
  method: "tools/call";
  params: CallToolRequestParams;
}

export interface CallToolResult extends Result {
  content: ContentBlock[];
  structuredContent?: { [key: string]: unknown };
  isError?: boolean;
}

export interface ToolListChangedNotification extends JSONRPCNotification {
  method: "notifications/tools/list_changed";
  params?: NotificationParams;
}
```

Required sets: `Tool` → `inputSchema`, `name`. `ToolExecution` → none (only `taskSupport`).
`ToolChoice` → none. `ToolAnnotations` → none. `CallToolRequestParams` → `name`.
`CallToolResult` → `content`.

### 1.5.2 `Tool` field list

| Field | JSON type | Required | Notes |
|---|---|---|---|
| `name` | string | yes | programmatic identifier; see name rules below |
| `title` | string | no | display name; precedence for display is `title`, then `annotations.title`, then `name` |
| `description` | string | no | hint to the model |
| `icons` | `Icon[]` | no | new in 2025-11-25 |
| `inputSchema` | object | yes | `$schema?`, `type: "object"`, `properties?`, `required?`; defaults to JSON Schema 2020-12 when `$schema` is absent |
| `execution` | `ToolExecution` | no | new in 2025-11-25 |
| `execution.taskSupport` | `"forbidden" \| "optional" \| "required"` | no | default `"forbidden"` when absent |
| `outputSchema` | object | no | same shape as `inputSchema`; "Currently restricted to `type: "object"` at the root level" |
| `annotations` | `ToolAnnotations` | no | hints only; treat as untrusted |
| `_meta` | object | no | |

Tool name rules (`server/tools.mdx` lines 217–228, verbatim list): 1–128 chars inclusive;
case-sensitive; SHOULD only allow `A-Z a-z 0-9 _ - .`; SHOULD NOT contain spaces/commas/other
special chars; SHOULD be unique within a server. Examples given: `getUser`, `DATA_EXPORT_v2`,
`admin.tools.list`.

`inputSchema` rules (`server/tools.mdx` lines 198–204): "Defaults to 2020-12 if no `$schema` field is
present"; "**MUST** be a valid JSON Schema object (not `null`)"; for no-parameter tools use
`{ "type": "object", "additionalProperties": false }` (Recommended) or `{ "type": "object" }`.

### 1.5.3 `tools/list` / `tools/call` wire examples

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/list",
  "params": { "cursor": "optional-cursor-value" }
}
```

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "tools": [
      {
        "name": "get_weather",
        "title": "Weather Information Provider",
        "description": "Get current weather information for a location",
        "inputSchema": {
          "type": "object",
          "properties": {
            "location": { "type": "string", "description": "City name or zip code" }
          },
          "required": ["location"]
        },
        "icons": [
          { "src": "https://example.com/weather-icon.png", "mimeType": "image/png", "sizes": ["48x48"] }
        ],
        "execution": { "taskSupport": "optional" }
      }
    ],
    "nextCursor": "next-page-cursor"
  }
}
```

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": { "name": "get_weather", "arguments": { "location": "New York" } }
}
```

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "content": [
      { "type": "text", "text": "Current weather in New York:\nTemperature: 72°F\nConditions: Partly cloudy" }
    ],
    "isError": false
  }
}
```

Task-augmented `tools/call` (`basic/utilities/tasks.mdx` lines 130–167):

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "get_weather",
    "arguments": { "city": "New York" },
    "task": { "ttl": 60000 }
  }
}
```

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "task": {
      "taskId": "786512e2-9e0d-44bd-8f29-789f320fe840",
      "status": "working",
      "statusMessage": "The operation is now in progress.",
      "createdAt": "2025-11-25T10:30:00Z",
      "lastUpdatedAt": "2025-11-25T10:40:00Z",
      "ttl": 60000,
      "pollInterval": 5000
    }
  }
}
```

Structured output example (`server/tools.mdx` lines 383–401):

```json
{
  "jsonrpc": "2.0",
  "id": 5,
  "result": {
    "content": [
      { "type": "text", "text": "{\"temperature\": 22.5, \"conditions\": \"Partly cloudy\", \"humidity\": 65}" }
    ],
    "structuredContent": { "temperature": 22.5, "conditions": "Partly cloudy", "humidity": 65 }
  }
}
```

Error model (`server/tools.mdx` lines 460–508): Protocol errors = unknown tool, malformed request
(fails `CallToolRequest` schema), server errors. Tool Execution Errors = "API failures",
"Input validation errors (e.g., date in wrong format, value out of range)", "Business logic errors" —
reported with `isError: true`; clients **SHOULD** feed these to the model for self-correction.

---

## 1.6 Content blocks

Source: `schema/2025-11-25/schema.ts` lines 1737–1908.

```typescript
export type ContentBlock =
  TextContent | ImageContent | AudioContent | ResourceLink | EmbeddedResource;

export type SamplingMessageContentBlock =
  TextContent | ImageContent | AudioContent | ToolUseContent | ToolResultContent;

export interface TextContent {
  type: "text";
  text: string;
  annotations?: Annotations;
  _meta?: { [key: string]: unknown };
}

export interface ImageContent {
  type: "image";
  /** @format byte */ data: string;
  mimeType: string;
  annotations?: Annotations;
  _meta?: { [key: string]: unknown };
}

export interface AudioContent {
  type: "audio";
  /** @format byte */ data: string;
  mimeType: string;
  annotations?: Annotations;
  _meta?: { [key: string]: unknown };
}

export interface ResourceLink extends Resource {
  type: "resource_link";
}

export interface EmbeddedResource {
  type: "resource";
  resource: TextResourceContents | BlobResourceContents;
  annotations?: Annotations;
  _meta?: { [key: string]: unknown };
}

export interface ToolUseContent {
  type: "tool_use";
  id: string;
  name: string;
  input: { [key: string]: unknown };
  _meta?: { [key: string]: unknown };
}

export interface ToolResultContent {
  type: "tool_result";
  toolUseId: string;
  content: ContentBlock[];
  structuredContent?: { [key: string]: unknown };
  isError?: boolean;
  _meta?: { [key: string]: unknown };
}

export interface SamplingMessage {
  role: Role;
  content: SamplingMessageContentBlock | SamplingMessageContentBlock[];
  _meta?: { [key: string]: unknown };
}
```

Content-block `type` discriminator table:

| `type` | Interface | Required fields | Optional fields |
|---|---|---|---|
| `"text"` | `TextContent` | `type`, `text` | `annotations`, `_meta` |
| `"image"` | `ImageContent` | `type`, `data`, `mimeType` | `annotations`, `_meta` |
| `"audio"` | `AudioContent` | `type`, `data`, `mimeType` | `annotations`, `_meta` |
| `"resource_link"` | `ResourceLink` | `type`, `uri`, `name` | `title`, `description`, `mimeType`, `size`, `icons`, `annotations`, `_meta` |
| `"resource"` | `EmbeddedResource` | `type`, `resource` | `annotations`, `_meta` |
| `"tool_use"` | `ToolUseContent` | `type`, `id`, `name`, `input` | `_meta` |
| `"tool_result"` | `ToolResultContent` | `type`, `toolUseId`, `content` | `structuredContent`, `isError`, `_meta` |

Wire examples (`server/tools.mdx` lines 244–318, `client/sampling.mdx`):

```json
{ "type": "text", "text": "Tool result text" }
```
```json
{ "type": "image", "data": "base64-encoded-data", "mimeType": "image/png",
  "annotations": { "audience": ["user"], "priority": 0.9 } }
```
```json
{ "type": "audio", "data": "base64-encoded-audio-data", "mimeType": "audio/wav" }
```
```json
{ "type": "resource_link", "uri": "file:///project/src/main.rs", "name": "main.rs",
  "description": "Primary application entry point", "mimeType": "text/x-rust" }
```
```json
{ "type": "resource",
  "resource": { "uri": "file:///project/src/main.rs", "mimeType": "text/x-rust",
                "text": "fn main() {\n    println!(\"Hello world!\");\n}",
                "annotations": { "audience": ["user", "assistant"], "priority": 0.7,
                                 "lastModified": "2025-05-03T14:30:00Z" } } }
```
```json
{ "type": "tool_use", "id": "call_abc123", "name": "get_weather", "input": { "city": "Paris" } }
```
```json
{ "type": "tool_result", "toolUseId": "call_abc123",
  "content": [ { "type": "text", "text": "Weather in Paris: 18°C, partly cloudy" } ] }
```

---

## 1.7 Resources

Source: `schema/2025-11-25/schema.ts` lines 649–919; `docs/specification/2025-11-25/server/resources.mdx`.

```typescript
export interface ListResourcesRequest extends PaginatedRequest {
  method: "resources/list";
}
export interface ListResourcesResult extends PaginatedResult {
  resources: Resource[];
}

export interface ListResourceTemplatesRequest extends PaginatedRequest {
  method: "resources/templates/list";
}
export interface ListResourceTemplatesResult extends PaginatedResult {
  resourceTemplates: ResourceTemplate[];
}

export interface ResourceRequestParams extends RequestParams {
  /** @format uri */
  uri: string;
}
export interface ReadResourceRequestParams extends ResourceRequestParams {}
export interface ReadResourceRequest extends JSONRPCRequest {
  method: "resources/read";
  params: ReadResourceRequestParams;
}
export interface ReadResourceResult extends Result {
  contents: (TextResourceContents | BlobResourceContents)[];
}

export interface ResourceListChangedNotification extends JSONRPCNotification {
  method: "notifications/resources/list_changed";
  params?: NotificationParams;
}

export interface SubscribeRequestParams extends ResourceRequestParams {}
export interface SubscribeRequest extends JSONRPCRequest {
  method: "resources/subscribe";
  params: SubscribeRequestParams;
}
export interface UnsubscribeRequestParams extends ResourceRequestParams {}
export interface UnsubscribeRequest extends JSONRPCRequest {
  method: "resources/unsubscribe";
  params: UnsubscribeRequestParams;
}
export interface ResourceUpdatedNotificationParams extends NotificationParams {
  /** @format uri */ uri: string;
}
export interface ResourceUpdatedNotification extends JSONRPCNotification {
  method: "notifications/resources/updated";
  params: ResourceUpdatedNotificationParams;
}

export interface Resource extends BaseMetadata, Icons {
  /** @format uri */ uri: string;
  description?: string;
  mimeType?: string;
  annotations?: Annotations;
  size?: number;
  _meta?: { [key: string]: unknown };
}

export interface ResourceTemplate extends BaseMetadata, Icons {
  /** @format uri-template */ uriTemplate: string;
  description?: string;
  mimeType?: string;
  annotations?: Annotations;
  _meta?: { [key: string]: unknown };
}

export interface ResourceContents {
  /** @format uri */ uri: string;
  mimeType?: string;
  _meta?: { [key: string]: unknown };
}
export interface TextResourceContents extends ResourceContents { text: string; }
export interface BlobResourceContents extends ResourceContents { blob: string; /* @format byte */ }
```

| Definition | `properties` | `required` |
|---|---|---|
| `Resource` | `_meta`, `annotations`, `description`, `icons`, `mimeType`, `name`, `size`, `title`, `uri` | `name`, `uri` |
| `ResourceTemplate` | `_meta`, `annotations`, `description`, `icons`, `mimeType`, `name`, `title`, `uriTemplate` | `name`, `uriTemplate` |
| `ResourceContents` | `_meta`, `mimeType`, `uri` | `uri` |
| `ReadResourceResult` | `_meta`, `contents` | `contents` |
| `ResourceLink` | `_meta`, `annotations`, `description`, `icons`, `mimeType`, `name`, `size`, `title`, `type`, `uri` | `name`, `type`, `uri` |

Wire examples: `resources/list` (with `title` and `icons`), `resources/read`, `resources/templates/list`,
`resources/subscribe`, `notifications/resources/updated` — see `server/resources.mdx` lines 85–249.
Note the 2025-11-25 `resources/templates/list` example request has **no `params` at all**:

```json
{ "jsonrpc": "2.0", "id": 3, "method": "resources/templates/list" }
```

Error: Resource not found = `-32002` still in 2025-11-25 (`server/resources.mdx` line 388;
changed to `-32602` only in 2026-07-28).

---

## 1.8 Prompts

Source: `schema/2025-11-25/schema.ts` lines 921–1078; `docs/specification/2025-11-25/server/prompts.mdx`.

```typescript
export interface ListPromptsRequest extends PaginatedRequest { method: "prompts/list"; }
export interface ListPromptsResult extends PaginatedResult { prompts: Prompt[]; }

export interface GetPromptRequestParams extends RequestParams {
  name: string;
  arguments?: { [key: string]: string };
}
export interface GetPromptRequest extends JSONRPCRequest {
  method: "prompts/get";
  params: GetPromptRequestParams;
}
export interface GetPromptResult extends Result {
  description?: string;
  messages: PromptMessage[];
}

export interface Prompt extends BaseMetadata, Icons {
  description?: string;
  arguments?: PromptArgument[];
  _meta?: { [key: string]: unknown };
}

export interface PromptArgument extends BaseMetadata {
  description?: string;
  required?: boolean;
}

export interface PromptMessage {
  role: Role;
  content: ContentBlock;   // NOT an array
}

export interface PromptListChangedNotification extends JSONRPCNotification {
  method: "notifications/prompts/list_changed";
  params?: NotificationParams;
}
```

| Definition | `properties` | `required` |
|---|---|---|
| `Prompt` | `_meta`, `arguments`, `description`, `icons`, `name`, `title` | `name` |
| `PromptArgument` | `description`, `name`, `required`, `title` | `name` |
| `PromptMessage` | `content`, `role` | `content`, `role` |

`prompts/list` response with 2025-11-25 additions (`title`, `icons`) — `server/prompts.mdx` lines 68–97:

```json
{
  "jsonrpc": "2.0", "id": 1,
  "result": {
    "prompts": [
      { "name": "code_review",
        "title": "Request Code Review",
        "description": "Asks the LLM to analyze code quality and suggest improvements",
        "arguments": [ { "name": "code", "description": "The code to review", "required": true } ],
        "icons": [ { "src": "https://example.com/review-icon.svg", "mimeType": "image/svg+xml", "sizes": ["any"] } ] }
    ],
    "nextCursor": "next-page-cursor"
  }
}
```

Prompt content types in 2025-11-25: text, image, **audio**, embedded resources. All support
`annotations`. `PromptMessage.content` is a single `ContentBlock` (never an array); to send multiple
parts the server returns multiple `PromptMessage` entries.

---

## 1.9 Elicitation (new client feature in 2025-11-25)

Source: `schema/2025-11-25/schema.ts` lines 2150–2501; `docs/specification/2025-11-25/client/elicitation.mdx`.
Elicitation itself was introduced in 2025-06-18 with **form mode only** and the simpler `EnumSchema`;
2025-11-25 adds **URL mode**, redesigns enum schemas (SEP-1330), and adds defaults (SEP-1034).

### 1.9.1 Two modes and capability rules

- `"form"`: "In-band structured data collection with optional schema validation. Data is exposed to the client."
- `"url"`: "Out-of-band interaction via URL navigation. Data (other than the URL itself) is **not** exposed to the client."
- "For backwards compatibility, servers **MAY** omit the `mode` field for form mode elicitation requests. Clients **MUST** treat requests without a `mode` field as form mode."
- "Servers **MUST NOT** send elicitation requests with modes that are not supported by the client."
- Safety: "Servers **MUST NOT** use form mode elicitation to request sensitive information such as passwords, API keys, access tokens, or payment credentials"; "Servers **MUST** use URL mode for interactions involving such sensitive information."
- Client duty for unknown-mode requests: return `-32602` (Invalid params).

### 1.9.2 Request params

```typescript
export interface ElicitRequestFormParams extends TaskAugmentedRequestParams {
  mode?: "form";
  message: string;
  requestedSchema: {
    $schema?: string;
    type: "object";
    properties: {
      [key: string]: PrimitiveSchemaDefinition;
    };
    required?: string[];
  };
}

export interface ElicitRequestURLParams extends TaskAugmentedRequestParams {
  mode: "url";
  message: string;
  elicitationId: string;
  /** @format uri */
  url: string;
}

export type ElicitRequestParams = ElicitRequestFormParams | ElicitRequestURLParams;

export interface ElicitRequest extends JSONRPCRequest {
  method: "elicitation/create";
  params: ElicitRequestParams;
}
```

| Definition | `properties` | `required` |
|---|---|---|
| `ElicitRequest` | `id`, `jsonrpc`, `method` (= `"elicitation/create"`), `params` | `id`, `jsonrpc`, `method`, `params` |
| `ElicitRequestFormParams` | `_meta`, `message`, `mode` (`const "form"`), `requestedSchema`, `task` | **`message`, `requestedSchema`** (`mode` optional!) |
| `ElicitRequestURLParams` | `_meta`, `elicitationId`, `message`, `mode` (`const "url"`), `task`, `url` | **`elicitationId`, `message`, `mode`, `url`** |
| `ElicitRequestParams` | `anyOf: [URLParams, FormParams]` (URL first) | — |

Note the JSON-Schema `mode` in `ElicitRequestFormParams` is `{"const":"form"}` (a const, not an
enum), so a present `mode` must be exactly `"form"`.

Wire examples:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "elicitation/create",
  "params": {
    "mode": "form",
    "message": "Please provide your GitHub username",
    "requestedSchema": {
      "type": "object",
      "properties": { "name": { "type": "string" } },
      "required": ["name"]
    }
  }
}
```

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "elicitation/create",
  "params": {
    "mode": "url",
    "elicitationId": "550e8400-e29b-41d4-a716-446655440000",
    "url": "https://mcp.example.com/ui/set_api_key",
    "message": "Please provide your API key to continue."
  }
}
```

### 1.9.3 `requestedSchema` primitive subset (`PrimitiveSchemaDefinition`)

```typescript
export type PrimitiveSchemaDefinition =
  StringSchema | NumberSchema | BooleanSchema | EnumSchema;

export interface StringSchema {
  type: "string";
  title?: string;
  description?: string;
  minLength?: number;
  maxLength?: number;
  format?: "email" | "uri" | "date" | "date-time";
  default?: string;              // NEW in 2025-11-25
}

export interface NumberSchema {
  type: "number" | "integer";
  title?: string;
  description?: string;
  minimum?: number;
  maximum?: number;
  default?: number;              // NEW in 2025-11-25
}

export interface BooleanSchema {
  type: "boolean";
  title?: string;
  description?: string;
  default?: boolean;
}

export interface UntitledSingleSelectEnumSchema {
  type: "string";
  title?: string;
  description?: string;
  enum: string[];
  default?: string;
}

export interface TitledSingleSelectEnumSchema {
  type: "string";
  title?: string;
  description?: string;
  oneOf: Array<{ const: string; title: string }>;
  default?: string;
}

export type SingleSelectEnumSchema =
  UntitledSingleSelectEnumSchema | TitledSingleSelectEnumSchema;

export interface UntitledMultiSelectEnumSchema {
  type: "array";
  title?: string;
  description?: string;
  minItems?: number;
  maxItems?: number;
  items: { type: "string"; enum: string[] };
  default?: string[];
}

export interface TitledMultiSelectEnumSchema {
  type: "array";
  title?: string;
  description?: string;
  minItems?: number;
  maxItems?: number;
  items: { anyOf: Array<{ const: string; title: string }> };
  default?: string[];
}

export type MultiSelectEnumSchema =
  UntitledMultiSelectEnumSchema | TitledMultiSelectEnumSchema;

/** Use TitledSingleSelectEnumSchema instead. This interface will be removed in a future version. */
export interface LegacyTitledEnumSchema {
  type: "string";
  title?: string;
  description?: string;
  enum: string[];
  enumNames?: string[];
  default?: string;
}

export type EnumSchema =
  SingleSelectEnumSchema | MultiSelectEnumSchema | LegacyTitledEnumSchema;
```

Required sets: `StringSchema`/`BooleanSchema`/`UntitledSingleSelectEnumSchema`/
`TitledSingleSelectEnumSchema`/`LegacyTitledEnumSchema` → `type` (+ `enum` or `oneOf` as applicable).
`NumberSchema` → `type`. `UntitledMultiSelectEnumSchema`/`TitledMultiSelectEnumSchema` →
`items`, `type`. `TitledSingleSelectEnumSchema.oneOf[]` items require `const` and `title`;
`TitledMultiSelectEnumSchema.items.anyOf[]` items require `const` and `title`.

Note the JSON Schema `default` for numeric/array types is typed numerically
(`NumberSchema.default: number`, not integer; `*MultiSelectEnumSchema.default: string[]`).

2025-11-25 prose constraint: "Note that complex nested structures, arrays of objects (beyond enums),
and other advanced JSON Schema features are intentionally not supported to simplify client user
experience." and "All primitive types support optional default values to provide sensible starting
points. Clients that support defaults SHOULD pre-populate form fields with these values."

### 1.9.4 Result

```typescript
export interface ElicitResult extends Result {
  action: "accept" | "decline" | "cancel";
  content?: { [key: string]: string | number | boolean | string[] };
}
```

| Definition | `properties` | `required` |
|---|---|---|
| `ElicitResult` | `_meta`, `action`, `content` | `action` |

`action` JSON-Schema enum (generated JSON, sorted): `["accept","cancel","decline"]`.
`content` JSON Schema: `additionalProperties: { anyOf: [ {items:{type:"string"},type:"array"},
{type:["string","integer","boolean"]} ] }`.

Semantics (verbatim, `client/elicitation.mdx` lines 548–561):

1. **Accept** (`action: "accept"`): User explicitly approved and submitted with data
   - For form mode: The `content` field contains the submitted data matching the requested schema
   - For URL mode: The `content` field is omitted
2. **Decline** (`action: "decline"`): User explicitly declined the request
   - The `content` field is typically omitted
3. **Cancel** (`action: "cancel"`): User dismissed without making an explicit choice
   - The `content` field is typically omitted

Note: in 2025-06-18 `ElicitResult.content` was `{ [key: string]: string | number | boolean }`
(no `string[]`) and `action` doc text said "User explicitly declined the action".

```json
{ "jsonrpc": "2.0", "id": 1, "result": { "action": "accept", "content": { "name": "octocat" } } }
```
```json
{ "jsonrpc": "2.0", "id": 3, "result": { "action": "accept" } }
```

### 1.9.5 `notifications/elicitation/complete`

```typescript
export interface ElicitationCompleteNotification extends JSONRPCNotification {
  method: "notifications/elicitation/complete";
  params: {
    elicitationId: string;
  };
}
```

Required: `jsonrpc`, `method`, `params`; `params.elicitationId` required.

```json
{
  "jsonrpc": "2.0",
  "method": "notifications/elicitation/complete",
  "params": { "elicitationId": "550e8400-e29b-41d4-a716-446655440000" }
}
```

Rules: servers **MUST** only send it to the client that initiated the elicitation; **MUST** include the
`elicitationId` from the original request. Clients **MUST** ignore notifications referencing unknown or
already-completed IDs; **MAY** use it to auto-retry requests that received a
`URLElicitationRequiredError`; **SHOULD** still offer manual retry/cancel.

### 1.9.6 `-32042` URL elicitation required

```typescript
// Implementation-specific JSON-RPC error codes [-32000, -32099]
export const URL_ELICITATION_REQUIRED = -32042;

export interface URLElicitationRequiredError extends Omit<JSONRPCErrorResponse, "error"> {
  error: Error & {
    code: typeof URL_ELICITATION_REQUIRED;
    data: {
      elicitations: ElicitRequestURLParams[];
      [key: string]: unknown;
    };
  };
}
```

| Definition | `properties` | `required` |
|---|---|---|
| `URLElicitationRequiredError` | `error`, `id`, `jsonrpc` | `error`, `jsonrpc` |
| `error` | `code` (const `-32042`), `data` | `code`, `data` |
| `error.data` | `elicitations` | `elicitations` |

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "error": {
    "code": -32042,
    "message": "This request requires more information.",
    "data": {
      "elicitations": [
        {
          "mode": "url",
          "elicitationId": "550e8400-e29b-41d4-a716-446655440000",
          "url": "https://mcp.example.com/connect?elicitationId=550e8400-e29b-41d4-a716-446655440000",
          "message": "Authorization is required to access your Example Co files."
        }
      ]
    }
  }
}
```

Rules: "the server **MUST NOT** return this error except when URL mode elicitation is required";
"The error **MUST** include a list of elicitations that are required to complete before the original
can be retried"; "Any elicitations returned in the error **MUST** be URL mode elicitations and have an
`elicitationId` property."

Client-side URL handling rules (verbatim list, `client/elicitation.mdx` lines 724–733):

1. **MUST NOT** automatically pre-fetch the URL or any of its metadata.
2. **MUST NOT** open the URL without explicit consent from the user.
3. **MUST** show the full URL to the user for examination before consent.
4. **MUST** open the URL provided by the server in a secure manner that does not enable the client or LLM to inspect the content or user inputs.
5. **SHOULD** highlight the domain of the URL to mitigate subdomain spoofing.
6. **SHOULD** have warnings for ambiguous/suspicious URIs (i.e., containing Punycode).
7. **SHOULD NOT** render URLs as clickable in any field of an elicitation request, except for the `url` field in a URL elicitation request.

### 1.9.7 Naming

The interface is **`ElicitResult`** (JSON Schema `$defs/ElicitResult`). There is no
`ElicitationResult` symbol. `ElicitRequestParams` is a union. Likewise the notification is
`ElicitationCompleteNotification` (method `notifications/elicitation/complete`).

---

## 1.10 Tasks (new, experimental, in 2025-11-25)

Source: `schema/2025-11-25/schema.ts` lines 1299–1498; `docs/specification/2025-11-25/basic/utilities/tasks.mdx`.

> "Tasks were introduced in version 2025-11-25 of the MCP specification and are currently considered **experimental**."

### 1.10.1 Types

```typescript
export type TaskStatus =
  | "working"          // The request is currently being processed
  | "input_required"   // The task is waiting for input (e.g., elicitation or sampling)
  | "completed"        // The request completed successfully and results are available
  | "failed"           // The associated request did not complete successfully. For tool calls specifically, this includes cases where the tool call result has `isError` set to true.
  | "cancelled";       // The request was cancelled before completion

export interface TaskMetadata {
  ttl?: number;        // Requested duration in milliseconds to retain task from creation.
}

export interface RelatedTaskMetadata {
  taskId: string;      // _meta key: "io.modelcontextprotocol/related-task"
}

export interface Task {
  taskId: string;
  status: TaskStatus;
  statusMessage?: string;
  /** ISO 8601 */ createdAt: string;
  /** ISO 8601 */ lastUpdatedAt: string;
  /** @nullable */ ttl: number | null;
  pollInterval?: number;
}

export interface TaskAugmentedRequestParams extends RequestParams {
  task?: TaskMetadata;
}

export interface CreateTaskResult extends Result {
  task: Task;
}

export interface GetTaskRequest extends JSONRPCRequest {
  method: "tasks/get";
  params: { taskId: string };
}
export type GetTaskResult = Result & Task;

export interface GetTaskPayloadRequest extends JSONRPCRequest {
  method: "tasks/result";
  params: { taskId: string };
}
export interface GetTaskPayloadResult extends Result {
  [key: string]: unknown;
}

export interface CancelTaskRequest extends JSONRPCRequest {
  method: "tasks/cancel";
  params: { taskId: string };
}
export type CancelTaskResult = Result & Task;

export interface ListTasksRequest extends PaginatedRequest {
  method: "tasks/list";
}
export interface ListTasksResult extends PaginatedResult {
  tasks: Task[];
}

export type TaskStatusNotificationParams = NotificationParams & Task;

export interface TaskStatusNotification extends JSONRPCNotification {
  method: "notifications/tasks/status";
  params: TaskStatusNotificationParams;
}
```

### 1.10.2 `Task` field table

| Field | JSON type | Required | Notes |
|---|---|---|---|
| `taskId` | string | **yes** | receiver-generated, unique among tasks controlled by the receiver |
| `status` | `"working"\|"input_required"\|"completed"\|"failed"\|"cancelled"` | **yes** | |
| `statusMessage` | string | no | may be present for any status |
| `createdAt` | string | **yes** | ISO 8601 |
| `lastUpdatedAt` | string | **yes** | ISO 8601 |
| `ttl` | integer \| null | **yes** | actual retention duration in ms; `null` = unlimited |
| `pollInterval` | integer | no | suggested polling interval in ms |

JSON-Schema `required` for `Task` = `["createdAt","lastUpdatedAt","status","taskId","ttl"]`.
`TaskMetadata.required` = none. `RelatedTaskMetadata.required` = `["taskId"]`.

### 1.10.3 Methods and required params

| Method | `params` | `params.required` | Result type |
|---|---|---|---|
| `tasks/get` | `{ taskId: string }` | `["taskId"]` | `GetTaskResult` = `Result & Task` (Task fields flattened into `result`) |
| `tasks/result` | `{ taskId: string }` | `["taskId"]` | `GetTaskPayloadResult` — "The structure matches the result type of the original request" |
| `tasks/list` | `PaginatedRequestParams` | (request requires only `id`,`jsonrpc`,`method`) | `ListTasksResult` (`tasks`, `nextCursor?`) |
| `tasks/cancel` | `{ taskId: string }` | `["taskId"]` | `CancelTaskResult` = `Result & Task` |
| `notifications/tasks/status` | `TaskStatusNotificationParams` (= `NotificationParams & Task`) | `jsonrpc`,`method`,`params` | (notification, no result) |

There is **no** `tasks/status` **request** method. `notifications/tasks/status` is listed in both
`ClientNotification` and `ServerNotification` (either side may be the receiver).
`GetTaskRequest`, `GetTaskPayloadRequest`, `ListTasksRequest`, `CancelTaskRequest` are listed in
**both** `ClientRequest` and `ServerRequest`; `GetTaskResult`, `GetTaskPayloadResult`,
`ListTasksResult`, `CancelTaskResult` are in both `ClientResult` and `ServerResult`.

### 1.10.4 Wire examples

`tasks/get` (`basic/utilities/tasks.mdx` lines 199–226):

```json
{ "jsonrpc": "2.0", "id": 3, "method": "tasks/get",
  "params": { "taskId": "786512e2-9e0d-44bd-8f29-789f320fe840" } }
```
```json
{
  "jsonrpc": "2.0", "id": 3,
  "result": {
    "taskId": "786512e2-9e0d-44bd-8f29-789f320fe840",
    "status": "working",
    "statusMessage": "The operation is now in progress.",
    "createdAt": "2025-11-25T10:30:00Z",
    "lastUpdatedAt": "2025-11-25T10:40:00Z",
    "ttl": 30000,
    "pollInterval": 5000
  }
}
```

`tasks/result` (lines 244–278) — note the `_meta` task augmentation on the result:

```json
{ "jsonrpc": "2.0", "id": 4, "method": "tasks/result",
  "params": { "taskId": "786512e2-9e0d-44bd-8f29-789f320fe840" } }
```
```json
{
  "jsonrpc": "2.0", "id": 4,
  "result": {
    "content": [
      { "type": "text", "text": "Current weather in New York:\nTemperature: 72°F\nConditions: Partly cloudy" }
    ],
    "isError": false,
    "_meta": {
      "io.modelcontextprotocol/related-task": { "taskId": "786512e2-9e0d-44bd-8f29-789f320fe840" }
    }
  }
}
```

`tasks/list` (lines 309–349):

```json
{ "jsonrpc": "2.0", "id": 5, "method": "tasks/list",
  "params": { "cursor": "optional-cursor-value" } }
```
```json
{
  "jsonrpc": "2.0", "id": 5,
  "result": {
    "tasks": [
      { "taskId": "786512e2-9e0d-44bd-8f29-789f320fe840", "status": "working",
        "createdAt": "2025-11-25T10:30:00Z", "lastUpdatedAt": "2025-11-25T10:40:00Z",
        "ttl": 30000, "pollInterval": 5000 },
      { "taskId": "abc123-def456-ghi789", "status": "completed",
        "createdAt": "2025-11-25T09:15:00Z", "lastUpdatedAt": "2025-11-25T10:40:00Z",
        "ttl": 60000 }
    ],
    "nextCursor": "next-page-cursor"
  }
}
```

`tasks/cancel` (lines 355–384) → `result` is the full `Task` with `"status": "cancelled"`.

`notifications/tasks/status` (lines 286–299) → `params` is the full `Task`:

```json
{
  "jsonrpc": "2.0",
  "method": "notifications/tasks/status",
  "params": {
    "taskId": "786512e2-9e0d-44bd-8f29-789f320fe840",
    "status": "completed",
    "createdAt": "2025-11-25T10:30:00Z",
    "lastUpdatedAt": "2025-11-25T10:50:00Z",
    "ttl": 60000,
    "pollInterval": 5000
  }
}
```

### 1.10.5 `_meta` task augmentation keys

| Key | Shape | Where |
|---|---|---|
| `io.modelcontextprotocol/related-task` | `{ "taskId": string }` (`RelatedTaskMetadata`) | "All requests, notifications, and responses related to a task **MUST** include the `io.modelcontextprotocol/related-task` key in their `_meta` field" |
| `io.modelcontextprotocol/model-immediate-response` | string | optional key in the `_meta` of a `CreateTaskResult` for `tools/call`; "The value of this key should be a string intended to be passed as an immediate tool result to the model." Non-binding/provisional. |

Exemptions (verbatim, `tasks.mdx` lines 472–479): for `tasks/get`, `tasks/result`, `tasks/cancel` the
`taskId` **parameter** is the source of truth; requestors SHOULD NOT send the related-task metadata and
receivers MUST ignore it if present. For `tasks/get`, `tasks/list`, `tasks/cancel` results, receivers
SHOULD NOT include the metadata. `tasks/result` **MUST** include it (result structure lacks the task id).
`notifications/tasks/status` SHOULD NOT include it.

### 1.10.6 Behaviour requirements (condensed verbatim rules)

- Capability negotiation: "Requestors **SHOULD** only augment requests with a task if the corresponding capability has been declared by the receiver." "If `capabilities.tasks` is not defined, the peer **SHOULD NOT** attempt to create tasks during requests." "The set of capabilities in `capabilities.tasks.requests` is exhaustive."
- Tool-level: capability `tasks.requests.tools.call` gates everything. Then `execution.taskSupport`:
  absent/`"forbidden"` → clients MUST NOT use tasks; server SHOULD return `-32601` if a client tries.
  `"optional"` → either. `"required"` → clients MUST use a task; server MUST return `-32601` if the
  client does not.
- Status lifecycle: tasks MUST begin `working`; allowed transitions `working → {input_required, completed, failed, cancelled}`, `input_required → {working, completed, failed, cancelled}`; terminal states never transition.
- TTL: receivers MUST include `createdAt` and `lastUpdatedAt`; MAY override requested `ttl`; MUST include actual `ttl` (or `null`) in `tasks/get` responses; MAY delete after `ttl` elapses.
- Result retrieval: receiver MUST return `CreateTaskResult` for an accepted task-augmented request; `tasks/result` on a terminal task MUST return exactly what the underlying request would have returned (result *or* JSON-RPC error); on a non-terminal task it MUST block until terminal.
- Cancellation: receivers MUST reject cancelling an already-terminal task with `-32602`; SHOULD attempt to stop execution and MUST transition to `cancelled` before responding; once cancelled it MUST remain `cancelled`.
- Listing: MUST include `nextCursor` when more tasks exist; cursors are opaque; if a task is retrievable via `tasks/get` it MUST be retrievable via `tasks/list`.
- Progress: "The `progressToken` provided in the initial request remains valid throughout the task lifetime."
- Errors: invalid/nonexistent `taskId` in `tasks/get`/`tasks/result`/`tasks/cancel` → `-32602`;
  invalid cursor in `tasks/list` → `-32602`; cancel of terminal task → `-32602`; internal → `-32603`;
  non-task-augmented request where the receiver requires augmentation → `-32600` (Invalid request).
- Security: receivers MUST bind tasks to the authorization context when one exists; otherwise MUST use cryptographically secure task IDs and SHOULD NOT declare `tasks.list`.

---

## 1.11 Sampling with tools (new in 2025-11-25)

Source: `schema/2025-11-25/schema.ts` lines 1572–1698; `docs/specification/2025-11-25/client/sampling.mdx` lines 36–40, 145–480.

```typescript
export interface CreateMessageRequestParams extends TaskAugmentedRequestParams {
  messages: SamplingMessage[];
  modelPreferences?: ModelPreferences;
  systemPrompt?: string;
  includeContext?: "none" | "thisServer" | "allServers";
  /** @TJS-type number */
  temperature?: number;
  maxTokens: number;
  stopSequences?: string[];
  metadata?: object;
  tools?: Tool[];
  toolChoice?: ToolChoice;
}

export interface ToolChoice {
  mode?: "auto" | "required" | "none";
}

export interface CreateMessageRequest extends JSONRPCRequest {
  method: "sampling/createMessage";
  params: CreateMessageRequestParams;
}

export interface CreateMessageResult extends Result, SamplingMessage {
  model: string;
  stopReason?: "endTurn" | "stopSequence" | "maxTokens" | "toolUse" | string;
}
```

| Definition | `properties` | `required` |
|---|---|---|
| `CreateMessageRequestParams` | `_meta`, `includeContext`, `maxTokens`, `messages`, `metadata`, `modelPreferences`, `stopSequences`, `systemPrompt`, `task`, `temperature`, `toolChoice`, `tools` | `maxTokens`, `messages` |
| `CreateMessageResult` | `_meta`, `content`, `model`, `role`, `stopReason` | `content`, `model`, `role` |
| `ToolChoice` | `mode` | (none) |

Rules (verbatim):

- "Clients **MUST** declare support for tool use via the `sampling.tools` capability to receive tool-enabled sampling requests. Servers **MUST NOT** send tool-enabled sampling requests to Clients that have not declared support for tool use via the `sampling.tools` capability."
- `tools`: "The client MUST return an error if this field is provided but ClientCapabilities.sampling.tools is not declared."
- `toolChoice`: same MUST-error rule; "Default is `{ mode: "auto" }`."
- Tool choice modes: `{mode:"auto"}` model decides (default); `{mode:"required"}` model MUST use at least one tool before completing; `{mode:"none"}` model MUST NOT use any tools.
- "When a user message contains tool results (type: "tool_result"), it **MUST** contain ONLY tool results. Mixing tool results with other content types (text, image, audio) in the same message is not allowed."
- "every assistant message containing `ToolUseContent` blocks **MUST** be followed by a user message that consists entirely of `ToolResultContent` blocks, with each tool use (e.g. with `id: $id`) matched by a corresponding tool result (with `toolUseId: $id`), before any other message."
- Error codes: user rejected sampling → `-1`; tool result missing → `-32602`; tool results mixed with other content → `-32602`.
- `ModelPreferences`: `hints?: ModelHint[]`, `costPriority?`, `speedPriority?`, `intelligencePriority?` (each `number`, `@minimum 0 @maximum 1`). `ModelHint`: `{ name?: string }`.
- `includeContext` values `"thisServer"` and `"allServers"` are **soft-deprecated** in 2025-11-25 (see §1.14).

Tool-enabled request/response examples (`client/sampling.mdx` lines 193–262):

```json
{
  "jsonrpc": "2.0", "id": 1, "method": "sampling/createMessage",
  "params": {
    "messages": [
      { "role": "user", "content": { "type": "text", "text": "What's the weather like in Paris and London?" } }
    ],
    "tools": [
      { "name": "get_weather", "description": "Get current weather for a city",
        "inputSchema": { "type": "object",
          "properties": { "city": { "type": "string", "description": "City name" } },
          "required": ["city"] } }
    ],
    "toolChoice": { "mode": "auto" },
    "maxTokens": 1000
  }
}
```
```json
{
  "jsonrpc": "2.0", "id": 1,
  "result": {
    "role": "assistant",
    "content": [
      { "type": "tool_use", "id": "call_abc123", "name": "get_weather", "input": { "city": "Paris" } },
      { "type": "tool_use", "id": "call_def456", "name": "get_weather", "input": { "city": "London" } }
    ],
    "model": "claude-3-sonnet-20240307",
    "stopReason": "toolUse"
  }
}
```

Follow-up request with tool results (`client/sampling.mdx` lines 275–348): assistant message with the
`tool_use` blocks, then a `user` message containing only `tool_result` blocks with
`toolUseId` matching each `id`.

---

## 1.12 Roots

Source: `schema/2025-11-25/schema.ts` lines 2083–2148; `docs/specification/2025-11-25/client/roots.mdx`.

```typescript
export interface ListRootsRequest extends JSONRPCRequest {
  method: "roots/list";
  params?: RequestParams;
}
export interface ListRootsResult extends Result {
  roots: Root[];
}
export interface Root {
  /** @format uri */ uri: string;
  name?: string;
  _meta?: { [key: string]: unknown };
}
export interface RootsListChangedNotification extends JSONRPCNotification {
  method: "notifications/roots/list_changed";
  params?: NotificationParams;
}
```

Required sets: `ListRootsRequest` → `id`,`jsonrpc`,`method` (no `params`); `ListRootsResult` →
`roots`; `Root` → `uri`. `Root.uri` "**MUST** be a `file://` URI in the current specification"
(prose) / "This *must* start with file:// for now" (schema comment).

Error: client does not support roots → `-32601` (Method not found).

Roots are otherwise unchanged from 2024-11-05, except `Root._meta` (added 2025-06-18).

---

## 1.13 Streamable HTTP transport in 2025-11-25

Source: `docs/specification/2025-11-25/basic/transports.mdx` (320 lines).

> "MCP uses JSON-RPC to encode messages. JSON-RPC messages **MUST** be UTF-8 encoded."
> Two standard transports: (1) stdio, (2) Streamable HTTP. "Clients **SHOULD** support stdio whenever possible."
> "This replaces the [HTTP+SSE transport](/specification/2024-11-05/basic/transports#http-with-sse) from protocol version 2024-11-05."

### 1.13.1 Security / Origin validation

> 1. Servers **MUST** validate the `Origin` header on all incoming connections to prevent DNS rebinding attacks
>    - If the `Origin` header is present and invalid, servers **MUST** respond with HTTP 403 Forbidden. The HTTP response body **MAY** comprise a JSON-RPC _error response_ that has no `id`
> 2. When running locally, servers **SHOULD** bind only to localhost (127.0.0.1) rather than all network interfaces (0.0.0.0)
> 3. Servers **SHOULD** implement proper authentication for all connections

(The `403 Forbidden` sentence is new in 2025-11-25; 2025-06-18 line 76 had only "Servers **MUST** validate the `Origin` header…" with no status code.)

### 1.13.2 Sending messages to the server (POST)

> Every JSON-RPC message sent from the client **MUST** be a new HTTP POST request to the MCP endpoint.
>
> 1. The client **MUST** use HTTP POST to send JSON-RPC messages to the MCP endpoint.
> 2. The client **MUST** include an `Accept` header, listing both `application/json` and `text/event-stream` as supported content types.
> 3. The body of the POST request **MUST** be a single JSON-RPC _request_, _notification_, or _response_.
> 4. If the input is a JSON-RPC _response_ or _notification_:
>    - If the server accepts the input, the server **MUST** return HTTP status code 202 Accepted with no body.
>    - If the server cannot accept the input, it **MUST** return an HTTP error status code (e.g., 400 Bad Request). The HTTP response body **MAY** comprise a JSON-RPC _error response_ that has no `id`.
> 5. If the input is a JSON-RPC _request_, the server **MUST** either return `Content-Type: text/event-stream`, to initiate an SSE stream, or `Content-Type: application/json`, to return one JSON object. The client **MUST** support both these cases.
> 6. If the server initiates an SSE stream:
>    - The server **SHOULD** immediately send an SSE event consisting of an event ID and an empty `data` field in order to prime the client to reconnect (using that event ID as `Last-Event-ID`).
>    - After the server has sent an SSE event with an event ID to the client, the server **MAY** close the _connection_ (without terminating the _SSE stream_) at any time in order to avoid holding a long-lived connection. The client **SHOULD** then "poll" the SSE stream by attempting to reconnect.
>    - If the server does close the _connection_ prior to terminating the _SSE stream_, it **SHOULD** send an SSE event with a standard `retry` field before closing the connection. The client **MUST** respect the `retry` field, waiting the given number of milliseconds before attempting to reconnect.
>    - The SSE stream **SHOULD** eventually include a JSON-RPC _response_ for the JSON-RPC _request_ sent in the POST body.
>    - The server **MAY** send JSON-RPC _requests_ and _notifications_ before sending the JSON-RPC _response_. These messages **SHOULD** relate to the originating client _request_.
>    - The server **MAY** terminate the SSE stream if the session expires.
>    - After the JSON-RPC _response_ has been sent, the server **SHOULD** terminate the SSE stream.
>    - Disconnection **MAY** occur at any time (e.g., due to network conditions). Therefore:
>      - Disconnection **SHOULD NOT** be interpreted as the client cancelling its request.
>      - To cancel, the client **SHOULD** explicitly send an MCP `CancelledNotification`.
>      - To avoid message loss due to disconnection, the server **MAY** make the stream resumable.

**Batching is gone in 2025-11-25** (removed in 2025-06-18): item 3 says "a single JSON-RPC _request_, _notification_, or _response_"; the 2025-03-26 wording ("An array batching one or more _requests and/or notifications_") is gone.

### 1.13.3 Listening for messages from the server (HTTP GET)

> 1. The client **MAY** issue an HTTP GET to the MCP endpoint. This can be used to open an SSE stream, allowing the server to communicate to the client, without the client first sending data via HTTP POST.
> 2. The client **MUST** include an `Accept` header, listing `text/event-stream` as a supported content type.
> 3. The server **MUST** either return `Content-Type: text/event-stream` in response to this HTTP GET, or else return HTTP 405 Method Not Allowed, indicating that the server does not offer an SSE stream at this endpoint.
> 4. If the server initiates an SSE stream:
>    - The server **MAY** send JSON-RPC _requests_ and _notifications_ on the stream.
>    - These messages **SHOULD** be unrelated to any concurrently-running JSON-RPC _request_ from the client.
>    - The server **MUST NOT** send a JSON-RPC _response_ on the stream **unless** resuming a stream associated with a previous client request.
>    - The server **MAY** close the SSE stream at any time.
>    - If the server closes the _connection_ without terminating the _stream_, it **SHOULD** follow the same polling behavior as described for POST requests: sending a `retry` field and allowing the client to reconnect.
>    - The client **MAY** close the SSE stream at any time.

New in 2025-11-25 vs 2025-06-18: the `retry`/polling bullet in GET, and the note that task messages may arrive on any stream (see tasks.mdx).

### 1.13.4 Multiple connections

> 1. The client **MAY** remain connected to multiple SSE streams simultaneously.
> 2. The server **MUST** send each of its JSON-RPC messages on only one of the connected streams; that is, it **MUST NOT** broadcast the same message across multiple streams.

### 1.13.5 Session management — `MCP-Session-Id`

> 1. A server using the Streamable HTTP transport **MAY** assign a session ID at initialization time, by including it in an `MCP-Session-Id` header on the HTTP response containing the `InitializeResult`.
>    - The session ID **SHOULD** be globally unique and cryptographically secure (e.g., a securely generated UUID, a JWT, or a cryptographic hash).
>    - The session ID **MUST** only contain visible ASCII characters (ranging from 0x21 to 0x7E).
>    - The client **MUST** handle the session ID in a secure manner…
> 2. If an `MCP-Session-Id` is returned by the server during initialization, clients using the Streamable HTTP transport **MUST** include it in the `MCP-Session-Id` header on all of their subsequent HTTP requests.
>    - Servers that require a session ID **SHOULD** respond to requests without an `MCP-Session-Id` header (other than initialization) with HTTP 400 Bad Request.
> 3. The server **MAY** terminate the session at any time, after which it **MUST** respond to requests containing that session ID with HTTP 404 Not Found.
> 4. When a client receives HTTP 404 in response to a request containing an `MCP-Session-Id`, it **MUST** start a new session by sending a new `InitializeRequest` without a session ID attached.
> 5. Clients that no longer need a particular session … **SHOULD** send an HTTP DELETE to the MCP endpoint with the `MCP-Session-Id` header, to explicitly terminate the session.
>    - The server **MAY** respond to this request with HTTP 405 Method Not Allowed, indicating that the server does not allow clients to terminate sessions.

Header-name history: `Mcp-Session-Id` in 2025-03-26 (`basic/transports.mdx` lines 187–205) and
2025-06-18 (lines 177–195); **`MCP-Session-Id`** in 2025-11-25 (lines 199–218). The session ID itself
is required to be visible ASCII `0x21`–`0x7E`.

DELETE summary for a .NET client: `DELETE <MCP endpoint>` with `MCP-Session-Id`; expect success or
`405 Method Not Allowed`.

### 1.13.6 Resumability and redelivery

> 1. Servers **MAY** attach an `id` field to their SSE events… If present, the ID **MUST** be globally unique across all streams within that session—or all streams with that specific client, if session management is not in use.
>    - Event IDs **SHOULD** encode sufficient information to identify the originating stream, enabling the server to correlate a `Last-Event-ID` to the correct stream.
> 2. If the client wishes to resume after a disconnection (whether due to network failure or server-initiated closure), it **SHOULD** issue an HTTP GET to the MCP endpoint, and include the `Last-Event-ID` header to indicate the last event ID it received.
>    - The server **MAY** use this header to replay messages that would have been sent after the last event ID, _on the stream that was disconnected_, and to resume the stream from that point.
>    - The server **MUST NOT** replay messages that would have been delivered on a different stream.
>    - This mechanism applies regardless of how the original stream was initiated (via POST or GET). Resumption is always via HTTP GET with `Last-Event-ID`.
>
> In other words, these event IDs should be assigned by servers on a _per-stream_ basis, to act as a cursor within that particular stream.

2025-11-25 additions vs 2025-06-18: the "event IDs SHOULD encode sufficient information to identify
the originating stream" bullet; "whether due to network failure or server-initiated closure"; and
"Resumption is always via HTTP GET with `Last-Event-ID`".

### 1.13.7 Protocol version header

> If using HTTP, the client **MUST** include the `MCP-Protocol-Version: <protocol-version>` HTTP header on all subsequent requests to the MCP server, allowing the MCP server to respond based on the MCP protocol version.
>
> For example: `MCP-Protocol-Version: 2025-11-25`
>
> The protocol version sent by the client **SHOULD** be the one negotiated during initialization.
>
> For backwards compatibility, if the server does _not_ receive an `MCP-Protocol-Version` header, and has no other way to identify the version - for example, by relying on the protocol version negotiated during initialization - the server **SHOULD** assume protocol version `2025-03-26`.
>
> If the server receives a request with an invalid or unsupported `MCP-Protocol-Version`, it **MUST** respond with `400 Bad Request`.

The header was introduced in 2025-06-18 (changelog major change 8); the `2025-03-26` fallback value is
repeated verbatim in 2025-11-25.

### 1.13.8 SSE event types, and does an `endpoint` event exist?

In the **Streamable HTTP** transport there is **no `endpoint` event**. The spec refers only to:

- "an SSE event consisting of an event ID and an empty `data` field" (priming event, no named `event:` field required);
- an SSE event with a standard `retry` field (per the HTML SSE spec);
- SSE events whose `data` is a JSON-RPC message (`request`, `notification`, or `response`) — these are the default/message events;
- the SSE `id` field used for `Last-Event-ID` resumption.

The only appearances of the word `endpoint` in the 2025-11-25 transports page are (a) "MCP endpoint"
(the single POST+GET URL) and (b) the legacy HTTP+SSE backwards-compatibility clause which says the
client should expect "an `endpoint` event as the first event" from an **old** server.

### 1.13.9 Backwards compatibility probe (2025-11-25 wording)

> 1. Accept an MCP server URL from the user, which may point to either a server using the old transport or the new transport.
> 2. Attempt to POST an `InitializeRequest` to the server URL, with an `Accept` header as defined above:
>    - If it succeeds, the client can assume this is a server supporting the new Streamable HTTP transport.
>    - If it fails with the following HTTP status codes "400 Bad Request", "404 Not Found" or "405 Method Not Allowed":
>      - Issue a GET request to the server URL, expecting that this will open an SSE stream and return an `endpoint` event as the first event.
>      - When the `endpoint` event arrives, the client can assume this is a server running the old HTTP+SSE transport, and should use that transport for all subsequent communication.

(2025-03-26 said "If it fails with an HTTP 4xx status code (e.g., 405 Method Not Allowed or 404 Not
Found)".)

### 1.13.10 stdio transport in 2025-11-25 (changed wording)

> - The server **MAY** write UTF-8 strings to its standard error (`stderr`) for any logging purposes including informational, debug, and error messages.
> - The client **MAY** capture, forward, or ignore the server's `stderr` output and **SHOULD NOT** assume `stderr` output indicates error conditions.

(2025-06-18 said "for logging purposes" without the explicit "informational, debug, and error" list —
this is changelog minor #1.)

### 1.13.11 Sequence diagram (verbatim mermaid, `basic/transports.mdx` lines 224–261)

```
sequenceDiagram
    participant Client
    participant Server

    note over Client, Server: initialization

    Client->>+Server: POST InitializeRequest
    Server->>-Client: InitializeResponse<br>MCP-Session-Id: 1868a90c...

    Client->>+Server: POST InitializedNotification<br>MCP-Session-Id: 1868a90c...
    Server->>-Client: 202 Accepted

    note over Client, Server: client requests
    Client->>+Server: POST ... request ...<br>MCP-Session-Id: 1868a90c...

    alt single HTTP response
      Server->>Client: ... response ...
    else server opens SSE stream
      loop while connection remains open
          Server-)Client: ... SSE messages from server ...
      end
      Server-)Client: SSE event: ... response ...
    end
    deactivate Server

    note over Client, Server: client notifications/responses
    Client->>+Server: POST ... notification/response ...<br>MCP-Session-Id: 1868a90c...
    Server->>-Client: 202 Accepted

    note over Client, Server: server requests
    Client->>+Server: GET<br>MCP-Session-Id: 1868a90c...
    loop while connection remains open
        Server-)Client: ... SSE messages from server ...
    end
    deactivate Server
```

---

## 1.14 Deprecations announced / present in 2025-11-25

| Item | 2025-11-25 status | Exact wording / location |
|---|---|---|
| `includeContext` values `"thisServer"` and `"allServers"` | **soft-deprecated** | `schema/2025-11-25/schema.ts` lines 1592–1595: "Default is `"none"`. Values `"thisServer"` and `"allServers"` are soft-deprecated. Servers SHOULD only use these values if the client declares `ClientCapabilities.sampling.context`. These values may be removed in future spec releases." Also `client/sampling.mdx` lines 81–87 and `basic/index.mdx`. |
| `ClientCapabilities.sampling.context` | soft-deprecated companion flag | `client/sampling.mdx` line 69: "**With context inclusion support (soft-deprecated):**" |
| `LegacyTitledEnumSchema` (`enumNames`) | deprecated in-schema | `schema/2025-11-25/schema.ts` lines 2440–2457: "Use TitledSingleSelectEnumSchema instead. This interface will be removed in a future version." and `enumNames` marked "(Legacy) Display names for enum values. Non-standard according to JSON schema 2020-12." |
| HTTP+SSE transport (from 2024-11-05) | already deprecated since 2025-03-26; still described only as a backwards-compat option | `basic/transports.mdx` line 284: "Clients and servers can maintain backwards compatibility with the deprecated [HTTP+SSE transport]…" |
| Tasks | **experimental**, not deprecated | `basic/utilities/tasks.mdx` lines 7–12 |
| URL mode elicitation | **new**, explicitly flagged as possibly changing | `client/elicitation.mdx` lines 332–336: "**New feature:** URL mode elicitation is introduced in the `2025-11-25` version of the MCP specification. Its design and implementation may change in future protocol revisions." |

There is **no** `deprecated.mdx` page in `docs/specification/2025-11-25/` — the formal deprecation
registry appears only in 2026-07-28 (`docs/specification/2026-07-28/deprecated.mdx`).

---

## 1.15 2025-11-25 vs 2025-06-18 — implementer-facing delta table

| Area | 2025-06-18 | 2025-11-25 |
|---|---|---|
| `initialize` handshake | present | present (unchanged shape) |
| `Implementation` | `extends BaseMetadata { version }` → `name`,`title`,`version` | `extends BaseMetadata, Icons` + `description?` + `websiteUrl?` |
| `Icon`/`icons` on Tool/Resource/ResourceTemplate/Prompt/Implementation | absent | present |
| `Tool` | `name`,`title`,`description`,`inputSchema`,`outputSchema`,`annotations`,`_meta` | + `icons`, + `execution.taskSupport`; `inputSchema`/`outputSchema` gain `$schema?` |
| `CallToolResult` | `content: ContentBlock[]`, `structuredContent?`, `isError?`, `_meta?` | identical |
| `ContentBlock` | `TextContent \| ImageContent \| AudioContent \| ResourceLink \| EmbeddedResource` | identical |
| `SamplingMessage.content` | `TextContent \| ImageContent \| AudioContent` | `SamplingMessageContentBlock \| SamplingMessageContentBlock[]` |
| `stopReason` | `"endTurn" \| "stopSequence" \| "maxTokens" \| string` | + `"toolUse"` |
| sampling `tools` / `toolChoice` | absent | present; requires `ClientCapabilities.sampling.tools` |
| `ClientCapabilities.sampling` | `object` | `{ context?: object; tools?: object }` |
| `ClientCapabilities.elicitation` | `object` | `{ form?: object; url?: object }` |
| `ClientCapabilities.tasks` / `ServerCapabilities.tasks` | absent | present |
| Elicitation request | `message`, `requestedSchema` only (no `mode`) | `mode?: "form"` \| `mode: "url"` + `elicitationId` + `url`; both extend `TaskAugmentedRequestParams` |
| `requestedSchema` | `{ type:"object", properties, required? }` (no `$schema`); `EnumSchema` = `{type:"string",enum[],enumNames?}` | + `$schema?`; enum schemas split into titled/untitled × single/multi-select + `LegacyTitledEnumSchema`; `default` on string/number/boolean/enum |
| `ElicitResult.content` | `{ [key: string]: string \| number \| boolean }` | `{ [key: string]: string \| number \| boolean \| string[] }` |
| `notifications/elicitation/complete` | absent | present |
| `-32042` / `URLElicitationRequiredError` | absent | present |
| Tasks (methods + types + `_meta` keys) | absent | present |
| `Tool.execution.taskSupport` | absent | present |
| `CancelledNotificationParams.requestId` | required `requestId: RequestId` | optional `requestId?: RequestId`, plus rules about tasks |
| JSON-RPC response types | `JSONRPCResponse` and `JSONRPCError` (id required) | `JSONRPCResultResponse` + `JSONRPCErrorResponse` (`id?: RequestId`); `JSONRPCResponse = Result \| Error` union |
| Request typing | RPC interfaces `extends Request` with inline `params` | RPC interfaces `extends JSONRPCRequest` with named `*RequestParams` types (SEP-1319) |
| `PingRequest` | no `params` | `params?: RequestParams` |
| `InitializedNotification` | no `params` | `params?: NotificationParams` |
| HTTP `MCP-Session-Id` | `Mcp-Session-Id` | `MCP-Session-Id` |
| Invalid `Origin` | "MUST validate" | "MUST validate… **MUST** respond with HTTP 403 Forbidden" |
| GET stream polling / `retry` | not specified | specified |
| `Last-Event-ID` resumption | "after a broken connection" | "after a disconnection (whether due to network failure or server-initiated closure)"; "Resumption is always via HTTP GET with `Last-Event-ID`" |
| JSON-RPC batching | removed | removed (still removed) |
| `includeContext` legacy values | normal values | soft-deprecated |
| JSON Schema dialect | unspecified | default 2020-12; implementations MUST support 2020-12 (`basic/index.mdx` §JSON Schema Usage) |

---

# PART 2 — Revision `2024-11-05` (the original)

## 2.0 Sources

| Section | File |
|---|---|
| HTTP+SSE transport | `docs/specification/2024-11-05/basic/transports.mdx` |
| Overview / base protocol | `docs/specification/2024-11-05/basic/index.mdx` |
| Messages | `docs/specification/2024-11-05/basic/messages.mdx` |
| Lifecycle | `docs/specification/2024-11-05/basic/lifecycle.mdx` |
| Tools | `docs/specification/2024-11-05/server/tools.mdx` |
| Resources | `docs/specification/2024-11-05/server/resources.mdx` |
| Prompts | `docs/specification/2024-11-05/server/prompts.mdx` |
| Sampling | `docs/specification/2024-11-05/client/sampling.mdx` |
| Roots | `docs/specification/2024-11-05/client/roots.mdx` |
| Schema (authoritative) | `schema/2024-11-05/schema.ts` (1117 lines) |
| Schema (generated) | `schema/2024-11-05/schema.json` |

Note: `docs/specification/2024-11-05/` contains **no `changelog.mdx`** — there is no predecessor revision
documented in this checkout. The 2024-11-05 revision is the baseline.

---

## 2.1 HTTP+SSE transport — exact mechanics

### 2.1.1 Verbatim normative text

`docs/specification/2024-11-05/basic/transports.mdx`, lines 46–86:

> ## HTTP with SSE
>
> In the **SSE** transport, the server operates as an independent process that can handle
> multiple client connections.
>
> #### Security Warning
>
> When implementing HTTP with SSE transport:
>
> 1. Servers **MUST** validate the `Origin` header on all incoming connections to prevent DNS rebinding attacks
> 2. When running locally, servers **SHOULD** bind only to localhost (127.0.0.1) rather than all network interfaces (0.0.0.0)
> 3. Servers **SHOULD** implement proper authentication for all connections
>
> Without these protections, attackers could use DNS rebinding to interact with local MCP servers from remote websites.
>
> The server **MUST** provide two endpoints:
>
> 1. An SSE endpoint, for clients to establish a connection and receive messages from the
>    server
> 2. A regular HTTP POST endpoint for clients to send messages to the server
>
> When a client connects, the server **MUST** send an `endpoint` event containing a URI for
> the client to use for sending messages. All subsequent client messages **MUST** be sent
> as HTTP POST requests to this endpoint.
>
> Server messages are sent as SSE `message` events, with the message content encoded as
> JSON in the event data.

The stdio section of the same file (lines 17–29) says the server "receives JSON-RPC messages on
its standard input (`stdin`) and writes responses to its standard output (`stdout`)", messages are
newline-delimited and "**MUST NOT** contain embedded newlines", and adds:

> - The server **MUST NOT** write anything to its `stdout` that is not a valid MCP message.
> - The client **MUST NOT** write anything to the server's `stdin` that is not a valid MCP message.

### 2.1.2 Exact SSE event names

| Event name | Direction | Data | Normative basis in 2024-11-05 |
|---|---|---|---|
| `endpoint` | server → client, once | a URI | "the server **MUST** send an `endpoint` event containing a URI for the client to use for sending messages" |
| `message` | server → client, repeated | JSON-RPC message as JSON | "Server messages are sent as SSE `message` events, with the message content encoded as JSON in the event data." |

There are **no other named SSE events** in the 2024-11-05 specification. Grep for `endpoint` across
`docs/specification/2024-11-05/` returns exactly two hits: the normative sentence above (line 67) and
the diagram line `Server->>Client: endpoint event` (line 80).

### 2.1.3 Message-flow diagram (verbatim mermaid, lines 74–86)

```
sequenceDiagram
    participant Client
    participant Server

    Client->>Server: Open SSE connection
    Server->>Client: endpoint event
    loop Message Exchange
        Client->>Server: HTTP POST messages
        Server->>Client: SSE message events
    end
    Client->>Server: Close SSE connection
```

Diagram semantics, literally:

1. Client opens one long-lived HTTP **GET** to the SSE endpoint and holds it open.
2. Server's **first** outbound transmission on that stream is the `endpoint` event (the diagram shows
   exactly one server→client arrow before the loop).
3. Inside the loop, each client message is a **separate HTTP POST** to the URI carried by the
   `endpoint` event.
4. Inside the same loop, server→client traffic arrives as `message` events **on the original GET
   stream** — i.e. POST responses do not carry the JSON-RPC body on the POST itself; the answer
   arrives over the SSE stream.
5. The client closes the SSE connection (or the server closes it; the spec is silent).

### 2.1.4 What the 2024-11-05 spec does **not** say (gap analysis — critical for a C# client)

| Question | 2024-11-05 answer | Where the constraint actually appears |
|---|---|---|
| Response `Content-Type` of the SSE endpoint | **Not stated** | First stated in 2025-03-26 (`basic/transports.mdx` lines 104, 134: "return `Content-Type: text/event-stream`") |
| HTTP status/body of a POST that carries a notification or response | **Not stated — no `202 Accepted` anywhere** | First stated in 2025-03-26 line 96–102 ("the server **MUST** return HTTP status code 202 Accepted with no body") |
| HTTP status of a POST that carries a request | **Not stated** | In 2025-03-26+ the POST itself returns either `text/event-stream` or `application/json`; in 2024-11-05 the result always arrives over the SSE stream |
| May the `endpoint` URI be relative? | **Not stated** — the text says only "a URI" | Nowhere in this checkout. No file contains "relative URI"/"relative URL" in an SSE context. |
| Must `endpoint` be the first event? May other events precede it? | **Not stated** | Only the *client probe* text of later revisions: "Issue a GET request to the server URL, expecting that this will open an SSE stream and return an `endpoint` event as the first event." (2025-03-26 line 274, 2025-06-18 line 283, 2025-11-25 line 306, 2026-07-28 line 734) |
| Can the server push unsolicited requests/notifications on the stream? | Implied by "Server messages are sent as SSE `message` events" | Explicit in 2025-03-26+ |
| Session IDs / `MCP-Protocol-Version` header | **Do not exist** | Introduced in 2025-03-26 / 2025-06-18 |
| Resumability / `Last-Event-ID` | **Does not exist** | Introduced in 2025-03-26 |
| Authorization | "Authentication and authorization are not currently part of the core MCP specification" (`basic/index.mdx` lines 73–81); "Clients and servers **MAY** negotiate their own custom authentication and authorization strategies." | OAuth framework introduced in 2025-03-26 |

**Practical .NET guidance (derived, not quoted):** resolve the `endpoint` URI against the SSE request's
base URI (tolerate absolute and relative), do not assume the POST returns a JSON body, treat the POST
as fire-and-forget and match responses by JSON-RPC `id` arriving on the GET stream, and ignore any
`event:` names other than `endpoint`/`message`.

### 2.1.5 Why the "first event" rule matters

Because 2024-11-05 provides no ordering requirement, and later revisions only require "an `endpoint`
event as the first event" from the client-probe perspective, a 2024-11-05-era C# client should:

- read events until it sees `event: endpoint` (do not fail on interleaved events), and
- send `notifications/initialized` and everything else only after the `endpoint` URI is known and the
  `initialize` handshake completes.

---

## 2.2 Original lifecycle and `initialize` (2024-11-05)

Source: `docs/specification/2024-11-05/basic/lifecycle.mdx`.

Same three phases (Initialization / Operation / Shutdown) and the same `initialize` →
`InitializeResult` → `notifications/initialized` sequence as later revisions, with these differences:

| Aspect | 2024-11-05 |
|---|---|
| Operation-phase obligation | "Both parties **SHOULD**: Respect the negotiated protocol version; Only use capabilities that were successfully negotiated" (**SHOULD**, not MUST) |
| Error handling list | includes "Protocol version mismatch", "Failure to negotiate required capabilities", "Initialize request timeout", "Shutdown timeout"; separate paragraph "Implementations **SHOULD** implement appropriate timeouts for all requests" |
| `instructions` | present and optional in `InitializeResult`, but **not shown** in the lifecycle example response |
| HTTP version header | absent |
| JSON-RPC batching | not mentioned at all |

Request/response examples (`basic/lifecycle.mdx` lines 53–101):

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "initialize",
  "params": {
    "protocolVersion": "2024-11-05",
    "capabilities": {
      "roots": { "listChanged": true },
      "sampling": {}
    },
    "clientInfo": { "name": "ExampleClient", "version": "1.0.0" }
  }
}
```
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "protocolVersion": "2024-11-05",
    "capabilities": {
      "logging": {},
      "prompts": { "listChanged": true },
      "resources": { "subscribe": true, "listChanged": true },
      "tools": { "listChanged": true }
    },
    "serverInfo": { "name": "ExampleServer", "version": "1.0.0" }
  }
}
```
```json
{ "jsonrpc": "2.0", "method": "notifications/initialized" }
```

Initialization failure example (lines 205–217) uses the same `-32602` shape with
`data: { "supported": ["2024-11-05"], "requested": "1.0.0" }`.

Capability table for 2024-11-05 (lines 140–149): client `roots`, `sampling`, `experimental`;
server `prompts`, `resources`, `tools`, `logging`, `experimental`. No `completions`, no
`elicitation`, no `tasks`.

---

## 2.3 Base protocol (2024-11-05)

Source: `docs/specification/2024-11-05/basic/messages.mdx` and `basic/index.mdx`.

> All messages in MCP **MUST** follow the [JSON-RPC 2.0](https://www.jsonrpc.org/specification) specification. The protocol defines three types of messages:

```typescript
// Requests
{
  jsonrpc: "2.0";
  id: string | number;
  method: string;
  params?: {
    [key: string]: unknown;
  };
}

// Responses
{
  jsonrpc: "2.0";
  id: string | number;
  result?: {
    [key: string]: unknown;
  }
  error?: {
    code: number;
    message: string;
    data?: unknown;
  }
}

// Notifications
{
  jsonrpc: "2.0";
  method: string;
  params?: {
    [key: string]: unknown;
  }
}
```

Rules, verbatim:

- "Requests **MUST** include a string or integer ID."
- "Unlike base JSON-RPC, the ID **MUST NOT** be `null`."
- "The request ID **MUST NOT** have been previously used by the requestor within the same session."
- "Responses **MUST** include the same ID as the request they correspond to."
- "Either a `result` or an `error` **MUST** be set. A response **MUST NOT** set both."
- "Error codes **MUST** be integers."
- "Notifications **MUST NOT** include an ID."

Base-protocol overview table (`basic/index.mdx` lines 9–17):

| Type | Description | Requirements |
|---|---|---|
| `Requests` | Messages sent to initiate an operation | Must include unique ID and method name |
| `Responses` | Messages sent in reply to requests | Must include same ID as request |
| `Notifications` | One-way messages with no reply | Must not include an ID |

Protocol layers (verbatim): "All implementations **MUST** support the base protocol and lifecycle
management components. Other components **MAY** be implemented based on the specific needs of the
application."

Error codes declared in `schema/2024-11-05/schema.ts` lines 79–84:
`PARSE_ERROR = -32700`, `INVALID_REQUEST = -32600`, `METHOD_NOT_FOUND = -32601`,
`INVALID_PARAMS = -32602`, `INTERNAL_ERROR = -32603`. No implementation-specific codes are declared.
Resource-not-found is `-32002` (prose, `server/resources.mdx` line 337).

---

## 2.4 Original tools / resources / prompts method shapes (2024-11-05)

Source of truth: `schema/2024-11-05/schema.ts`.

### 2.4.1 `_meta`

`_meta` exists in 2024-11-05 but only as:

```typescript
export interface Request {
  method: string;
  params?: {
    _meta?: {
      progressToken?: ProgressToken;
    };
    [key: string]: unknown;
  };
}

export interface Notification {
  method: string;
  params?: {
    _meta?: { [key: string]: unknown };
    [key: string]: unknown;
  };
}

export interface Result {
  _meta?: { [key: string]: unknown };
  [key: string]: unknown;
}
```

There is **no** `_meta` on `Resource`, `ResourceTemplate`, `ResourceContents`, `Prompt`, `Tool`,
`TextContent`, `ImageContent`, `EmbeddedResource`, or `Root` in 2024-11-05. Note `Request.params._meta`
did **not** have `[key: string]: unknown` inside `_meta` (only `progressToken`), unlike 2025-06-18+.

### 2.4.2 Tools (2024-11-05)

```typescript
export interface ListToolsRequest extends PaginatedRequest { method: "tools/list"; }
export interface ListToolsResult extends PaginatedResult { tools: Tool[]; }

export interface CallToolResult extends Result {
  content: (TextContent | ImageContent | EmbeddedResource)[];
  isError?: boolean;
}

export interface CallToolRequest extends Request {
  method: "tools/call";
  params: {
    name: string;
    arguments?: { [key: string]: unknown };
  };
}

export interface ToolListChangedNotification extends Notification {
  method: "notifications/tools/list_changed";
}

export interface Tool {
  name: string;
  description?: string;
  inputSchema: {
    type: "object";
    properties?: { [key: string]: object };
    required?: string[];
  };
}
```

`docs/specification/2024-11-05/server/tools.mdx` "Data Types → Tool" lists exactly: "`name`:
Unique identifier for the tool; `description`: Human-readable description of functionality;
`inputSchema`: JSON Schema defining expected parameters". Tool result content types: text, image,
embedded resources (no audio, no resource links).

### 2.4.3 Resources (2024-11-05)

```typescript
export interface ListResourcesRequest extends PaginatedRequest { method: "resources/list"; }
export interface ListResourcesResult extends PaginatedResult { resources: Resource[]; }
export interface ListResourceTemplatesRequest extends PaginatedRequest { method: "resources/templates/list"; }
export interface ListResourceTemplatesResult extends PaginatedResult { resourceTemplates: ResourceTemplate[]; }

export interface ReadResourceRequest extends Request {
  method: "resources/read";
  params: { /** @format uri */ uri: string; };
}
export interface ReadResourceResult extends Result {
  contents: (TextResourceContents | BlobResourceContents)[];
}

export interface Resource extends Annotated {
  /** @format uri */ uri: string;
  name: string;
  description?: string;
  mimeType?: string;
  size?: number;
}

export interface ResourceTemplate extends Annotated {
  /** @format uri-template */ uriTemplate: string;
  name: string;
  description?: string;
  mimeType?: string;
}

export interface ResourceContents {
  /** @format uri */ uri: string;
  mimeType?: string;
}
export interface TextResourceContents extends ResourceContents { text: string; }
export interface BlobResourceContents extends ResourceContents { blob: string; }

export interface SubscribeRequest extends Request {
  method: "resources/subscribe";
  params: { /** @format uri */ uri: string; };
}
export interface UnsubscribeRequest extends Request {
  method: "resources/unsubscribe";
  params: { /** @format uri */ uri: string; };
}
export interface ResourceUpdatedNotification extends Notification {
  method: "notifications/resources/updated";
  params: { /** @format uri */ uri: string; };
}
export interface ResourceListChangedNotification extends Notification {
  method: "notifications/resources/list_changed";
}

export interface Annotated {
  annotations?: {
    audience?: Role[];
    priority?: number;   // @minimum 0 @maximum 1
  };
}
```

`Resource.uri` and `Resource.name` are both required; there is **no** `title`, **no** `icons`, and
annotations have **no** `lastModified`.

### 2.4.4 Prompts (2024-11-05)

```typescript
export interface ListPromptsRequest extends PaginatedRequest { method: "prompts/list"; }
export interface ListPromptsResult extends PaginatedResult { prompts: Prompt[]; }

export interface GetPromptRequest extends Request {
  method: "prompts/get";
  params: {
    name: string;
    arguments?: { [key: string]: string };
  };
}
export interface GetPromptResult extends Result {
  description?: string;
  messages: PromptMessage[];
}

export interface Prompt {
  name: string;
  description?: string;
  arguments?: PromptArgument[];
}

export interface PromptArgument {
  name: string;
  description?: string;
  required?: boolean;
}

export interface PromptMessage {
  role: Role;
  content: TextContent | ImageContent | EmbeddedResource;   // NOT ContentBlock
}

export interface PromptListChangedNotification extends Notification {
  method: "notifications/prompts/list_changed";
}
```

No `title`, no `icons`, no `_meta`, no audio content, no `ResourceLink`.

### 2.4.5 Sampling and roots (2024-11-05)

```typescript
export interface CreateMessageRequest extends Request {
  method: "sampling/createMessage";
  params: {
    messages: SamplingMessage[];
    modelPreferences?: ModelPreferences;
    systemPrompt?: string;
    includeContext?: "none" | "thisServer" | "allServers";
    /** @TJS-type number */ temperature?: number;
    maxTokens: number;
    stopSequences?: string[];
    metadata?: object;
  };
}

export interface CreateMessageResult extends Result, SamplingMessage {
  model: string;
  stopReason?: "endTurn" | "stopSequence" | "maxTokens" | string;
}

export interface SamplingMessage {
  role: Role;
  content: TextContent | ImageContent;
}

export interface ModelPreferences {
  hints?: ModelHint[];
  costPriority?: number;         // 0..1
  speedPriority?: number;        // 0..1
  intelligencePriority?: number; // 0..1
}
export interface ModelHint { name?: string; }

export interface ListRootsRequest extends Request { method: "roots/list"; }
export interface ListRootsResult extends Result { roots: Root[]; }
export interface Root {
  /** @format uri */ uri: string;
  name?: string;
}
export interface RootsListChangedNotification extends Notification {
  method: "notifications/roots/list_changed";
}
```

`includeContext` in 2024-11-05 is documented **without** any deprecation note:

> "A request to include context from one or more MCP servers (including the caller), to be attached to the prompt. The client MAY ignore this request."

### 2.4.6 Completion (2024-11-05)

```typescript
export interface CompleteRequest extends Request {
  method: "completion/complete";
  params: {
    ref: PromptReference | ResourceReference;
    argument: { name: string; value: string; };
  };
}
export interface CompleteResult extends Result {
  completion: { values: string[]; total?: number; hasMore?: boolean; };
}
export interface ResourceReference {
  type: "ref/resource";
  /** @format uri-template */ uri: string;
}
export interface PromptReference {
  type: "ref/prompt";
  name: string;
}
```

Note: `ResourceReference` (2024-11-05) → renamed `ResourceTemplateReference` in 2025-06-18+;
`CompleteRequestParams.context` does **not** exist in 2024-11-05 (added 2025-06-18);
`PromptReference` did **not** extend `BaseMetadata` in 2024-11-05 (it declared `name` directly) and
gained `title` in 2025-06-18.

### 2.4.7 Message-type unions (2024-11-05)

```typescript
export type ClientRequest =
  | PingRequest | InitializeRequest | CompleteRequest | SetLevelRequest
  | GetPromptRequest | ListPromptsRequest | ListResourcesRequest
  | ListResourceTemplatesRequest | ReadResourceRequest | SubscribeRequest
  | UnsubscribeRequest | CallToolRequest | ListToolsRequest;

export type ClientNotification =
  | CancelledNotification | ProgressNotification | InitializedNotification
  | RootsListChangedNotification;

export type ClientResult = EmptyResult | CreateMessageResult | ListRootsResult;

export type ServerRequest = PingRequest | CreateMessageRequest | ListRootsRequest;

export type ServerNotification =
  | CancelledNotification | ProgressNotification | LoggingMessageNotification
  | ResourceUpdatedNotification | ResourceListChangedNotification
  | ToolListChangedNotification | PromptListChangedNotification;

export type ServerResult =
  | EmptyResult | InitializeResult | CompleteResult | GetPromptResult
  | ListPromptsResult | ListResourcesResult | ListResourceTemplatesResult
  | ReadResourceResult | CallToolResult | ListToolsResult;
```

`JSONRPCMessage` in 2024-11-05 is `JSONRPCRequest | JSONRPCNotification | JSONRPCResponse | JSONRPCError`
and `JSONRPCError.id` is **required** (`id: RequestId`).

---

## 2.5 Field-availability matrix across revisions

`—` = absent; `✓` = present.

| Field / type | 2024-11-05 | 2025-03-26 | 2025-06-18 | 2025-11-25 |
|---|---|---|---|---|
| `Tool.title` | — | — | ✓ | ✓ |
| `Tool.icons` | — | — | — | ✓ |
| `Tool.outputSchema` | — | — | ✓ | ✓ (`$schema?` added) |
| `Tool.annotations` (`ToolAnnotations`) | — | ✓ | ✓ | ✓ |
| `Tool.execution.taskSupport` | — | — | — | ✓ |
| `Tool._meta` | — | — | ✓ | ✓ |
| `CallToolResult.structuredContent` | — | — | ✓ | ✓ |
| `CallToolResult.isError` | ✓ | ✓ | ✓ | ✓ |
| `CallToolResult.content` type | `(TextContent\|ImageContent\|EmbeddedResource)[]` | `ContentBlock[]` | `ContentBlock[]` | `ContentBlock[]` |
| `AudioContent` | — | ✓ | ✓ | ✓ |
| `ResourceLink` | — | — | ✓ | ✓ |
| `Annotations.lastModified` | — | — | ✓ | ✓ |
| `Resource.title` | — | — | ✓ | ✓ |
| `Resource.icons` | — | — | — | ✓ |
| `Resource.annotations` | ✓ (via `Annotated`) | ✓ (via `Annotated`) | ✓ | ✓ |
| `Resource._meta` | — | — | ✓ | ✓ |
| `ResourceContents._meta` | — | — | ✓ | ✓ |
| `ResourceTemplate.icons` | — | — | — | ✓ |
| `Prompt.title` | — | — | ✓ | ✓ |
| `Prompt.icons` | — | — | — | ✓ |
| `Prompt._meta` | — | — | ✓ | ✓ |
| `PromptArgument.title` | — | — | ✓ | ✓ |
| `TextContent._meta` / `ImageContent._meta` | — | — | ✓ | ✓ |
| `EmbeddedResource.annotations` | ✓ (via `Annotated`) | ✓ | ✓ | ✓ |
| `EmbeddedResource._meta` | — | — | ✓ | ✓ |
| `SamplingMessage._meta` | — | — | — | ✓ |
| `SamplingMessage.content` | `Text\|Image` | `Text\|Image\|Audio` | `Text\|Image\|Audio` | `SamplingMessageContentBlock \| SamplingMessageContentBlock[]` |
| `ToolUseContent` / `ToolResultContent` | — | — | — | ✓ |
| `stopReason` `"toolUse"` | — | — | — | ✓ |
| `Implementation.title` | — | — | ✓ | ✓ |
| `Implementation.icons` | — | — | — | ✓ |
| `Implementation.description` | — | — | — | ✓ |
| `Implementation.websiteUrl` | — | — | — | ✓ |
| `Icon` / `Icons` interfaces | — | — | — | ✓ |
| `Root._meta` | — | — | ✓ | ✓ |
| `elicitation` capability / `elicitation/create` | — | — | ✓ (form only) | ✓ (form + url) |
| `tasks` capability / task methods | — | — | — | ✓ |
| `completions` server capability | — | ✓ | ✓ | ✓ |
| `ProgressNotificationParams.message` | — | ✓ | ✓ | ✓ |
| `CompleteRequestParams.context` | — | — | ✓ | ✓ |
| `PingRequest.params` | — | — | — | ✓ |
| `InitializedNotification.params` | — | — | — | ✓ |
| JSON-RPC batching | — | ✓ | — (removed) | — |

---

## 2.6 JSON-RPC batching: exactly what was added, what was removed, and when

| Revision | Batching status | Evidence |
|---|---|---|
| `2024-11-05` | **Not supported / not mentioned.** No occurrence of "batch" in `docs/specification/2024-11-05/**`. Messages are "individual JSON-RPC requests, notifications, or responses" (`basic/transports.mdx` line 27) and each POST carries one message. | repo-wide grep for `batch` |
| `2025-03-26` | **Added.** Changelog major change: "Added support for JSON-RPC **batching** (PR #228)". Normative rules: `basic/index.mdx` §Batching — "JSON-RPC also defines a means to batch multiple requests and notifications, by sending them in an array. MCP implementations **MAY** support sending JSON-RPC batches, but **MUST** support receiving JSON-RPC batches." `basic/lifecycle.mdx` lines 72–76 — "The initialize request **MUST NOT** be part of a JSON-RPC batch, as other requests and notifications are not possible until initialization has completed. This also permits backwards compatibility with prior protocol versions that do not explicitly support JSON-RPC batches." `basic/transports.mdx` lines 25–27, 90–95, 108–114, 138–140 — batches allowed in stdio, in POST bodies (arrays of requests/notifications, or arrays of responses), and on SSE streams ("These _responses_ **MAY** be batched", "These _requests_ and _notifications_ **MAY** be batched"). | as cited |
| `2025-06-18` | **Removed.** Changelog major change 1: "Remove support for JSON-RPC **batching** (PR #416)". Transport prose reverts to singular: "The body of the POST request **MUST** be a single JSON-RPC _request_, _notification_, or _response_." The `initialize`-must-not-be-batched note disappears with the capability. | `docs/specification/2025-06-18/changelog.mdx` line 12; `docs/specification/2025-06-18/basic/transports.mdx` line 90 |
| `2025-11-25` | Still removed; batching is never mentioned. | `docs/specification/2025-11-25/basic/transports.mdx` line 94 |
| `2026-07-28` | Still removed. | `docs/specification/2026-07-28/basic/transports/streamable-http.mdx` |

Summary sentence for the implementer: **batching existed only in `2025-03-26`, was opt-in for sending
but mandatory for receiving in that revision, and was removed again in `2025-06-18`. No released
revision other than `2025-03-26` permits it. A `2024-11-05` and `2025-11-25` client must send and
accept exactly one JSON-RPC object per frame.**

---

# PART 3 — Version summary table

Published revision directories in this checkout (identical set in all three places —
`docs/specification/`, `schema/`, `docs/docs/`):

```
2024-11-05
2025-03-26
2025-06-18
2025-11-25
2026-07-28
draft
```

`docs/docs.json` `navigation.tabs[].versions[]` lists, in order:
`"Version 2026-07-28 (latest)"`, `"Version 2025-11-25"`, `"Version 2025-06-18"`,
`"Version 2025-03-26"`, `"Version 2024-11-05"`.

| Protocol version string | Release date | Schema file(s) | One-line summary |
|---|---|---|---|
| `2024-11-05` | 2024-11-05 (date is the version string; the original revision — **no changelog, no predecessor** in this checkout) | `schema/2024-11-05/schema.ts` (1117 lines), `schema/2024-11-05/schema.json` | The original: JSON-RPC 2.0 base protocol, stdio + **HTTP+SSE** transports, `roots`/`sampling` client features and `prompts`/`resources`/`tools`/`logging` server features; no OAuth, no batching, no `title`/`icons`/`_meta` on entities, no structured tool output, no elicitation, no tasks. |
| `2025-03-26` | 2025-03-26 | `schema/2025-03-26/schema.ts` (1258 lines), `schema/2025-03-26/schema.json` | Added an OAuth 2.1-based authorization framework; **replaced HTTP+SSE with Streamable HTTP** (single POST+GET MCP endpoint, `Mcp-Session-Id`, `Last-Event-ID` resumability); **added** JSON-RPC batching; added tool annotations; added audio content; added `ProgressNotification.message`; added the `completions` server capability. |
| `2025-06-18` | 2025-06-18 — "Our last spec was released on June 18, 2025" (`blog/content/posts/2025-09-26-mcp-next-version-update.md` line 25) | `schema/2025-06-18/schema.ts` (1613 lines), `schema/2025-06-18/schema.json`, `schema/2025-06-18/schema.mdx` | **Removed** JSON-RPC batching; added structured tool output (`structuredContent` + `Tool.outputSchema`); added resource links (`"resource_link"`); added **elicitation (form mode)**; added `title` for display names; added `CompletionRequest.context`; added `_meta` to more interface types; required the `MCP-Protocol-Version` header; classified MCP servers as OAuth resource servers and required RFC 8707 resource indicators. |
| `2025-11-25` | 2025-11-25 — "we're also releasing a brand-new MCP specification version" dated `2025-11-25T00:00:00+00:00` (`blog/content/posts/2025-11-25-first-mcp-anniversary.md` lines 1–19); the release timeline post states "The specification release date remains to be **November 25th, 2025**" | `schema/2025-11-25/schema.ts` (2582 lines), `schema/2025-11-25/schema.json`, `schema/2025-11-25/schema.mdx` | **Last handshake-era revision** (still `initialize` + `notifications/initialized`). Added icons metadata (SEP-973); `Implementation.description`/`websiteUrl`; **URL mode elicitation** (SEP-1036) with `elicitationId`, `notifications/elicitation/complete` and the `-32042` `URLElicitationRequiredError`; redesigned `EnumSchema`/`ElicitResult` (titled/untitled, single/multi-select) and defaults on all primitive elicitation schemas; **experimental tasks** (SEP-1686); **tool calling in sampling** (`tools`/`toolChoice`, `ToolUseContent`/`ToolResultContent`, `stopReason: "toolUse"`); soft-deprecated `includeContext` `"thisServer"`/`"allServers"`; `MCP-Session-Id` capitalisation; SSE GET-stream polling + `retry`; JSON Schema 2020-12 as the default dialect; standalone `*RequestParams` schemas (SEP-1319); 403 Forbidden for invalid `Origin`; OIDC Discovery + Client ID Metadata Documents; `JSONRPCResultResponse`/`JSONRPCErrorResponse` split with optional error `id`. |
| `2026-07-28` | 2026-07-28 — blog `date: "2026-07-28T09:00:00+00:00"` (`blog/content/posts/2026-07-28-spec-ga/index.md`) | `schema/2026-07-28/schema.ts` (3197 lines), `schema/2026-07-28/schema.json`, `schema/2026-07-28/schema.mdx` | **Removed the `initialize`/`notifications/initialized` handshake and protocol-level sessions** (stateless: version + client capabilities move into `_meta`; `MCP-Session-Id` gone); added `server/discover`; replaced the HTTP GET stream and `resources/subscribe`/`unsubscribe` with `subscriptions/listen`; removed `ping`, `logging/setLevel`, `notifications/roots/list_changed`; moved tasks to an extension (`io.modelcontextprotocol/tasks`, drops `tasks/result` and `tasks/list`, adds `tasks/update`); introduced MRTR (`InputRequiredResult`, `inputRequests`/`inputResponses`, required `resultType`); **removed SSE resumability/`Last-Event-ID`**; removed `notifications/elicitation/complete` and URL-mode `elicitationId`; added cacheable results (`ttlMs`, `cacheScope`); required `Mcp-Method`/`Mcp-Name` headers; deprecated Roots, Sampling, Logging, HTTP+SSE and the legacy `includeContext` values. |
| `draft` | in-progress (unreleased) | `schema/draft/schema.ts` (3197 lines), `schema/draft/schema.json`, `schema/draft/schema.mdx` | The next in-progress revision. `schema/draft/schema.json` is **byte-identical** to `schema/2026-07-28/schema.json` (SHA-256 `EF70B61F99B6D2E5E3B46863822EAB08DFF6A45BEDC7A08914E0E5B133F40203`), but `schema/draft/schema.ts` differs from `schema/2026-07-28/schema.ts` (SHA-256 `B2D3A00D…` vs `742750AF…`), i.e. the draft TS source has moved ahead of the last released JSON. |

Notes on the table:

- Every revision's version string is a date, so the "release date" is the string itself; where a
  separate published date exists in this checkout it is cited above.
- `docs/specification/2024-11-05/` and `docs/specification/2025-03-26/` … all revisions:
  only `2025-03-26`, `2025-06-18`, `2025-11-25` and `2026-07-28` contain a `changelog.mdx`.
  `2024-11-05` has none, and `draft` has none.
- `docs/specification/draft/` and `docs/specification/2026-07-28/` use a restructured navigation
  (`basic/transports/streamable-http.mdx`, `basic/patterns/mrtr.mdx`, `server/discover.mdx`,
  `deprecated.mdx`), which is why the transport file path differs from the `basic/transports.mdx`
  used by 2024-11-05 … 2025-11-25.
