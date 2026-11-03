# Voice Agent – Client Services Portal

An AI voice agent that answers phone calls for a conveyancing firm, plus a live portal for the client services team. Most callers are clients who want a progress update on their sale, purchase or remortgage.

- **Voice agent** (Azure AI Foundry agent + Voice Live): greets the caller, finds out why they're calling, identifies their matter, asks security questions, then gives a progress update: the stage, what it's waiting on, the next step and target dates.
- **Security is checked on the server.** The model never sees the real answers. It only gets pass/fail, and never learns which answer was wrong.
- **Callers are never locked out.** After 3 failed security attempts, or whenever they ask for a person, the caller is put in the **agent queue**.
- **Agent portal** (Blazor WebAssembly + MudBlazor, dark mode): live calls board, agent queue, agent status (Available / Busy / Wrap-up / Offline) with calls offered to available agents, live transcripts, call timeline, call history, and a **browser test call** so you can try it without a phone number.

## How it works

```
Caller ──PSTN──► Azure Communication Services ──IncomingCall──► Event Grid ──► /api/acs/incoming-call
                         │  answers + bidirectional media streaming (24 kHz PCM)
                         ▼
                /ws/media/{callId}  ◄──────── VoiceAgent.Api (Azure Container Apps) ────────►  Voice Live
                                                 │  tool calls (find_customer, verify_identity, …)     │ (Foundry agent:
                                                 │  transcripts + call state                           │  customer-service-voice-agent)
                                                 ▼
                                   Cosmos DB (calls, clients, agents)    SignalR ──► Agent portal (Blazor WASM)
```

| Project | What it is |
| --- | --- |
| `source/VoiceAgent.Api` | ASP.NET Core API. Handles ACS webhooks, bridges call audio to Voice Live, runs the agent's tools, provides the SignalR hub, and serves the portal. |
| `source/VoiceAgent.Portal` | Blazor WebAssembly PWA for customer service agents. |
| `source/VoiceAgent.Core` | Agent definition (instructions + tools), identity verification, customer model and sample data. |
| `source/VoiceAgent.Shared` | Contracts shared by the API and the portal. |
| `source/VoiceAgent.Provisioning` | Console app that creates the agent in Azure AI Foundry. |
| `tests/VoiceAgent.Tests` | xUnit + bUnit tests. |

### Call flow and stages

`Connecting → Identifying case → Security check → Verified`, with `Waiting for agent → With agent` from any stage.

| Tool | What it does |
| --- | --- |
| `record_call_reason` | Saves a summary and category of why the customer is calling. |
| `find_customer` | Looks up the client by matter reference (e.g. `CV2041`) or client number. Returns only *found / not found*. |
| `verify_identity` | Checks date of birth, postcode and memorable word on the server. After 3 failures the caller is queued for an agent. |
| `get_case_details` | Returns the matter's stage, latest update, what it's waiting on, next step, target dates and milestones, but only once the caller has passed security. |
| `transfer_to_agent` | Puts the caller in the agent queue. |

### Agents and the queue

- Each member of the team sets up a profile in the portal (**Set up agent** in the top bar) with their name and **their own transfer target**: a phone number (`+44…`) or a Teams ID (`8:orgid:…`).
- Agents set their status: **Available**, **Busy**, **Wrap-up** or **Offline**.
- Waiting callers are offered, oldest first, to the **longest-idle available agent**. The agent sees a "Call for you" banner and has 20 seconds to accept (set by `Queue__OfferTimeoutSeconds`).
  - **Decline** passes the call to the next agent.
  - **Not answering** sets the agent Offline and moves the call on.
- On accept (or **Take call** from the queue), the AI stops and Call Automation transfers the phone call to *that agent's* target. The agent is then Busy, and sets themselves Available again when they've finished.
- Several agents can take calls at the same time. The **Agents** page shows everyone's status.

## Run locally

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download) and the Azure CLI (`az login`).

```bash
dotnet run --project source/VoiceAgent.Api
# open http://localhost:5244
```

With no Azure settings, the app uses in-memory storage and the sample clients. To talk to the agent, point it at your Foundry resource and use **Test call** in the portal:

```bash
export VoiceLive__Endpoint=https://<foundry>.services.ai.azure.com
export VoiceLive__ProjectName=<project>
dotnet run --project source/VoiceAgent.Api
```

The **Sample clients** page lists the fictional clients, their matters and their security answers. For example: Amelia Hughes, client number `10234567`, matter `CV2041` (purchase, at searches), born 14 March 1985, postcode `LS1 4AP`, memorable word `lighthouse`.

Run the tests:

```bash
dotnet test VoiceAgent.slnx
```

## Deploy

See **[docs/SETUP.md](docs/SETUP.md)** for the full Azure setup: Foundry, the agent, Cosmos DB, ACS and a phone number, Container Apps, Event Grid and GitHub Actions.

- **Pull request to `main`:** build and test (`.github/workflows/ci.yml`).
- **Merge to `main`:** build, test, build the container image in ACR and deploy to Azure Container Apps (`.github/workflows/deploy.yml`). The version shown in the portal footer is `1.0.<run number>`.

No secrets are stored in the repo. Every Azure service is accessed with managed identity (`DefaultAzureCredential`).

## Configuration

| Setting | Purpose |
| --- | --- |
| `VoiceLive__Endpoint`, `VoiceLive__ProjectName` | Foundry resource endpoint and project name. |
| `VoiceLive__Mode` | `Agent` (default) uses the Foundry agent. `Model` sends the same instructions and tools from the app to a realtime model instead. |
| `VoiceLive__ApiVersion`, `VoiceLive__AuthMode` | Voice Live API version (default `2026-04-10`) and where the token goes (`Header` or `Query`). |
| `Acs__Endpoint`, `Acs__PublicBaseUrl` | ACS endpoint and this app's public https URL. |
| `Acs__CallbackToken` | Shared token on webhook and media URLs. Store it as a Container Apps secret. |
| `Queue__OfferTimeoutSeconds`, `Queue__DeclineCooldownSeconds` | How long an agent has to accept an offered call (default 20s), and how long before a declined call can be offered to the same agent again (default 60s). |
| `Acs__HoldMusicUrl` | Optional WAV file played while the caller waits in the queue. |
| `Cosmos__Endpoint` | Cosmos DB account endpoint. Leave it empty to use in-memory storage. |
| `Demo__SeedSampleData`, `Demo__ShowSampleData` | Load the sample clients, and show the sample data page. Turn both off for real data. |

## Where this differs from the standard app template

- **It has a backend.** Phone calls and live audio need a server, so the portal is served by the API on Azure Container Apps rather than GitHub Pages.
- **It is live-first, not offline-first.** Live calls and transcripts need a connection, so there is no local outbox. The portal is still an installable PWA with dark mode, a version footer and an update prompt. Its connection indicator (Live / Reconnecting / Offline) and notifications replace the usual sync-status indicator.

See [ROADMAP.md](ROADMAP.md) for planned work, including Entra ID sign-in.
