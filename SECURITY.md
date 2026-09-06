# Security policy

## Supported versions

DeskMux is currently in alpha. Security fixes are provided for the newest GitHub release.

## Reporting a vulnerability

Please do not open a public issue for an undisclosed vulnerability. Use GitHub's **Security → Report a vulnerability** private reporting form for the repository at https://github.com/kurtian/DeskMux/security/advisories/new.

Include affected versions, impact, reproduction steps, and any proposed mitigation. Remove unrelated personal window titles, paths, and application data. You should receive an acknowledgment within seven days. A fix and disclosure schedule will be coordinated based on severity.

DeskMux controls top-level Windows application windows, installs a global keyboard hook, and stores local window metadata. Reports involving window-identity confusion, unintended process launching, command-line handling, installer upgrades, recovery, or exposure of session and log data are especially useful.
