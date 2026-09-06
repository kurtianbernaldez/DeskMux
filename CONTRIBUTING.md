# Contributing to DeskMux

Thank you for helping improve DeskMux. Bug reports should include the DeskMux version, Windows version, applications involved, monitor layout and scaling, exact steps, and the expected and actual result. Remove private window titles and paths from logs before attaching them publicly.

## Development

Install the .NET 10 SDK for Windows x64. Build and run the core checks from PowerShell:

```powershell
.\scripts\test.ps1
```

Native integration checks require an unlocked interactive Windows desktop:

```powershell
.\scripts\test.ps1 -Integration
```

Install Node.js 24 to work on the landing page:

```powershell
cd website
npm ci
npm run dev
npm run build
```

Keep changes focused. Preserve the safety rule that DeskMux restores every managed window during normal exit, recovery, and failed layout operations. New native behavior should include core tests where possible and manual checks with ordinary and elevated applications, multiple windows from one process, and more than one monitor when relevant.

Pull requests should explain the user workflow before and after the change, tests performed, and any effect on recovery, persistence, window visibility, focus, pane geometry, or global keyboard handling. By contributing, you agree that your contribution is licensed under the repository's MIT License.
