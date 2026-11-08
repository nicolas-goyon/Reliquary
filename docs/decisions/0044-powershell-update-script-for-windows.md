# 0044. A PowerShell update script for Windows

- Status: Accepted
- Date: 2026-11-07
- Deciders: @nicolas-goyon

## Context

[ADR 0042](0042-deployed-instances-and-updates.md) gives the script `reliquary-update` for the deployed instances.
It is a POSIX shell script. On Windows, it needs a POSIX shell.

The maintainer uses Windows with Docker Desktop for the pre-production instance. The first installation in Git Bash failed.
Before Git Bash starts a Windows program, it changes each argument that looks like a Linux path to a Windows path.
The script gives Docker two types of paths:

- the paths in the containers, for example `/backups/NAME` and `/app/deploy/compose.yaml`
- the paths of the folder of the instance, for example `/c/Users/NAME/reliquary-preprod/.env`

Git Bash cannot convert the second type and keep the first type. With `MSYS_NO_PATHCONV=1`, Docker gets the first type correctly and the second type incorrectly.

The options are:

1. WSL 2 only. The script works in Ubuntu. But the user must install and learn a Linux system for one script.
2. Support of Git Bash in the POSIX script: convert each path of the instance with `cygpath`. The project cannot test Git Bash in CI, and each new Docker command can bring the problem back.
3. A PowerShell script with the same commands and the same files. Windows contains PowerShell. PowerShell 7 also runs on Linux, so the CI can run the full test of the script.
4. A PowerShell script that starts the POSIX script in a container with the Docker socket. The paths of the bind mounts then come from the container, and Docker Desktop does not know them.

## Decision

- The file `deploy/reliquary-update.ps1` gives the commands of `reliquary-update` for PowerShell: update, `--init`, `--check`, `--status`, `--backup`, `--rollback`, `--recover`, `--import-cards` and `--force`.
- The two scripts use the same files: `.env`, `.state` and `backups`. They use the same steps, the same restore points and the same lock.
- The PowerShell script works with Windows PowerShell 5.1 and with PowerShell 7. It contains only ASCII characters, and it writes the files in UTF-8 with no byte order mark.
- The CLI image contains the two scripts in `/app/deploy`. Each script replaces itself with the script of a new version when this script has a higher revision.
- The CI runs the gate "Update keeps the data" with the two scripts. A job on Windows reads the PowerShell script with Windows PowerShell 5.1, runs PSScriptAnalyzer, and runs `--help` and `--init`.
- The documentation tells Windows users to use PowerShell for a deployed instance, and WSL 2 or Git Bash with `MSYS_NO_PATHCONV=1` for a local instance.

## Consequences

- On Windows, a pre-production instance needs only Docker Desktop and PowerShell.
- A change of the update steps must go into the two scripts. The gate checks the two scripts with the same data, so a difference in the steps makes the CI fail.
- The CI on Windows cannot run Linux containers. It checks the syntax for PowerShell 5.1, not the full update. The full update runs with PowerShell 7 on Linux.
- The CLI images of the releases before this decision do not contain `reliquary-update.ps1`. The documentation tells the user to get the script from the image `develop`. This script works with all the versions.
