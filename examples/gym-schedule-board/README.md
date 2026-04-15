# Gym Schedule Board

A high-energy, neon-styled digital signage template designed for fitness clubs and gyms. It features a bold, carbon-fiber inspired design meant to motivate members, displaying the current and upcoming class schedule, club capacity/occupancy, announcements, and a motivational quote.

![Gym Schedule Board preview](screenshot.png)

## Preview

Open [`display.html`](display.html) in your browser. If your browser blocks local JSON files from `file://`, serve this folder with a local static server.

## Send to agentView

Follow the setup and send instructions in the [repository README](../../README.md).

If you upload this through the dashboard, upload the files in `assets/` first and replace the matching relative paths in the HTML with the asset URLs from agentView.

## Customize

> **Tip:** The easiest way to customize this display is with an AI agent connected via [MCP](https://agentview.de/mcp). Share the example files with the agent, describe what you want to change, and the agent will adapt and send it to your display.

Edit `config.json` to alter the gym name, schedule, quote, and occupancy data. When sending through the dashboard, edit the matching `defaultConfig` object in the `<script>` section instead.

| Setting | Config key |
| --- | --- |
| Gym Name | `gymName` |
| Class Schedule | `schedule` |
| Motivational Quote | `motivationalQuote` |
| Club Announcements | `announcements` |
| Gym Occupancy (Percentage) | `gymOccupancy` |
| Theme Colors | `theme` |
| Optional live JSON feed or agentView Data Slot | `dataUrl` |
| Refresh interval in seconds | `refreshInterval` |
