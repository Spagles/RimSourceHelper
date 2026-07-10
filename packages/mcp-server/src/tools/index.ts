import type { Tool } from "@modelcontextprotocol/sdk/types.js";
import type { Config } from "../config.js";
import { findTarget, getTargetInfo, listTypeMembers, decompile } from "./search.js";
import { getCallers, getCallees, getCallTree } from "./callgraph.js";
import { searchDefs, getDefDetails, listDefTypes, findDefReferences } from "./defs.js";
import { findHarmonyPatches, listHarmonyPatches } from "./harmony.js";
import { findXmlPatches, listXmlPatches } from "./defs.js";
import { buildDatabase, addMod, removeMod, listSources } from "./management.js";

// 全部 16 个工具的定义
export const toolDefinitions: Tool[] = [
  // --- 搜索与发现 ---
  {
    name: "find_target",
    description: "Search types/methods/fields/properties by name. Supports FullName exact match and relevance ranking. Use kind to filter.",
    inputSchema: {
      type: "object",
      properties: {
        query: { type: "string", description: "Search keyword" },
        kind: { type: "string", enum: ["type", "method", "field", "property"], description: "Filter by kind" },
        source: { type: "string", description: "Filter by source name (e.g. 'RimWorld Core', 'Anomaly', or Mod's full name)" },
        limit: { type: "number", description: "Max results per kind (default: 20)" },
        offset: { type: "number", description: "Starting position (default: 0)" }
      },
      required: ["query"]
    }
  },
  {
    name: "get_target_info",
    description: "Get full info about a type or method: metadata, inheritance, callers/callees, harmony patches. Set include_source=true for decompiled code (slower).",
    inputSchema: {
      type: "object",
      properties: {
        target: { type: "string", description: "Type FullName or Method FullName/Signature (exact match)" },
        include_source: { type: "boolean", description: "Include decompiled source code (slower, default false)" }
      },
      required: ["target"]
    }
  },
  {
    name: "list_type_members",
    description: "List all members (methods, fields, properties) of a type. For field default values, use decompile_target.",
    inputSchema: {
      type: "object",
      properties: {
        type_name: { type: "string", description: "Type FullName" },
        kind: { type: "string", enum: ["methods", "fields", "properties", "all"], description: "Filter by member kind (default: all)" }
      },
      required: ["type_name"]
    }
  },

  // --- 调用图 ---
  {
    name: "get_callers",
    description: "Find all methods that call the specified method, field, or property. Auto-detects target type.",
    inputSchema: {
      type: "object",
      properties: {
        method: { type: "string", description:         "Method FullName/Signature, or TypeFullName.FieldName, or TypeFullName.PropertyName" },
        limit: { type: "number", description: "Max results (default: 50)" },
        offset: { type: "number", description: "Starting position (default: 0)" }
      },
      required: ["method"]
    }
  },
  {
    name: "get_callees",
    description: "Find all methods that the specified method directly calls. Set include_field_access=true to also see field/property accesses.",
    inputSchema: {
      type: "object",
      properties: {
        method: { type: "string", description: "Method FullName or Signature" },
        limit: { type: "number", description: "Max results (default: 50)" },
        offset: { type: "number", description: "Starting position (default: 0)" },
        include_field_access: { type: "boolean", description: "Also return field/property accesses (default: false)" }
      },
      required: ["method"]
    }
  },
  {
    name: "get_call_tree",
    description: "Recursively expand call chain with cycle detection. Supports methods only (not fields/properties).",
    inputSchema: {
      type: "object",
      properties: {
        method: { type: "string", description: "Method FullName or Signature" },
        direction: { type: "string", enum: ["callers", "callees"], description: "Expand direction" },
        max_depth: { type: "number", description: "Max recursion depth (default: 3)" }
      },
      required: ["method", "direction"]
    }
  },

  // --- Defs ---
  {
    name: "search_defs",
    description: "Search Defs by name, or browse all Defs of a type with empty query and def_type filter.",
    inputSchema: {
      type: "object",
      properties: {
        query: { type: "string", description: "Search keyword (can be empty to list all)" },
        def_type: { type: "string", description: "Filter by DefType (e.g. ThingDef). Use empty query to browse all of a type." },
        limit: { type: "number", description: "Max results (default: 100)" },
        offset: { type: "number", description: "Starting position (default: 0)" }
      },
      required: ["query"]
    }
  },
  {
    name: "get_def_details",
    description: "Get full Def details including raw XML.",
    inputSchema: {
      type: "object",
      properties: {
        def_name: { type: "string", description: "DefName (exact match)" },
        def_type: { type: "string", description: "DefType for disambiguation" }
      },
      required: ["def_name"]
    }
  },
  {
    name: "list_def_types",
    description: "List all Def types and their counts.",
    inputSchema: {
      type: "object",
      properties: {}
    }
  },
  {
    name: "find_def_references",
    description: "Find all Defs that reference the specified Def.",
    inputSchema: {
      type: "object",
      properties: {
        def_name: { type: "string", description: "Target DefName" },
        limit: { type: "number", description: "Max results (default: 50)" },
        offset: { type: "number", description: "Starting position (default: 0)" }
      },
      required: ["def_name"]
    }
  },

  // --- Harmony ---
  {
    name: "find_harmony_patches",
    description: "Find Harmony patches targeting a specific type or method. Shows parameter types for overload disambiguation. At least one of target_type or target_method is required.",
    inputSchema: {
      type: "object",
      properties: {
        target_type: { type: "string", description: "Target type FullName" },
        target_method: { type: "string", description: "Target method name" }
      }
    }
  },
  {
    name: "list_harmony_patches",
    description: "List all Harmony patches, optionally filtered by source mod.",
    inputSchema: {
      type: "object",
      properties: {
        source: { type: "string", description: "Filter by source name (mod name)" },
        limit: { type: "number", description: "Max results (default: 100)" },
        offset: { type: "number", description: "Starting position (default: 0)" }
      }
    }
  },

  // --- 源码 ---
  {
    name: "decompile_target",
    description: "Decompile a type or method on demand. Returns only source code. Use this to see field default values and implementation details.",
    inputSchema: {
      type: "object",
      properties: {
        target: { type: "string", description: "Type FullName or Method FullName/Signature" }
      },
      required: ["target"]
    }
  },

  // --- xml patch ---
  {
    name: "list_xml_patches",
    description: "List XML Patches from a specific mod with pagination.",
    inputSchema: {
      type: "object",
      properties: {
        source: { type: "string", description: "Mod name (as shown in About.xml, e.g. 'RimWorld Core' or Mod's full name)" },
        offset: { type: "number", description: "Starting position (default: 0)" },
        limit: { type: "number", description: "Max results per page (default: 50)" }
      },
      required: ["source"]
    }
  },
  {
    name: "find_xml_patches",
    description: "Find XML Patches by def_name. Set include_raw=true for full XML.",
    inputSchema: {
      type: "object",
      properties: {
        def_name: { type: "string", description: "DefName to search in XPath" },
        source: { type: "string", description: "Filter by source name (e.g. 'RimWorld Core' or Mod's full name)" },
        include_raw: { type: "boolean", description: "Return full RawXml (default: false)" },
        limit: { type: "number", description: "Max results (default: 50)" }
      },
      required: ["def_name"]
    }
  },

  // --- 数据库管理 ---
  {
    name: "build_database",
    description: "Build/rebuild from game files. WARNING: clears ALL existing data including previously added mods.",
    inputSchema: {
      type: "object",
      properties: {}
    }
  },
  {
    name: "add_mod",
    description: "Add a mod's code, Defs, and Harmony patches to the knowledge database. Idempotent (replaces existing data for the same mod).",
    inputSchema: {
      type: "object",
      properties: {
        mod_path: { type: "string", description: "Mod root directory path" }
      },
      required: ["mod_path"]
    }
  },
  {
    name: "remove_mod",
    description: "Remove a mod's data from the knowledge database. Idempotent (no-op if already removed).",
    inputSchema: {
      type: "object",
      properties: {
        mod_name: { type: "string", description: "Mod name (as shown in About.xml)" }
      },
      required: ["mod_name"]
    }
  },

  // --- 信息 ---
  {
    name: "list_sources",
    description: "List all sources (Core, DLCs, Mods) currently in the knowledge database.",
    inputSchema: {
      type: "object",
      properties: {}
    }
  }
];

