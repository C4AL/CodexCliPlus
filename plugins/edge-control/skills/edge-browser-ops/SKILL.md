---
name: edge-browser-ops
description: Use the local Edge Control plugin to inspect and operate the local Microsoft Edge browser directly through MCP tools, DOM execution, and CDP commands instead of screenshots.
---

# Edge Browser Ops

Use this skill when the task is to inspect, drive, automate, or crawl through the local Edge browser.

## Workflow

1. Call `edge_bridge_status` first.
2. If the bridge is not ready, tell the user to run the local install/start scripts and ensure the unpacked extension is loaded.
3. Call `edge_list_tabs` to find the relevant tab instead of asking for screenshots.
4. Prefer purpose-built tools:
   - `edge_focus_tab`
   - `edge_navigate`
   - `edge_wait_for`
   - `edge_query`
   - `edge_get_dom`
   - `edge_click`
   - `edge_type`
   - `edge_cdp_evaluate`
5. For crawling, prefer the higher-level tools:
   - `edge_expand_queries`
   - `edge_crawl_snapshot`
   - `edge_crawl_discover_links`
   - `edge_crawl_batch`
   - `edge_crawl_search`
   - `edge_plan_crawl_job`
   - `edge_run_crawl_job`
   - `edge_reload_extension` when updated local extension code needs to become live in the current session
6. Use `edge_send_cdp` when standard DOM actions are insufficient or when you need browser-internal power that exceeds the normal UI.

## Operating Rules

- Prefer reading live DOM state over visual inference.
- Prefer tab IDs returned by `edge_list_tabs` rather than assuming the active tab.
- Never create or rely on a new Edge profile window unless the user explicitly asks for it.
- Always operate in the existing main Edge window and current default profile/session.
- Prefer `edge_cdp_evaluate` over `edge_eval` for robust page-side execution.
- Prefer crawler tools over hand-written one-off page scripts when the task involves many pages, many queries, or repeated extraction.
- Prefer `edge_reload_extension` over browser restarts when the local unpacked Edge Control extension needs to be refreshed.
- Keep page mutations targeted. Do not blast arbitrary code into every tab without need.

## Recovery

- If the extension disconnects, re-check `edge_bridge_status`.
- If updated extension code is on disk but not live yet, call `edge_reload_extension` before considering a full browser relaunch.
- If the page blocks isolated-world behavior, retry with `edge_cdp_evaluate` or `edge_send_cdp`.
- If DOM access is not enough, use `edge_send_cdp` with the correct method and params.
