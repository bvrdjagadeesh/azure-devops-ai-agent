# Azure DevOps AI Agent

A **Blazor Server** web application that turns your **Azure DevOps** board into a conversational assistant. Ask about your current sprint, inspect work items, and create tasks — all in natural language, powered by **Azure OpenAI** and **Microsoft Semantic Kernel** function-calling.

> 💬 "What's in the current sprint?"
> 💬 "Show me all bugs assigned to Priya."
> 💬 "Create 3 tasks under #12345: write unit tests, update docs, add telemetry."

---

## ✨ Features

- 🤖 **Natural language chat** over your Azure DevOps board
- 📅 **Sprint insights** — current iteration name, dates, days remaining
- 📋 **Work item queries** — list, filter, and drill into User Stories, Bugs, and Tasks
- ➕ **Create tasks** (single or bulk) with optional parent linking and assignment
- 🧠 **Semantic Kernel agent** with tool/function calling against the live Azure DevOps REST API
- 🔧 **Runtime configuration** — enter your org URL, PAT, project, and team from the UI
- 🌐 **Blazor Server UI** built on .NET 8

---

## 🏗️ Architecture

```
┌────────────────────────┐        ┌───────────────────────────┐
│  Blazor UI (.razor)    │──────▶│ ConfigurationStateService  │
│  Configure / Sprint /  │        │ (holds ADO creds + wires   │
│  WorkItems / Chat      │        │  Kernel + services)        │
└────────────────────────┘        └────────────┬──────────────┘
                                               │
                        ┌──────────────────────┼──────────────────────┐
                        ▼                      ▼                      ▼
              ┌──────────────────┐   ┌───────────────────┐   ┌──────────────────┐
              │  ChatService     │   │ AzureDevOpsPlugin │   │ AzureDevOpsSvc   │
              │  (Semantic       │──▶│ (KernelFunctions) │──▶│ (TFS SDK client) │
              │   Kernel)        │   └───────────────────┘   └────────┬─────────┘
              └────────┬─────────┘                                    │
                       ▼                                              ▼
                ┌──────────────┐                              ┌───────────────┐
                │ Azure OpenAI │                              │ Azure DevOps  │
                │  (gpt-4.1)   │                              │  REST API     │
                └──────────────┘                              └───────────────┘
```

This is **not** a RAG system — there's no vector store. It's an **agent with tools**: the LLM decides which `KernelFunction` (e.g. `get_sprint_work_items`, `create_task`) to invoke, and the plugin hits Azure DevOps live for fresh data.

---

## 🚀 Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- An **Azure OpenAI** resource with a chat-capable deployment (e.g. `gpt-4.1-mini`, `gpt-4o`)
- An **Azure DevOps** organization + a **Personal Access Token (PAT)** with at least:
  - `Work Items` — Read & Write
  - `Project and Team` — Read

### 1. Clone

```bash
git clone https://github.com/<your-user>/azure-devops-ai-agent.git
cd azure-devops-ai-agent
```

### 2. Configure Azure OpenAI (User Secrets — recommended)

**Do not commit secrets to `appsettings.json`.** Use .NET user secrets:

```bash
dotnet user-secrets init
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://<your-resource>.openai.azure.com/"
dotnet user-secrets set "AzureOpenAI:ApiKey" "<your-api-key>"
dotnet user-secrets set "AzureOpenAI:DeploymentName" "gpt-4.1-mini"
```

Or set environment variables:

```bash
AzureOpenAI__Endpoint=...
AzureOpenAI__ApiKey=...
AzureOpenAI__DeploymentName=...
```

### 3. Run

```bash
dotnet run
```

Open the URL shown in the terminal, go to the **Configure** page, and enter:

- **Organization URL** — e.g. `https://dev.azure.com/your-org`
- **Personal Access Token**
- **Project Name**
- **Team Name** (optional)

Then jump to **Sprint** or **Work Items** and start chatting.

---

## 🔌 Available Agent Tools (KernelFunctions)

| Function | Description |
|---|---|
| `get_current_sprint_info` | Current iteration name, dates, days remaining |
| `get_sprint_work_items` | All work items in the current sprint, grouped by type |
| `get_work_item_details` | Full details for a specific work item ID |
| `create_task` | Create a Task, optionally linked to a parent and assigned |
| `create_multiple_tasks` | Bulk-create Tasks under a parent work item |

Add more by extending [`Services/AzureDevOpsPlugin.cs`](Services/AzureDevOpsPlugin.cs).

---

## 📁 Project Structure

```
AzureDevOpsAgentWeb/
├─ Components/
│  ├─ Pages/
│  │  ├─ Home.razor
│  │  ├─ Configure.razor        # Enter ADO org URL / PAT / project / team
│  │  ├─ Sprint.razor           # Current sprint view + chat
│  │  └─ WorkItems.razor        # Work items browser + chat
│  ├─ Layout/MainLayout.razor
│  ├─ App.razor
│  └─ Routes.razor
├─ Services/
│  ├─ ConfigurationStateService.cs   # Runtime state + Kernel wiring
│  ├─ AzureDevOpsService.cs          # ADO SDK wrapper
│  ├─ AzureDevOpsPlugin.cs           # Semantic Kernel plugin (tools)
│  └─ ChatService.cs                 # Kernel chat orchestration
├─ appsettings.json
├─ Program.cs
└─ AzureDevOpsAgentWeb.csproj
```

---

## 🛠️ Tech Stack

- **.NET 8** / **Blazor Server**
- **Microsoft Semantic Kernel** `1.32.0`
- **Microsoft.SemanticKernel.Connectors.AzureOpenAI**
- **Microsoft.TeamFoundationServer.Client** / **Microsoft.VisualStudio.Services.Client** `19.225.1`

---

## 🚢 Deployment

Two Azure App Service **Zip Deploy** publish profiles are included:

- `Properties/PublishProfiles/Sprint - Zip Deploy.pubxml`
- `Properties/PublishProfiles/sprintboard - Zip Deploy.pubxml`

Publish from Visual Studio, or:

```bash
dotnet publish -c Release
```

When deploying, configure `AzureOpenAI:*` values via **App Service → Configuration** (application settings) — **never** commit them.

---

## 🔒 Security Notes

- ⚠️ Rotate any keys or PATs that may have been committed to `appsettings.json` during development.
- Prefer **Azure Key Vault**, **Managed Identity**, or **App Service configuration** for production secrets.
- The Azure DevOps PAT is entered per-session in the UI and held in memory only.

---

## 🗺️ Roadmap Ideas

- [ ] Add RAG over work-item comments & wiki pages (Azure AI Search)
- [ ] Streaming chat responses
- [ ] Persist chat history per user
- [ ] Support Managed Identity for Azure OpenAI
- [ ] More tools: update state, add comments, assign, move iteration
- [ ] Multi-project / multi-team switching

---

## 🤝 Contributing

Issues and PRs welcome! Please open an issue first to discuss significant changes.

---

## 📄 License

MIT — see [LICENSE](LICENSE).