// 工具调用路由
export async function handleToolCall(
  name: string,
  args: Record<string, unknown> | undefined,
  config: Config
) {
  const safeArgs = args ?? {};

  switch (name) {
    case "find_target": return findTarget(safeArgs, config);
    case "get_target_info": return getTargetInfo(safeArgs, config);
    case "list_type_members": return listTypeMembers(safeArgs, config);
    case "get_callers": return getCallers(safeArgs, config);
    case "get_callees": return getCallees(safeArgs, config);
    case "get_call_tree": return getCallTree(safeArgs, config);
    case "search_defs": return searchDefs(safeArgs, config);
    case "get_def_details": return getDefDetails(safeArgs, config);
    case "list_def_types": return listDefTypes(safeArgs, config);
    case "find_def_references": return findDefReferences(safeArgs, config);
    case "find_harmony_patches": return findHarmonyPatches(safeArgs, config);
    case "list_harmony_patches": return listHarmonyPatches(safeArgs, config);
    case "list_xml_patches": return listXmlPatches(safeArgs, config);
    case "find_xml_patches": return findXmlPatches(safeArgs, config);
    case "decompile_target": return decompile(safeArgs, config);
    case "build_database": return buildDatabase(safeArgs, config);
    case "add_mod": return addMod(safeArgs, config);
    case "remove_mod": return removeMod(safeArgs, config);
    case "list_sources": return listSources(safeArgs, config);
    default:
      return { content: [{ type: "text" as const, text: `Unknown tool: ${name}` }], isError: true };
  }
}
