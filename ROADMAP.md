# Roadmap

The portal and voice agent are for a **conveyancing firm**. Most callers are clients asking for a progress update on their existing case.

## Next
- **Conveyancing sample data and agent instructions.** Replace the broadband-style sample cases with conveyancing matters (sale or purchase, property address, milestones such as searches, enquiries, exchange and completion, plus the next step and who is responsible for it). Update the agent's instructions so it gives a clear progress update in plain English.
- **Entra ID sign-in for the portal.** Protect the portal, API and SignalR hub with Microsoft Entra ID (MSAL in Blazor WebAssembly, JWT bearer on the API). Use the signed-in user's name for **Take call** instead of the name stored in the browser. Webhook and media endpoints stay on their own token or ACS JWT validation.
- **Validate ACS webhook JWTs.** Check the Call Automation callback and media streaming JWTs as well as the shared `?token=`.
- **Light mode.** Add a light/dark toggle to the portal's app bar, stick with dark by default, and remember the choice on each device (or follow the system setting).
- **Real case data.** Replace the sample dataset with a connector to the case management system behind `ICustomerRepository`, and turn off `Demo__ShowSampleData`.
- **Agent status and assignment.** Agents set themselves to Available, Busy or Wrap-up, and queued calls are only offered to available agents. Each agent has their own transfer target, so two agents can take two calls at once (today every taken call goes to one shared `Acs__AgentTransferTarget`).

## For callers
- **Opening hours.** Outside opening hours, the agent still gives progress updates but offers a callback or voicemail instead of queuing callers for agents who aren't there.
- **Callback instead of waiting.** "Press 1 to keep your place and we'll call you back." The callback request shows in the portal queue.
- **Queue position and estimated wait,** read out while the caller is on hold.
- **More languages.** Detect the caller's language and switch voice and transcription to match.
- **Vulnerable client detection.** Spot signs of distress or vulnerability, flag them in the portal, and move the caller up the queue.
- **Text a follow-up.** After the call, send the case reference and a summary of the update by SMS (ACS SMS).
- **Knowledge base answers.** Answer general questions (what searches are, what exchange and completion mean, typical timescales, fees) from the firm's help articles, using retrieval. Case-specific details still need security to pass first.

## For agents
- **Summary when taking a call.** When an agent takes a call, show a short summary of the conversation so far (why they're calling, the case, whether security passed) so the client doesn't have to repeat themselves.
- **Case panel.** The verified client's case details and milestones next to the live transcript.
- **Wrap-up notes and outcome codes** recorded against the call when it ends.
- **Supervisor view.** Listen in on or join a live call, and get alerts for long waits or rising caller frustration.
- **Search and export transcripts,** with filters by date, category, case and outcome.

## Compliance and operations
- **Redact sensitive data** (dates of birth, bank details, memorable words) from stored transcripts.
- **Retention policy.** Delete call records and transcripts automatically after an agreed period, using Cosmos DB's time-to-live setting.
- **Audit log** of who viewed or took which call.
- **Monitoring and alerts.** Application Insights dashboards and alerts on Voice Live failures, failed transfers and long queues.
- **Agent behaviour tests.** Scripted test conversations, run in CI, that check the agent never shares case details before security passes and always offers a person when asked.
- **Prompt versioning.** Try new agent instructions on a share of calls and compare results before switching everyone over.

## Later
- **Scale out.** Add Azure SignalR Service and a shared call-state store so the API can run more than one replica. Until then, keep Container Apps at one replica.
- **Proper agent routing.** Use ACS Job Router with skills-based queues, and let agents answer calls in the browser with the ACS Calling SDK instead of transferring to a phone number.
- **Post-call summary.** Generate a short summary and outcome code into the call record when the call ends.
- **Call recording** with consent prompts, stored in Blob Storage.
- **Keypad entry** so callers can key in case references with their phone keypad (DTMF).
- **Analytics:** share of calls the voice agent resolves without a person, security pass rate, average queue wait.
