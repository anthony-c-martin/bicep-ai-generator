---
name: bicep-generation
description: Generate Azure Bicep infrastructure files using the Bicep Generator MCP tools. Use when creating Azure infrastructure as code, designing cloud architecture, or generating Bicep templates.
---

# Skill Instructions

## Goal

Generate a new, human-maintainable set of `.bicep` files plus a `.bicepparam` file that deploys Azure infrastructure based on user requirements.

## Guiding Principles

- Optimize for maintainability and clarity: split into sensible modules, use descriptive names, and factor repeated patterns.
- Prefer templates that look like they were written by a human, not auto-generated code.
- Always start with example infrastructure and planning before generating resource configurations.

## Required Tools / Commands

Strongly prefer the following tools/commands as part of the workflow, instead of finding other tools or CLI commands to call:

- `get_related_infra_examples` MCP tool: fetch relevant example Bicep templates to understand common patterns for similar infrastructure.
- `generate_infrastructure_plan` MCP tool: gather comprehensive requirements and generate a detailed architecture design.
- `generate_resource_body` MCP tool: get properly configured resource definitions with security best practices applied.
- `get_bicep_best_practices` MCP tool: learn current best practices before generating templates.
- `get_bicep_file_diagnostics` MCP tool: compile/validate Bicep and address errors/warnings after generating files.

## Workflow

### 1) Fetch Example Infrastructure

- Start by fetching relevant example infrastructure using the `get_related_infra_examples` MCP tool to understand common patterns.
- This returns example Bicep templates that demonstrate best practices and common configurations for similar infrastructure.
- Use these examples as reference for structure, naming conventions, and resource configurations.

### 2) Plan the Architecture

- Gather comprehensive requirements using the `generate_infrastructure_plan` MCP tool.
- This returns:
  - Overview of the infrastructure design
  - Complete list of required resources with types and API versions
  - Resource relationships and dependencies
  - Security and configuration requirements
- Explain the proposed architecture to the user before proceeding.

### 3) Generate Resource Configurations

- For each resource in the plan, use the `generate_resource_body` MCP tool to get properly configured resource definitions.
- This returns complete resource body JSON with security best practices applied.
- Ensure all resources from the plan are accounted for.

### 4) Create the Bicep Files

- Generate complete `.bicep` and `.bicepparam` files including:
  - Parameters with @description annotations for configurable values
  - Variables for computed values
  - Resources converted from JSON to Bicep syntax
  - Proper resource dependencies using Bicep references
  - Outputs for important resource properties (IDs, endpoints, URLs)
  - Comments documenting configuration decisions
- Use a main entrypoint (for example `main.bicep`) and split resources into modules by domain if the infrastructure is complex.
- Declare parameter values in the `.bicepparam` file, rather than using default values in the `.bicep` file parameter declarations.

### 5) Validate the Templates

- After generating files, run `get_bicep_file_diagnostics` on the `.bicep` files.
- Fix compilation errors immediately.
- Review warnings and fix anything that could affect correctness, deployment behavior, or maintainability.

## Acceptance Criteria

- A coherent set of `.bicep` files + a `.bicepparam` file exists for the requested infrastructure.
- The Bicep compiles cleanly (diagnostics show no errors; warnings are understood/acceptable).
- All resources from the infrastructure plan are included.
- Security best practices from the generated resource bodies are applied.
