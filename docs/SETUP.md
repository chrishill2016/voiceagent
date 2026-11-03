# Azure setup guide

This guide sets up everything the voice agent needs, from an empty Azure subscription to answering a real phone call. Most steps are Azure CLI commands, with a few portal steps where the portal is easier.

**Time:** about 45 minutes, plus however long it takes to buy a phone number.

**You need:**
- An Azure subscription where you are Owner (or Contributor + User Access Administrator).
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), [.NET 10 SDK](https://dotnet.microsoft.com/download) and Git.
- Run `az login` first.

> You can test the whole voice flow with the portal's **Test call** page before buying a phone number. Do steps 1–3, then use the "Run locally" section of the README.

---

## 0. Set some variables

Pick a region that supports Voice Live and your model. Sweden Central and East US 2 are good choices. Names marked *unique* must be globally unique.

```bash
RG=rg-voiceagent
LOCATION=swedencentral
SUFFIX=$RANDOM                         # makes names unique
COSMOS=voiceagent-cosmos-$SUFFIX       # unique
ACS=voiceagent-acs-$SUFFIX             # unique
ACR=voiceagentacr$SUFFIX               # unique, letters and numbers only
ENVIRONMENT=voiceagent-env
APP=voiceagent-app

az group create -n $RG -l $LOCATION
for ns in Microsoft.App Microsoft.OperationalInsights Microsoft.Communication Microsoft.EventGrid Microsoft.DocumentDB Microsoft.CognitiveServices; do
  az provider register --namespace $ns
done
```

## 1. Create the Foundry resource and project (portal)

1. Go to **https://ai.azure.com** and select **Create project**.
2. Choose your subscription, resource group `rg-voiceagent` and the same region. This creates a Foundry resource and a project.
3. Go to the project's **Models + endpoints** page and **deploy a model** for the agent's reasoning, for example `gpt-4.1-mini`. Note the **deployment name**.
4. From the project **Overview**, note:
   - the **project endpoint**: `https://<foundry>.services.ai.azure.com/api/projects/<project>`
   - the **Foundry resource name** (`<foundry>`) and the **project name** (`<project>`).

```bash
FOUNDRY=<foundry>
PROJECT=<project>
FOUNDRY_ID=$(az cognitiveservices account show -n $FOUNDRY -g $RG --query id -o tsv)
MY_ID=$(az ad signed-in-user show --query id -o tsv)

# Let yourself create agents and use Voice Live (needed for provisioning and local testing).
az role assignment create --assignee-object-id $MY_ID --assignee-principal-type User --role "Azure AI User" --scope $FOUNDRY_ID
```

## 2. Create the agent in Foundry

The agent's instructions and tools are defined in `source/VoiceAgent.Core/Agent/AgentDefinition.cs`. The provisioning tool creates the agent from that file. Run it again whenever you change the instructions; each run creates a new agent version.

```bash
dotnet run --project source/VoiceAgent.Provisioning -- \
  --project-endpoint "https://$FOUNDRY.services.ai.azure.com/api/projects/$PROJECT" \
  --model gpt-4.1-mini
```

Use `--dry-run` to print the JSON without calling Azure. If your Foundry version rejects the request, you can paste the instructions and tools into the portal by hand.

Then, in the Foundry portal:
1. Open **Agents → customer-service-voice-agent**.
2. If there is a **Voice Live** or **Voice** option, turn it on, and pick the voice `en-GB-SoniaNeural`. The app also sets the voice when a call starts.
3. Try it in the playground: "Hi, I'm calling for an update on my house purchase". The agent should call `record_call_reason`.

## 3. Create Cosmos DB (serverless)

```bash
az cosmosdb create -n $COSMOS -g $RG --locations regionName=$LOCATION --capabilities EnableServerless --disable-key-based-metadata-write-access true
az cosmosdb sql database create -a $COSMOS -g $RG -n voiceagent
az cosmosdb sql container create -a $COSMOS -g $RG -d voiceagent -n calls --partition-key-path /id
az cosmosdb sql container create -a $COSMOS -g $RG -d voiceagent -n customers --partition-key-path /id
az cosmosdb sql container create -a $COSMOS -g $RG -d voiceagent -n agents --partition-key-path /id
COSMOS_ENDPOINT=$(az cosmosdb show -n $COSMOS -g $RG --query documentEndpoint -o tsv)
```

The app uses Entra ID (managed identity) to read and write data. It does not use keys.

## 4. Create Azure Communication Services and get a phone number

```bash
az communication create -n $ACS -g $RG --location global --data-location UK
ACS_ID=$(az communication show -n $ACS -g $RG --query id -o tsv)
ACS_ENDPOINT="https://$(az communication show -n $ACS -g $RG --query hostName -o tsv)"
```

Buy a number in the portal: **Communication Services → your resource → Phone numbers → Get**.
- Choose a number type that can **receive calls (inbound)**.
- Which numbers you can buy depends on your subscription's billing country and type. UK geographic numbers need address verification; toll-free numbers are often the quickest to get.
- If you can't buy a number yet, carry on: browser test calls work without one.

## 5. Create the Container App

```bash
az acr create -n $ACR -g $RG --sku Basic
az acr build -r $ACR -t voiceagent:1.0.0 --build-arg VERSION=1.0.0 .

az containerapp env create -n $ENVIRONMENT -g $RG -l $LOCATION

# Keep it at ONE replica: live call state and SignalR live in memory (see ROADMAP.md).
az containerapp create -n $APP -g $RG --environment $ENVIRONMENT \
  --image $ACR.azurecr.io/voiceagent:1.0.0 \
  --registry-server $ACR.azurecr.io --registry-identity system \
  --system-assigned \
  --ingress external --target-port 8080 \
  --min-replicas 1 --max-replicas 1

APP_FQDN=$(az containerapp show -n $APP -g $RG --query properties.configuration.ingress.fqdn -o tsv)
APP_PRINCIPAL=$(az containerapp show -n $APP -g $RG --query identity.principalId -o tsv)
echo "Portal: https://$APP_FQDN"
```

## 6. Give the app access (managed identity)

```bash
# Voice Live + Foundry agent
az role assignment create --assignee-object-id $APP_PRINCIPAL --assignee-principal-type ServicePrincipal --role "Azure AI User" --scope $FOUNDRY_ID
az role assignment create --assignee-object-id $APP_PRINCIPAL --assignee-principal-type ServicePrincipal --role "Cognitive Services User" --scope $FOUNDRY_ID

# Cosmos DB data (built-in Data Contributor)
az cosmosdb sql role assignment create -a $COSMOS -g $RG --scope "/" \
  --principal-id $APP_PRINCIPAL --role-definition-id 00000000-0000-0000-0000-000000000002

# Call Automation (answer, transfer, play hold music)
az role assignment create --assignee-object-id $APP_PRINCIPAL --assignee-principal-type ServicePrincipal --role "Contributor" --scope $ACS_ID
```

Role assignments can take a few minutes to take effect.

## 7. Configure the app

```bash
CALLBACK_TOKEN=$(openssl rand -hex 24)
az containerapp secret set -n $APP -g $RG --secrets callback-token=$CALLBACK_TOKEN

az containerapp update -n $APP -g $RG --set-env-vars \
  VoiceLive__Endpoint=https://$FOUNDRY.services.ai.azure.com \
  VoiceLive__ProjectName=$PROJECT \
  VoiceLive__Mode=Agent \
  Cosmos__Endpoint=$COSMOS_ENDPOINT \
  Acs__Endpoint=$ACS_ENDPOINT \
  Acs__PublicBaseUrl=https://$APP_FQDN \
  Acs__CallbackToken=secretref:callback-token
```

- Each member of the team sets their own transfer target in the portal (**Set up agent**): their phone number (`+44…`) or Teams user (`8:orgid:<user-object-id>`). Calls they accept are transferred there.
- Optional: `Acs__HoldMusicUrl=https://…/hold.wav` (a public WAV file).

Open `https://$APP_FQDN`, go to **Test call**, and talk to the agent. Use the **Sample clients** page for the security answers.

## 8. Route incoming phone calls to the app (Event Grid)

```bash
az eventgrid event-subscription create \
  --name incoming-calls \
  --source-resource-id $ACS_ID \
  --endpoint "https://$APP_FQDN/api/acs/incoming-call?token=$CALLBACK_TOKEN" \
  --included-event-types Microsoft.Communication.IncomingCall
```

Event Grid sends a validation request first, and the app answers it automatically. Now ring your ACS number: the call appears in the portal straight away.

## 9. Automatic deployment from GitHub (OIDC, no passwords)

```bash
SUB=$(az account show --query id -o tsv)
TENANT=$(az account show --query tenantId -o tsv)
CLIENT_ID=$(az ad app create --display-name voiceagent-github --query appId -o tsv)
az ad sp create --id $CLIENT_ID
az ad app federated-credential create --id $CLIENT_ID --parameters '{
  "name": "voiceagent-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:chrishill2016/voiceagent:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}'
az role assignment create --assignee $CLIENT_ID --role Contributor --scope /subscriptions/$SUB/resourceGroups/$RG
echo "AZURE_CLIENT_ID=$CLIENT_ID AZURE_TENANT_ID=$TENANT AZURE_SUBSCRIPTION_ID=$SUB"
```

In GitHub, go to **Settings → Environments**, create an environment called **`production`**, then:
- **Secrets:** `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`
- **Variables:** `AZURE_RESOURCE_GROUP=rg-voiceagent`, `AZURE_ACR_NAME=<your ACR name>`, `AZURE_CONTAINER_APP=voiceagent-app`

From then on, every merge to `main` builds, tests and deploys. The deploy job is skipped until these variables exist.

---

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| Test call goes straight to **Waiting for agent: Voice agent unavailable** | `VoiceLive__Endpoint` and `VoiceLive__ProjectName` are set, the role assignments from step 6 exist (wait 5 minutes after creating them), and the container logs show the Voice Live error. |
| Voice Live rejects the connection (400/401/404) | The agent connection parameters have changed between API versions. Try a different `VoiceLive__ApiVersion` (for example `2026-06-01-preview` or `2025-10-01`), or `VoiceLive__AuthMode=Query`. |
| The agent talks but the portal shows no reason, matter or security progress | Tool calls aren't reaching the app. Set `VoiceLive__Mode=Model` (and `VoiceLive__Model=gpt-realtime` or another realtime deployment). The app then sends the same instructions and tools itself. |
| Phone rings but isn't answered | Check that the Event Grid subscription exists and that its delivery metrics show successes, that `Acs__PublicBaseUrl` is the https URL of the app, and that the token matches. |
| Queued callers are never offered to anyone | At least one agent must be set to **Available** in the portal. Agents who miss an offer are set to Offline. |
| **Take call** says telephony isn't configured | Set `Acs__Endpoint` and `Acs__PublicBaseUrl`. Browser test calls can still be taken without them. |
| A transfer rings but fails | Check the agent's transfer target on the **Agents** page. Calling out to a phone number needs an ACS number that can make outbound calls. |
| The agent mishears matter references | Callers can say them letter by letter and digit by digit. The app normalises "C V two oh four one" to `CV2041`. |

## Local development against Azure

```bash
az login
export VoiceLive__Endpoint=https://$FOUNDRY.services.ai.azure.com
export VoiceLive__ProjectName=$PROJECT
# optional: export Cosmos__Endpoint=$COSMOS_ENDPOINT   (also give your user the Cosmos data role)
dotnet run --project source/VoiceAgent.Api
```

To receive real phone calls on your machine, expose it with a dev tunnel (`devtunnel host -p 5244 --allow-anonymous`). Then set `Acs__Endpoint`, `Acs__PublicBaseUrl` (the tunnel URL) and an Event Grid subscription that points at the tunnel.
