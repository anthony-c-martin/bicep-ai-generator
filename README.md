# Bicep AI Experiments

Some POCs for enhancing Bicep code generation using agent skills and MCP tools.

## Usage

1. Clone the repo and open in VSCode.
2. Use GitHub Copilot to try out one of the example prompts below.

## Skills

This project includes specialized AI skills for working with Azure Bicep infrastructure as code:

### [bicep-generation](.github/skills/bicep-generation/SKILL.md)

Generate Azure Bicep infrastructure files from scratch using AI-powered planning and resource generation.

**Use when you need to:**
- Create new Azure infrastructure as code
- Design cloud architecture with best practices
- Generate complete Bicep templates with proper configuration

**How it works:**
1. Plans architecture based on your requirements
2. Generates properly configured resource definitions
3. Creates maintainable `.bicep` and `.bicepparam` files with documentation

**Example prompts:**
- "Create a web application with App Service, SQL Database, and Application Insights"
- "Generate Bicep for a secure storage account with private endpoints and encryption"
- "Build infrastructure for a containerized microservice with AKS, ACR, and Key Vault"

### [bicep-export](.github/skills/bicep-export/SKILL.md)

Export existing Azure infrastructure to human-maintainable Bicep files.

**Use when you need to:**
- Convert existing Azure resources to infrastructure as code
- Document current infrastructure state
- Create no-op deployments that match live resources

**How it works:**
1. Exports live resource group state using Azure CLI
2. Refactors machine-generated output into readable, modular code
3. Validates deployment equivalence using snapshots and what-if analysis

**Example prompts:**
- "Export my production resource group to Bicep"
- "Convert the resources in subscription X, resource group Y to maintainable Bicep files"
- "Generate Bicep code that matches my existing Azure infrastructure in the 'app-prod-rg' resource group"