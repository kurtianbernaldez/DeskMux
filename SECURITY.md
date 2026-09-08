# Security policy

## Supported versions

DeskMux is a pre-1.0 project. Security fixes are provided for the newest GitHub release only.

| Version | Supported |
| --- | --- |
| Latest release | Yes |
| Older releases | No |
| Development builds | Best effort |

Users should update to the newest release before reporting an issue that may already be fixed.

## Reporting a vulnerability

Please **do not open a public issue** for an undisclosed security vulnerability.

Use GitHub's private vulnerability reporting form:

https://github.com/kurtianbernaldez/DeskMux/security/advisories/new

Include as much of the following as possible:

- affected DeskMux version and Windows version
- impact and realistic attack scenario
- exact reproduction steps or a minimal proof of concept
- whether the affected application was elevated or running at the same privilege level as DeskMux
- relevant monitor/session/pane state when the issue occurred
- logs or crash details after removing unrelated private window titles, file paths, arguments, and application data
- any proposed mitigation, if known

You should receive an acknowledgment within **7 days**. Triage, remediation, release, and disclosure timing will depend on severity and reproducibility. Please allow a reasonable period for a fix before public disclosure.

## Security-sensitive areas

DeskMux manages ordinary top-level Windows application windows, installs a global keyboard hook, launches configured executables, persists local window metadata, and includes installer/update and crash-recovery paths.

Reports are especially useful when they involve:

- incorrect window identity or controlling the wrong HWND/process
- unintended application or argument execution
- command-line or launcher handling
- privilege-boundary behavior involving elevated applications
- global hotkey or keyboard-hook behavior
- windows remaining hidden or inaccessible after failure
- recovery journal misuse or unsafe restoration
- installer, update, rollback, or package-integrity behavior
- exposure or unintended transmission of session, path, title, or log data

## Data and privacy

DeskMux stores its session state and diagnostic data locally. Security reports may contain sensitive window titles, executable paths, working directories, or command arguments. Remove unrelated personal or confidential data before attaching logs or screenshots.

Do not publish private user data, credentials, tokens, signing material, or other secrets as part of a report.

## Release integrity and signing

GitHub releases include `SHA256SUMS.txt` so users can verify downloaded file integrity. A checksum confirms that a file matches the published release asset; it does **not** establish publisher identity by itself.

Application and installer signing is used only when the project has a configured Windows code-signing certificate. Unsigned builds may trigger Windows SmartScreen warnings. Download DeskMux only from this repository's official GitHub Releases page or the project website.

## Scope notes

Some Windows restrictions are expected behavior rather than vulnerabilities. In particular, Windows may prevent a normal DeskMux process from inspecting, moving, hiding, or focusing an elevated application, and secure-desktop windows are intentionally outside DeskMux's control.

Compatibility failures, layout bugs, focus problems, or application-specific window behavior that do not create a security impact should be reported through the normal bug-report template instead.
