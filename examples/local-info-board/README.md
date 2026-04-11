# Local Info Board

A fullscreen information display with time, weather, air quality, daylight data, and rotating headlines. Data refreshes every 15 minutes and on wake from sleep. If an API is unavailable, the display shows a status message instead of stale data.

![Local Info Board preview](screenshot.webp)

## Preview

Open `index.html` in your browser. If your browser blocks local JSON files from `file://`, serve this folder with a local static server.

## Send to agentView

Follow the setup and send instructions in the [repository README](../../README.md).

## Customize

Edit `config.json` to change the display. When sending through the dashboard, edit the matching `defaultConfig` object in the `<script>` section instead.

| Setting | Config key |
| --- | --- |
| Location, coordinates, timezone, locale | `locationName`, `coordinates`, `timezone`, `locale` |
| Icon font URL | `fontUrl` |
| Background image URL | `backgroundImage` |
| Temperature unit (celsius / fahrenheit) | `units.temperature` |
| Wind speed unit (kmh / ms / mph / kn) | `units.windSpeed` |
| Weather and air quality APIs | `apis` — uses Open-Meteo, no API key needed |
| News source | `apis.newsUrl` — expects JSON with a `title` field |
| Disable a section | `features.weather`, `features.airQuality`, or `features.news` → `false` |
| Panel rotation speed (seconds) | `rotationInterval` |
| Data refresh interval (minutes) | `refreshInterval` |
| Layout and styling | `<style>` section in `index.html` |
