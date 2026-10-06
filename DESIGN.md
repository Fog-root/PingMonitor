# Design System: Raycast Dark Canvas

Reference: https://github.com/VoltAgent/awesome-design-md/blob/main/design-md/raycast/DESIGN.md

## Philosophy
A dark-canvas developer-tools and diagnostic system that treats the interface like a precision instrument: pure deep-ink black backgrounds, hairline 1px borders, crisp typography with tabular numbers, and rare splashes of saturated category accents (emerald green for stability, amber for warning/spikes, coral red for high ping/timeouts).

## Color Tokens

```yaml
colors:
  canvas: "#07080A"            # Deep obsidian window background
  surface: "#0D0E12"           # Header and secondary surfaces
  surface-card: "#121418"      # Card backgrounds
  surface-card-hover: "#181B21" # Card / row hover
  surface-input: "#0A0B0E"     # ComboBox / input field background
  hairline: "#22262C"          # 1px borders
  hairline-soft: "#1A1D23"     # Subtle inner borders / gridlines
  hairline-strong: "#353C46"   # Focus / active borders
  
  text-primary: "#FFFFFF"      # Headings, hero numbers, primary text
  text-secondary: "#A0A8B4"    # Labels, table headers, descriptions
  text-muted: "#606773"        # Footnotes, countdowns, axis labels
  
  accent-green: "#59D499"      # Low ping / stable connection / best route
  accent-green-soft: "#1859D499"
  accent-yellow: "#FFC533"     # Moderate latency / spikes
  accent-yellow-soft: "#18FFC533"
  accent-red: "#FF6161"        # High latency / timeout / critical
  accent-red-soft: "#18FF6161"
  accent-blue: "#57C1FF"       # Info / link / active state
  accent-blue-soft: "#1857C1FF"
```

## Typography
- Font Family: `Segoe UI`, `Inter`, sans-serif
- Monospace / Tabular for numbers and timestamps: `Consolas`, `Cascadia Code`, `Segoe UI Variable`
- Scale:
  - Hero Ping: 72px Bold
  - Section / Card Stat: 24-26px SemiBold
  - Headers: 14-16px SemiBold, tracking: 0.5px
  - Body / Table: 13px Regular
  - Meta / Axis / Status badges: 11-12px Medium

## Layout & Components
- Card Border Radius: 8px to 10px
- Hairline Border Thickness: 1px
- Button: Pill or 6px-8px rounded corners with crisp border and smooth hover elevation
- Table Rows: Alternating subtle hover states with 4px corner padding
- Scrollbars: 5px ultra-compact trackless dark scrollbars
