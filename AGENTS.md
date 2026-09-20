# PriceCheck Collector Next

- This repository is independent from the old `C:\PriceCheck\collector`.
- Keep the desktop UI small and Windows-native. Do not add UI frameworks or
  NuGet packages unless the standard WPF stack cannot provide the feature.
- A collector profile is the unit of configuration and process ownership.
  Never bind one live PID to two profiles.
- Treat `traderKey` (normalized nickname) as durable identity. ObjectID is a
  process/shop-generation token only.
- Radar/broker collection and price verification are separate roles even when
  they share common models.
- Persist user configuration under LocalAppData, never inside the repository.
- The temporary JSON research bridge is replaceable infrastructure. Domain and
  UI code must not depend on the old collector repository.
- Use PowerShell for local commands and `build.ps1` for verification.

