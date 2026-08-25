# QAD Compile Automation Tool

Deploys QAD program changes to a client's server: connect the VPN, upload the
files, compile them and confirm the compile.

Replaces three steps that were done manually. Dial the VPN, upload in
FileZilla or WinSCP, compile in PuTTY with a single command.

The main command:
qad deploy <client> <environment> <ticket>

or run qad with no arguments and answer three questions.

## Requirements

- The Windows VPN support to use rasdial and the FortiClient to check
  reads Windows network adapters.
- .NET 8 SDK to build, but it is not needed to run: the published build is
  self-contained.
- If you choose to build it you need any IDE that supports .NET
- Network access to the client's server, through whatever VPN it sits behind.

- No FileZilla or WinSCP no PuTTY as well. The tool speaks SFTP and SSH directly.

## Getting it

```powershell
git clone <this repository>
cd QAD-automation-tool

dotnet publish src\QadAutomation.Cli -c Release -r win-x64 --self-contained true -o publish
```
That produces `publish\qad.exe`, which runs on a machine with no .NET installed.

Optionally put `publish` on your PATH so you can type `qad` instead of
`.\publish\qad.exe`, and make a desktop shortcut to `qad.exe` for the guided
flow.

## Configuration

Two files, neither committed:

| File | Holds | Where it goes |
|---|---|---|
| `config.json` | structure: clients, environments, paths, compile recipes | see below |
| `.env` | secrets, referenced from `config.json` as `${VARIABLE}` | beside `config.json` |

Copy `config.example.json` to `config.json` and edit it. Every setting is
commented in that file.

`config.json` is looked for in this order, whichever way you choose to implement it :

1. `--config <path>`
2. the `QAD_TOOL_CONFIG` environment variable
3. the current directory
4. next to the executable
5. `%APPDATA%\QadAutomationTool\`

I suggest the last option , because any rebuilds anywhere else, could delete the config.

Check it loads, and see a redacted summary of what the tool thinks it knows:

```powershell
qad validate
```

## Secrets

Passwords live in `.env` as plain `NAME=value` lines, and `config.json` refers
to them as `${NAME}`.

---

## Running it

### Guided

```powershell
qad
```

Pick a client, an environment and a ticket from numbered lists, look at the
plan, confirm. It loops: Enter returns to the menu for the next ticket, `q`
quits. Good for a desktop shortcut.

### By command

```powershell
qad deploy pilot TEST 9999555            # upload, then compile
qad deploy pilot TEST 9999555 --dry-run  # show the plan, connect to nothing
qad upload  pilot TEST 9999555           # upload only
qad compile pilot TEST 9999555           # compile only
qad check   pilot TEST                   # connect and verify paths, read-only
```

Everything else:

| Command | Does |
|---|---|
| `qad validate` | load the configuration and print a summary |
| `qad tickets` | list ticket folders in the working folder |
| `qad ticket <ticket>` | show how a ticket's files classify as SRC or QRF |
| `qad vpn status\|connect\|disconnect <client>` | manage a client's VPN by hand |
| `qad help` | full usage |

| Option | Does |
|---|---|
| `--dry-run` | print the plan and stop; connects to nothing |
| `--yes` | required to write to a production environment |
| `--no-backup` | skip the local backup of files being replaced |
| `--config <path>` | use a specific configuration file |

---

## Ticket folders

The tool deploys whatever is in a ticket folder, sorted by which sub-folder each
file is in — never by guessing from its name or contents:

```
QAD Tasks/
  Ticket 9999555/
    SRC/                  maintenance programs
      xxfoo.p
    QRF/                  reporting framework programs
      report.p
    _backup/              created by the tool automatically
      TEST-20260101-120000/
        QRF/
          report.p        the version the server had before
```

Only the top level of `SRC/` and `QRF/` is read. `_backup/` sits beside them, not
inside, so a previous version can never be picked up and re-deployed.

---

## What it does

Uploads each file to the remote directory configured for its kind, taking a
copy of anything it is about to replace into the ticket's `_backup/` folder
first. The undo is printed as commands you can paste.

Compiles by whichever procedure the client uses. Three are supported, chosen
per client in configuration:

- an interactive Progress editor, driven by function keys;
- a manifest file plus a build script run once per language;
- a single shell command.

This is the part worth knowing about. A compile is reported as
successful only when the artefact it should have produced actually changed — the
compiled file's timestamp moved, or the command returned a real exit code. What
the screen printed is shown to explain a failure, never to decide one.

Where a program compiles into more than one place, all of them must have changed.

A program it cannot compile is listed with a reason and
makes the run exit non-zero, rather than being quietly skipped. A remote
directory that does not exist stops the run before anything is written, rather
than being created.

---

## Safety

- The plan is printed before anything connects, and `--dry-run` prints the very
  same plan the run would use.
- Production needs `--yes` on the command line, or the environment's name typed
  out in the guided flow. A keystroke is not enough.
- Files being replaced are copied to your machine first.
- The VPN is restored to how it was found — a connection you opened yourself is
  never closed behind you.
- Mistyped options are refused, not ignored. `--dryrun` is an error, because
  silently ignoring it would mean an upload someone believed was a dry run.

---

## Exit codes

| Code | Meaning |
|---|---|
| 0 | fine |
| 1 | configuration problem |
| 2 | wrong usage, or production refused without `--yes` |
| 3 | ticket folder problem |
| 4 | VPN failed |
| 5 | transfer failed |
| 6 | a program did not compile |
| 99 | a bug in the tool |

---

## Repository layout

```
src/QadAutomation.Core/    domain, configuration, VPN, transfer, compile
src/QadAutomation.Cli/     commands and the composition root
tests/                     unit and end-to-end tests
config.example.json        annotated template for config.json
```

Build and test:

```powershell
dotnet build
dotnet test
```
