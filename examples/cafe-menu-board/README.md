# Café Menu Board

A fullscreen digital menu board for restaurants, cafés, and bistros. It shows the restaurant branding, a daily special, categorized menu items with prices, opening hours, and guest WiFi — all configurable through JSON.

![Café Menu Board preview](screenshot.webp)

## Preview

Open `display.html` in your browser. If your browser blocks local JSON files from `file://`, serve this folder with a local static server.

## Send to agentView

Follow the setup and send instructions in the [repository README](../../README.md).

If you upload this through the dashboard, upload the files in `assets/` first and replace the matching relative paths in the HTML with the asset URLs from agentView.

## Customize

> **Tip:** The easiest way to customize this display is with an AI agent connected via [MCP](https://agentview.de/mcp). Share the example files with the agent, describe what you want to change, and the agent will adapt and send it to your display.

Edit `config.json` to change the restaurant name, menu items, prices, and branding. When sending through the dashboard, edit the matching `defaultConfig` object in the `<script>` section instead.

| Setting | Config key |
| --- | --- |
| Restaurant name and tagline | `restaurantName`, `tagline` |
| Hero image URL | `heroImage` |
| Icon font URL | `fontUrl` |
| Currency symbol | `currency` |
| Locale for clock format | `locale` |
| Daily special dish | `dailySpecial` |
| Menu categories and items | `categories` |
| Opening hours | `hours` |
| Guest WiFi network and password | `wifi` |
| Allergen disclaimer | `allergenNote` |
| Optional live JSON feed or agentView Data Slot | `dataUrl` |
| Refresh interval in seconds | `refreshInterval` |

## Menu item format

Each item in a category can have:

```json
{
  "name": "Wild Mushroom Pasta",
  "description": "Fresh tagliatelle, porcini, cream, parmesan",
  "price": 17.50,
  "tags": ["vegetarian"]
}
```

The `description` and `tags` fields are optional. Tags are shown as small badges next to the item name.

## Optional Data Slot

Set `dataUrl` to a public agentView Data Slot URL such as `/data/u/your-public-slug/menu.json`. The JSON can contain any subset of the same keys as `config.json`; missing keys fall back to the sample data.
