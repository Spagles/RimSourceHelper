# RimSourceHelper

An MCP (Model Context Protocol) server that provides AI coding assistants with deep knowledge of RimWorld's codebase — types, methods, call graphs, Defs, and Harmony patches.

Built for RimWorld mod developers who use AI-assisted coding tools like opencode.

## What It Does

RimSourceHelper analyzes RimWorld's game DLLs and XML definitions, builds a structured knowledge database, and exposes it through 17 MCP tools that an AI agent can query in real-time.

```
Game DLLs + Defs XML
        ↓
   rim-analyzer (Mono.Cecil + ICSharpCode.Decompiler)
        ↓
   SQLite Knowledge Database
        ↓
   MCP Server (17 tools)
        ↓
   AI Agent (opencode, Claude Desktop, etc.)
```

- **rim-analyzer** — C# CLI tool. Analyzes DLLs via Mono.Cecil, decompiles via ICSharpCode.Decompiler
- **MCP Server** — Node.js. Exposes 17 tools over MCP protocol, returns LLM-friendly Markdown

## Requirements

- [.NET 10+](https://dotnet.microsoft.com/) Runtime
- [Node.js 22.5+](https://nodejs.org/) (`node:sqlite` required)

## Build from source

Requires [pnpm](https://pnpm.io/).

```bash
git clone https://github.com/user/RimSourceHelper.git
cd RimSourceHelper
pnpm install
pnpm build
```

Output in `release/`:
```
release/
├── index.js              ← MCP Server (single bundled file)
├── config.json           ← Edit gamePath before use
└── rim-analyzer/
    └── rim-analyzer.dll  ← Analyzer (cross-platform .NET)
```

## Configuration

Edit `release/config.json`:
```json
{
  "gamePath": "<Enter your RimWorld installation directory here>",
  "databasePath": "./rimworld.db",
  "analyzerPath": "./rim-analyzer/rim-analyzer.dll"
}
```

### opencode Integration

Add to your project's `opencode.json`:
```json
{
  "mcp": {
    "RimSourceHelper": {
      "type": "local",
      "command": ["node", "<absolute-path-to>/release/index.js"],
      "enabled": true
    }
  }
}
```

After connecting, ask the agent to call `build_database` to initialize the knowledge base, then use `add_mod` to add any mods you want to analyze.

## MCP Tools (19)

### Search & Discovery
| Tool | Description |
|------|-------------|
| `find_target` | Search types/methods/fields/properties by name with relevance ranking |
| `get_target_info` | Full metadata + inheritance + callers/callees + patches + decompiled source |
| `list_type_members` | List all members of a type |

### Call Graph
| Tool | Description |
|------|-------------|
| `get_callers` | Find callers of a method, field, or property |
| `get_callees` | Find callees of a method; optionally include field/property accesses |
| `get_call_tree` | Recursive call tree with cycle detection |

### Defs
| Tool | Description |
|------|-------------|
| `search_defs` | Search Defs by name or browse all of a DefType |
| `get_def_details` | Full Def details with raw XML |
| `list_def_types` | All DefTypes with counts |
| `find_def_references` | Find Defs referencing a target Def |

### Harmony Patches
| Tool | Description |
|------|-------------|
| `find_harmony_patches` | Find patches on a target type/method with parameter type info |
| `list_harmony_patches` | List patches by source mod |

### XML Patches
| Tool | Description |
|------|-------------|
| `list_xml_patches` | List XML Patches from a mod with pagination |
| `find_xml_patches` | Find XML Patches by def_name |

### Source Code
| Tool | Description |
|------|-------------|
| `decompile_target` | Decompile a type or method; shows field default values |

### Database Management
| Tool | Description |
|------|-------------|
| `build_database` | Build/rebuild from game files. Clears all data. |
| `add_mod` | Add a mod to the knowledge base |
| `remove_mod` | Remove a mod from the knowledge base |
| `list_sources` | Show loaded sources (Core/DLC/Mods) |

## Documentation

- [MCP Tools Reference](packages/mcp-server/README.md) — detailed tool documentation with input/output examples
- [rim-analyzer CLI Reference](packages/rim-analyzer/README.md) — CLI commands for database building, mod analysis, and decompilation

## Project Structure

```
packages/
├── rim-analyzer/     # C# — DLL analysis, decompilation, Harmony scanning
└── mcp-server/       # TypeScript — MCP protocol, 17 tool implementations
```
