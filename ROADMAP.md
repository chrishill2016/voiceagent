# Roadmap

## Next
- **Entra ID sign-in for the portal.** Protect the portal, API and SignalR hub with Microsoft Entra ID (MSAL in Blazor WebAssembly, JWT bearer on the API). Use the signed-in user's name for **Take call** instead of the name stored in the browser. Webhook and media endpoints stay on their own token or ACS JWT validation.
- **Validate ACS webhook JWTs.** Check the Call Automation callback and media streaming JWTs as well as the shared `?token=`.
- **Light mode.** Add a light/dark toggle to the portal's app bar, stick with dark by default, and remember the choice on each device (or follow the system setting).
- **Real customer data.** Replace the sample dataset with a CRM or database connector behind `ICustomerRepository`, and turn off `Demo__ShowSampleData`.

## Later
- **Scale out.** Add Azure SignalR Service and a shared call-state store so the API can run more than one replica. Until then, keep Container Apps at one replica.
- **Proper agent routing.** Use ACS Job Router with skills-based queues, and let agents answer calls in the browser with the ACS Calling SDK instead of transferring to a phone number.
- **Post-call summary.** Generate a short summary and disposition code into the call record when the call ends.
- **Call recording** with consent prompts, stored in Blob Storage.
- **DTMF fallback** so callers can key in account numbers.
- **Analytics:** containment rate, verification pass rate, average queue wait.
