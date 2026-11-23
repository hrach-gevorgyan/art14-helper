## What and why

<!-- What does this change, and what problem does it solve? Link the issue if there is one. -->

## Testing

- [ ] The build, the tests and the self-test pass (`build.ps1 -SelfTest`, `dotnet test`)
- [ ] Logic that needs no hardware has tests
- [ ] Tested on a laptop (model and BIOS version: ...), or no hardware is involved

## Checklist

- [ ] No polling timers, no new network use, no HONOR files
- [ ] Admin rights only through `Privileged`, never a UAC prompt for background work
- [ ] README, docs or CHANGELOG updated if users will notice the change
