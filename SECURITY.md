# Security policy

Art14 Helper runs with administrator rights (it talks to the BIOS) and installs to Program
Files, so security problems matter. Thank you for reporting them responsibly.

## Supported versions

Only the latest release gets security fixes.

## Reporting a vulnerability

Please **don't open a public issue** for a security problem. Report it privately through
GitHub: the repository's **Security** tab > **Report a vulnerability**. Include:

- what the problem is and what an attacker could do with it;
- the steps to reproduce it, and the app version;
- if you have one, a suggested fix.

You'll get an answer as soon as possible. Once a fix is released, the advisory is
published with credit to you, unless you prefer otherwise.

## In scope

- Anything that lets another program or user gain administrator rights through the app:
  the installed exe, its logon task, the `--set-…` and driver backup command lines, the
  files it writes.
- The driver download checks (HTTPS, SHA-256, digital signatures) and the Drivers page's
  communication with HONOR's servers.
- Leaks of personal data, for example through the log or Copy diagnostics.

## Out of scope

- Problems in Windows, the graphics driver, the BIOS or HONOR's servers themselves.
  Report those to Microsoft, Intel or HONOR.
- Anything that needs administrator rights already.
- The exe not being code-signed. That is deliberate; see the Install section of the
  README. Build from source if you want to be sure what runs.
