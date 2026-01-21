---
name: bicep-export
description: Generates a set of .bicep files to replicate existing Azure infrastructure
---

# Skill Instructions

## Goal

Generate a new, human-maintainable set of `.bicep` files plus a `.bicepparam` file that can redeploy the *current live state* of a target Azure resource group as a **no-op** (i.e., deploying should produce no changes).

## Guiding Principles

- Optimize for maintainability and clarity: split into sensible modules, use descriptive names, and factor repeated patterns.
- Prefer templates that look like they were written by a human, not a direct export dump.
- If there is a trade-off between “prettier” and “no-op correctness,” correctness wins.

## Inputs

Ensure you have the subscriptionId and resourceGroup of the resource group to export from the user before starting.

## Required Tools / Commands

Strongly prefer the following tools/commands as part of the workflow, instead of finding other tools or CLI commands to call:

- `get_bicep_best_practices` MCP tool: learn current best practices before refactoring.
- `get_bicep_file_diagnostics` MCP tool: compile/validate Bicep and address errors/warnings during edits.
- `get_snapshot` MCP tool: predict deployment output; take “before” and “after” snapshots and compare.
- `az group export ...` CLI command: export the live resource group to bootstrap the initial template.
- `az deployment group what-if ...` CLI command: run a live diff against Azure using the final `.bicepparam`.

## Workflow

### 1) Bootstrap from Live State (Export)

- Run `az group export ...` against the target resource group to get an initial baseline. This will give you a very rough machine-generated output.
- Use the `--export-format bicep` and `--output tsv` arguments to obtain a `.bicep` file output.
- Only pipe stdout, don't use `2>&1` to pipe stderr to file.
- Put the temporary exported artifact a temp directory, not in the user's workspace.
- Treat export output as input material only; plan to refactor heavily for readability and long-term maintenance.

### 2) Refactor for maintainability

Refactor the exported output into a clean structure:

- Use a main entrypoint (for example `main.bicep`) and split resources into modules by domain (for example: network, compute, storage, identity, monitoring).
- Use parameters and variables judiciously: avoid abstraction for its own sake; prefer clarity.
- Prefer stable, explicit resource names and scopes.
- Preserve drift-sensitive configuration needed for a no-op (examples: identity blocks, SKU/tier, role assignments, locks, private endpoints/DNS, diagnostics settings).
- Use snapshots when embarking on complex refactors to verify that the before+after produce the same desired state.

### 3) Keep the Template Healthy (Diagnostics)

- After each significant change, run `get_bicep_file_diagnostics` on the edited `.bicep` files.
- Fix compilation errors immediately.
- Review warnings and fix anything that could affect correctness, deployment behavior, or maintainability.

### 4) Predict Deployment Output (Snapshots)

- Generate a snapshot for the “before” state (initial structure + parameters) using `get_snapshot`.
- Generate a snapshot for the “after” state (refactored modules + parameters) using `get_snapshot`.
- Compare the predicted resources (IDs/names/types/locations/properties) to verify the refactor preserved semantics.
- Iterate until snapshots are equivalent (or any differences are explicitly intentional and justified).

### 5) Validate Against Live Azure (What-If)

- Once snapshots look equivalent, run `az deployment group what-if ...` using the `.bicepparam` file.
- Review the **full** What-If output.
- The target outcome is a **no-op**: no creates, no deletes, no updates.
- If What-If reports changes, adjust templates/parameters until What-If is clean.

## Acceptance Criteria

- A coherent set of `.bicep` files + a `.bicepparam` file exists for the target resource group.
- The Bicep compiles cleanly (diagnostics show no errors; warnings are understood/acceptable).
- “Before vs after” snapshots are equivalent (or differences are explicitly intentional and justified).
- `az deployment group what-if` indicates the deployment would have **no impact** on live resources.