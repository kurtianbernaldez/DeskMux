# DeskMux website

This folder contains the static landing page for `deskmux.kurtian.dev`. It is deployed as its own Coolify application and is never included in the DeskMux installer or portable ZIP.

```powershell
npm ci
npm run dev
npm run build
```

The production site is written to `dist/client`. Coolify builds the included `Dockerfile` and serves that directory with Nginx. Download buttons point to the stable asset names produced by the DeskMux release workflow.
