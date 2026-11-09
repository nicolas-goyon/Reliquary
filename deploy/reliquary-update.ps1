# reliquary-update.ps1: installs, updates and restores a deployed instance of Reliquary with Docker Compose, from PowerShell.
# It is the PowerShell version of the script reliquary-update. It has the same commands, and it uses the same files.
# See https://nicolas-goyon.github.io/reliquary/deployment/pre-production/, or run ".\reliquary-update.ps1 --help".
#
# The folder of the instance (RELIQUARY_HOME, default: the folder of this script) contains:
#   .env             the settings of the instance (secrets included)
#   .state\          the images and the Compose file of the running version, the restore points and the lock
#   backups\         the backups of the databases (RELIQUARY_BACKUP_DIRECTORY)
#
# Requirements: Docker Desktop (or Docker with the Compose plugin), and Windows PowerShell 5.1 or PowerShell 7.
# The script works also with PowerShell 7 on Linux and macOS.
# Keep this file in ASCII: Windows PowerShell 5.1 reads a file with no byte order mark as ANSI.

# The revision of this script. The script replaces itself only with a script that has a higher revision.
# reliquary-update-revision: 1

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# Variables of the shell must not change the project or the images of the instance.
foreach ($name in @('COMPOSE_PROJECT_NAME', 'COMPOSE_FILE', 'COMPOSE_PROFILES', 'COMPOSE_ENV_FILES',
        'RELIQUARY_IMAGE_API', 'RELIQUARY_IMAGE_WORKER', 'RELIQUARY_IMAGE_CLI', 'RELIQUARY_IMAGE_WEB', 'RELIQUARY_RUNNING_VERSION')) {
    Remove-Item -Path "Env:$name" -ErrorAction SilentlyContinue
}

$script:OnWindows = ($PSVersionTable.PSEdition -eq 'Desktop') -or ((Test-Path -Path 'Variable:IsWindows') -and $IsWindows)
$script:ScriptPath = $PSCommandPath
$script:InstanceHome = [Environment]::GetEnvironmentVariable('RELIQUARY_HOME')
if (-not $script:InstanceHome) {
    $script:InstanceHome = $PSScriptRoot
}
$script:InstanceHome = [System.IO.Path]::GetFullPath($script:InstanceHome)
$script:EnvFile = Join-Path $script:InstanceHome '.env'
$script:State = Join-Path $script:InstanceHome '.state'
$script:RestorePoints = Join-Path $script:State 'restore-points'
$script:Marker = Join-Path $script:State 'in-progress'
$script:Lock = Join-Path $script:State 'lock'
$script:Recovering = $false
$script:Prefix = ''
$script:Project = ''
$script:BackupDirectory = ''
$script:Utf8 = New-Object System.Text.UTF8Encoding($false)

function Show-Usage {
    @'
Usage: .\reliquary-update.ps1 [VERSION]          Update the instance to VERSION, or to RELIQUARY_VERSION of .env.
                                                 VERSION is "develop", "latest" or a release number, for example 0.2.0.
       .\reliquary-update.ps1 --init [VERSION]   Make the .env file with new secrets. The default VERSION is "latest".
       .\reliquary-update.ps1 --check [VERSION]  Tell if a different version is available. Change nothing.
       .\reliquary-update.ps1 --status           Show the running version, the containers and the restore points.
       .\reliquary-update.ps1 --backup [LABEL]   Back up the databases to the backups folder, in a folder DATE-LABEL.
       .\reliquary-update.ps1 --rollback [--yes] Start the previous version again, with the data of the backup before the update.
       .\reliquary-update.ps1 --recover          Finish an update or a rollback that stopped: restore its backup and start its version.
       .\reliquary-update.ps1 --import-cards     Import all the cards from Scryfall now.
       .\reliquary-update.ps1 --force [VERSION]  Update even when the images did not change.

Environment: RELIQUARY_HOME (folder of the instance), RELIQUARY_PULL=false (use the local images only),
RELIQUARY_SELF_UPDATE=false (do not replace this script with the script of the new version).
'@ | Write-Host
}

function Write-UpdateLog([string] $Message) {
    Write-Host "reliquary-update: $Message"
}

# Stops the command. The main block writes the message and sets the exit code 1.
function Stop-WithError([string] $Message) {
    throw (New-Object System.InvalidOperationException($Message))
}

# Runs Docker and shows its output. Stops the command when Docker fails.
function Invoke-Docker {
    & docker @args | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Stop-WithError "The command ""docker $($args -join ' ')"" failed with the exit code $LASTEXITCODE."
    }
}

# Runs Docker and returns its standard output. Stops the command when Docker fails.
function Get-DockerOutput {
    $output = & docker @args
    if ($LASTEXITCODE -ne 0) {
        Stop-WithError "The command ""docker $($args -join ' ')"" failed with the exit code $LASTEXITCODE."
    }
    return $output
}

# Runs Docker with no output. Returns $true when Docker succeeds.
# Windows PowerShell 5.1 stops on a redirected error stream when ErrorActionPreference is "Stop", so this function uses "Continue".
function Test-Docker {
    $ErrorActionPreference = 'Continue'
    & docker @args *> $null
    return ($LASTEXITCODE -eq 0)
}

function Write-TextFile([string] $Path, [string] $Text) {
    [System.IO.File]::WriteAllText($Path, $Text, $script:Utf8)
}

function Add-TextLine([string] $Path, [string] $Line) {
    [System.IO.File]::AppendAllText($Path, "$Line`n", $script:Utf8)
}

function New-Folder([string] $Path) {
    [System.IO.Directory]::CreateDirectory($Path) | Out-Null
}

function Remove-Folder([string] $Path) {
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Copy-Folder([string] $Source, [string] $Destination) {
    Remove-Folder $Destination
    Copy-Item -LiteralPath $Source -Destination $Destination -Recurse
}

function Move-Folder([string] $Source, [string] $Destination) {
    [System.IO.Directory]::Move($Source, $Destination)
}

# The name of a new backup: the UTC date, then a label. The order of the names is the order of the dates.
function Get-BackupName([string] $Label) {
    return [DateTime]::UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'") + "-$Label"
}

# Returns the value of a setting: the environment first, then the .env file, then the default.
function Get-Setting([string] $Name, [string] $Default = '') {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if (-not $value -and (Test-Path -LiteralPath $script:EnvFile)) {
        $pattern = '^\s*(export\s+)?' + [regex]::Escape($Name) + '=(.*)$'
        foreach ($line in [System.IO.File]::ReadAllLines($script:EnvFile)) {
            $match = [regex]::Match($line.TrimEnd("`r"), $pattern)
            if ($match.Success) {
                $value = $match.Groups[2].Value
            }
        }
        if ($value) {
            if ($value -match '^"([^"]*)"') {
                $value = $Matches[1]
            }
            elseif ($value -match "^'([^']*)'") {
                $value = $Matches[1]
            }
            else {
                $value = ($value -replace '\s+#.*$', '').Trim()
            }
        }
    }
    if (-not $value) {
        $value = $Default
    }
    return $value
}

function Assert-Docker {
    if (-not (Get-Command -Name docker -ErrorAction SilentlyContinue)) {
        Stop-WithError 'Docker is not installed. Install Docker Desktop.'
    }
    if (-not (Test-Docker compose version)) {
        Stop-WithError 'The Docker Compose plugin is not installed.'
    }
    if (-not (Test-Docker info)) {
        Stop-WithError 'Docker does not answer. Start Docker Desktop, wait until the engine runs, then try again.'
    }
}

function Assert-Env {
    if (-not (Test-Path -LiteralPath $script:EnvFile)) {
        Stop-WithError "$($script:EnvFile) does not exist. Run "".\reliquary-update.ps1 --init"" first."
    }
    if (-not (Get-Setting 'RELIQUARY_SIGNING_KEY')) {
        Stop-WithError "Set RELIQUARY_SIGNING_KEY in $($script:EnvFile)."
    }
    if (-not (Get-Setting 'RELIQUARY_POSTGRES_PASSWORD')) {
        Stop-WithError "Set RELIQUARY_POSTGRES_PASSWORD in $($script:EnvFile)."
    }
}

function Read-Settings {
    $script:Prefix = Get-Setting 'RELIQUARY_IMAGE_PREFIX' 'ghcr.io/nicolas-goyon/reliquary'
    $script:Project = Get-Setting 'RELIQUARY_PROJECT' 'reliquary-preprod'
    $folder = Get-Setting 'RELIQUARY_BACKUP_DIRECTORY' './backups'
    if (-not [System.IO.Path]::IsPathRooted($folder)) {
        $folder = Join-Path $script:InstanceHome $folder
    }
    $folder = [System.IO.Path]::GetFullPath($folder).TrimEnd('\', '/')
    if ($folder -eq $script:InstanceHome.TrimEnd('\', '/')) {
        Stop-WithError 'RELIQUARY_BACKUP_DIRECTORY must be a folder of its own, not the folder of the instance.'
    }
    $script:BackupDirectory = $folder
    # Compose reads the variables of the shell before the .env file, so the backup folder is always absolute.
    $env:RELIQUARY_BACKUP_DIRECTORY = $folder
    New-Folder $folder
    if (-not $script:OnWindows) {
        # The CLI container runs with the user of this script, so only this user can read and write the backups.
        & chmod 700 $folder
    }
}

# Takes the lock of the instance, then runs the command. Two runs of the script cannot change the instance at the same time.
# When the command stops before its end and an operation is recorded, the script restores the state before the operation.
function Invoke-WithLock([scriptblock] $Command) {
    New-Folder $script:State
    try {
        New-Item -ItemType Directory -Path $script:Lock -ErrorAction Stop | Out-Null
    }
    catch {
        Stop-WithError "An other run of reliquary-update uses the instance. If no other run exists, delete the folder $($script:Lock)."
    }
    $completed = $false
    try {
        & $Command
        $completed = $true
    }
    finally {
        if ((-not $completed) -and (Test-Path -LiteralPath $script:Marker) -and (-not $script:Recovering)) {
            $operation = Get-MarkerValue 'RECOVER_OPERATION'
            try {
                Restore-Operation
                [Console]::Error.WriteLine("reliquary-update: error: the $operation failed. The previous state runs again. Read the messages above.")
            }
            catch {
                [Console]::Error.WriteLine("reliquary-update: error: the recovery failed: $($_.Exception.Message)")
                [Console]::Error.WriteLine('reliquary-update: error: correct the problem, then run ".\reliquary-update.ps1 --recover".')
            }
        }
        Remove-Item -LiteralPath $script:Lock -Force -ErrorAction SilentlyContinue
    }
}

# Returns the folder of a state: "current", "next" or the path of a state folder.
function Get-StateFolder([string] $Name) {
    if ($Name -eq 'current' -or $Name -eq 'next') {
        return (Join-Path $script:State $Name)
    }
    return $Name
}

# Returns the arguments of Docker Compose with the images of a state.
function Get-ComposeArguments([string] $StateName) {
    $folder = Get-StateFolder $StateName
    return @('compose', '--project-name', $script:Project, '--project-directory', $script:InstanceHome,
        '--file', (Join-Path $folder 'compose.yaml'), '--env-file', $script:EnvFile, '--env-file', (Join-Path $folder 'images.env'))
}

function Invoke-Compose([string] $StateName) {
    $arguments = (Get-ComposeArguments $StateName) + $args
    Invoke-Docker @arguments
}

# Runs the CLI of a state. On Linux and macOS, the CLI runs as the user of this script.
function Invoke-Cli([string] $StateName) {
    $user = @()
    if (-not $script:OnWindows) {
        $user = @('--user', "$(& id -u):$(& id -g)")
    }
    $arguments = (Get-ComposeArguments $StateName) + @('run', '--rm') + $user + @('cli') + $args
    Invoke-Docker @arguments
}

# Returns a reference that does not move: the digest of the image in the registry, or else the local image ID.
function Get-PinnedReference([string] $Image) {
    $repository = $Image.Substring(0, $Image.LastIndexOf(':'))
    $digests = (Get-DockerOutput image inspect --format '{{json .RepoDigests}}' $Image) -join ''
    foreach ($digest in @(ConvertFrom-Json -InputObject $digests)) {
        if ($digest -and $digest.StartsWith("$repository@")) {
            return $digest
        }
    }
    return ((Get-DockerOutput image inspect --format '{{.Id}}' $Image) -join '').Trim()
}

# Returns the version of the CLI image, for example 0.2.0 or 0.2.0-develop.42.
function Get-ImageVersion([string] $Image, [string] $Default) {
    $ErrorActionPreference = 'Continue'
    $output = & docker run --rm $Image --version 2> $null
    $version = ''
    if ($LASTEXITCODE -eq 0 -and $output) {
        $version = (@($output)[0] -split '\+')[0].Trim()
    }
    if (-not $version) {
        $version = $Default
    }
    return $version
}

# Gets the images of a version and writes a state folder: images.env, compose.yaml and the scripts of this version.
# Then it checks the Compose file with the settings of the instance. It changes nothing in the instance.
function Initialize-State([string] $Version, [string] $Folder) {
    Remove-Folder $Folder
    New-Folder $Folder
    $images = Join-Path $Folder 'images.env'
    Write-TextFile $images ''
    foreach ($service in @('api', 'worker', 'cli', 'web')) {
        $image = "$($script:Prefix)-$($service):$Version"
        if ((Get-Setting 'RELIQUARY_PULL' 'true') -ne 'false') {
            Write-UpdateLog "Pull $image."
            if (-not (Test-Docker pull --quiet $image)) {
                Stop-WithError "Cannot pull $image. Check the version and the network."
            }
        }
        if (-not (Test-Docker image inspect $image)) {
            Stop-WithError "The image $image does not exist."
        }
        $reference = Get-PinnedReference $image
        if (-not $reference) {
            Stop-WithError "Cannot read the digest of $image."
        }
        Add-TextLine $images "RELIQUARY_IMAGE_$($service.ToUpperInvariant())=$reference"
    }
    $cli = "$($script:Prefix)-cli:$Version"
    Add-TextLine $images "RELIQUARY_RUNNING_VERSION=$(Get-ImageVersion $cli $Version)"
    Add-TextLine $images "RELIQUARY_RUNNING_CHANNEL=$Version"
    Add-TextLine $images "RELIQUARY_INSTALLED_AT=$([DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))"

    # The CLI image contains the Compose file and the scripts of its version.
    $container = ((Get-DockerOutput create $cli) -join '').Trim()
    try {
        if (-not (Test-Docker cp "$($container):/app/deploy/compose.yaml" (Join-Path $Folder 'compose.yaml'))) {
            Stop-WithError "The image $cli has no Compose file. Use a version that is more recent."
        }
        Test-Docker cp "$($container):/app/deploy/reliquary-update.ps1" (Join-Path $Folder 'reliquary-update.ps1') | Out-Null
    }
    finally {
        Test-Docker rm $container | Out-Null
    }

    $arguments = (Get-ComposeArguments $Folder) + @('config', '--quiet')
    $ErrorActionPreference = 'Continue'
    & docker @arguments
    if ($LASTEXITCODE -ne 0) {
        Stop-WithError "The Compose file of $Version does not work with $($script:EnvFile). Read the message above, and add the missing settings."
    }
}

function Get-StateValue([string] $Folder, [string] $Name) {
    $file = Join-Path $Folder 'images.env'
    $value = ''
    if (Test-Path -LiteralPath $file) {
        foreach ($line in [System.IO.File]::ReadAllLines($file)) {
            if ($line.StartsWith("$Name=")) {
                $value = $line.Substring($Name.Length + 1)
            }
        }
    }
    return $value
}

function Get-Images([string] $Folder) {
    $file = Join-Path $Folder 'images.env'
    if (-not (Test-Path -LiteralPath $file)) {
        return ''
    }
    return ((@([System.IO.File]::ReadAllLines($file)) | Where-Object { $_.StartsWith('RELIQUARY_IMAGE_') }) -join "`n")
}

function Test-Installed {
    return (Test-Path -LiteralPath (Join-Path $script:State 'current/images.env'))
}

function Backup-Databases([string] $StateName, [string] $Label) {
    Write-UpdateLog "Back up the databases to $(Join-Path $script:BackupDirectory $Label)."
    Invoke-Compose $StateName up --detach --wait postgres redis
    Invoke-Cli $StateName backup "/backups/$Label"
}

function Stop-Application([string] $StateName) {
    Write-UpdateLog 'Stop the API, the worker and the web app.'
    Invoke-Compose $StateName stop api worker web
}

# Records the state and the backup that give a working instance again, until the operation ends.
function Start-Operation([string] $Folder, [string] $Backup, [string] $Operation) {
    Write-TextFile $script:Marker "RECOVER_STATE=$Folder`nRECOVER_BACKUP=$Backup`nRECOVER_OPERATION=$Operation`n"
}

function Complete-Operation {
    Remove-Item -LiteralPath $script:Marker -Force -ErrorAction SilentlyContinue
}

function Get-MarkerValue([string] $Name) {
    $value = ''
    if (Test-Path -LiteralPath $script:Marker) {
        foreach ($line in [System.IO.File]::ReadAllLines($script:Marker)) {
            if ($line.StartsWith("$Name=")) {
                $value = $line.Substring($Name.Length + 1)
            }
        }
    }
    return $value
}

# Restores the backup of the marker, and starts the version of the marker. The state "current" becomes this version.
function Restore-Operation {
    $folder = Get-MarkerValue 'RECOVER_STATE'
    $backup = Get-MarkerValue 'RECOVER_BACKUP'
    $operation = Get-MarkerValue 'RECOVER_OPERATION'
    $current = Join-Path $script:State 'current'
    if (-not (Test-Path -LiteralPath (Join-Path $folder 'images.env'))) {
        Stop-WithError "The state $folder of the stopped $operation does not exist."
    }
    Write-UpdateLog "The $operation did not end. Restore the backup $backup and start version $(Get-StateValue $folder 'RELIQUARY_RUNNING_VERSION') again."
    foreach ($name in @('current', 'next')) {
        if (Test-Path -LiteralPath (Join-Path (Get-StateFolder $name) 'compose.yaml')) {
            $arguments = (Get-ComposeArguments $name) + @('stop', 'api', 'worker', 'web')
            Test-Docker @arguments | Out-Null
        }
    }
    Invoke-Compose $folder up --detach --wait postgres redis mailpit
    Invoke-Cli $folder restore "/backups/$backup" --replace
    Invoke-Cli $folder migrate
    Invoke-Compose $folder up --detach --wait --remove-orphans
    if ([System.IO.Path]::GetFullPath($folder) -ne [System.IO.Path]::GetFullPath($current)) {
        $new = Join-Path $script:State 'current.new'
        Copy-Folder $folder $new
        Remove-Folder $current
        Move-Folder $new $current
    }
    # The version of the restore point runs again, so the restore point is not necessary.
    if ([System.IO.Path]::GetFullPath($folder).StartsWith([System.IO.Path]::GetFullPath($script:RestorePoints))) {
        Remove-Folder $folder
    }
    Remove-Folder (Join-Path $script:State 'next')
    Remove-Folder (Join-Path $script:State 'before-rollback')
    Complete-Operation
    Write-UpdateLog "Version $(Get-StateValue $current 'RELIQUARY_RUNNING_VERSION') runs again with the data of the backup $backup."
}

# Checks the state at the start of a command that changes the instance.
function Assert-State {
    $current = Join-Path $script:State 'current'
    $previous = Join-Path $script:State 'previous'
    # A run stopped between the two renames of the state "current".
    if (-not (Test-Path -LiteralPath $current) -and (Test-Path -LiteralPath $previous)) {
        Move-Folder $previous $current
    }
    if (Test-Path -LiteralPath $script:Marker) {
        $script:Recovering = $true
        Stop-WithError "An earlier $(Get-MarkerValue 'RECOVER_OPERATION') did not end. Run "".\reliquary-update.ps1 --recover""."
    }
}

function Get-NewestRestorePoint {
    if (-not (Test-Path -LiteralPath $script:RestorePoints)) {
        return ''
    }
    $points = @(Get-ChildItem -LiteralPath $script:RestorePoints -Directory | Sort-Object -Property Name)
    if ($points.Count -eq 0) {
        return ''
    }
    return $points[-1].Name
}

# Deletes the older backups and their restore points. RELIQUARY_KEEP_BACKUPS gives the number to keep.
# The script deletes only the folders of backups that it made, and never the backup of the newest restore point.
function Remove-OldBackups {
    $keepText = Get-Setting 'RELIQUARY_KEEP_BACKUPS' '10'
    if ($keepText -notmatch '^[0-9]+$') {
        Write-UpdateLog 'RELIQUARY_KEEP_BACKUPS is not a number. The script deletes no backup.'
        return
    }
    $keep = [Math]::Max(1, [int] $keepText)
    $newest = Get-NewestRestorePoint
    $backups = @(Get-ChildItem -LiteralPath $script:BackupDirectory -Directory |
        Where-Object { $_.Name -match '^[0-9]{8}T[0-9]{6}Z-' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'manifest.json')) } |
        Sort-Object -Property Name -Descending)
    $count = 0
    foreach ($backup in $backups) {
        $count++
        if ($count -gt $keep -and $backup.Name -ne $newest) {
            Write-UpdateLog "Delete the old backup $($backup.Name)."
            Remove-Folder $backup.FullName
            Remove-Folder (Join-Path $script:RestorePoints $backup.Name)
        }
    }
}

function Get-ScriptRevision([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return 0
    }
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        if ($line -match '^# reliquary-update-revision: ([0-9]+)$') {
            return [int] $Matches[1]
        }
    }
    return 0
}

function Test-InGitTree([string] $Folder) {
    $item = Get-Item -LiteralPath $Folder
    while ($item) {
        if (Test-Path -LiteralPath (Join-Path $item.FullName '.git')) {
            return $true
        }
        $item = $item.Parent
    }
    return $false
}

# Replaces this script with the script of the new version, when it has a higher revision.
# PowerShell reads all the script before it runs it, so the replacement is safe.
function Update-ThisScript {
    $new = Join-Path $script:State 'current/reliquary-update.ps1'
    if ((Get-Setting 'RELIQUARY_SELF_UPDATE' 'true') -eq 'false') { return }
    if (-not (Test-Path -LiteralPath $new)) { return }
    if ([System.IO.Path]::GetFullPath((Split-Path -Parent $script:ScriptPath)) -ne $script:InstanceHome) { return }
    # Do not change a file of a Git working tree.
    if (Test-InGitTree $script:InstanceHome) { return }
    $newRevision = Get-ScriptRevision $new
    if ($newRevision -le (Get-ScriptRevision $script:ScriptPath)) { return }
    Copy-Item -LiteralPath $new -Destination "$($script:ScriptPath).new" -Force
    Move-Item -LiteralPath "$($script:ScriptPath).new" -Destination $script:ScriptPath -Force
    Write-UpdateLog "The script reliquary-update.ps1 of this version (revision $newRevision) replaces the old script."
}

function Invoke-Update([string] $Version, [bool] $Force) {
    if (-not $Version) {
        $Version = Get-Setting 'RELIQUARY_VERSION' 'latest'
    }
    Assert-Docker
    Assert-Env
    Read-Settings
    Invoke-WithLock {
        Assert-State
        $current = Join-Path $script:State 'current'
        $next = Join-Path $script:State 'next'
        Initialize-State $Version $next

        if ((Test-Installed) -and (-not $Force) -and ((Get-Images $current) -eq (Get-Images $next))) {
            Write-UpdateLog "The instance already has the images of ""$Version"" ($(Get-StateValue $current 'RELIQUARY_RUNNING_VERSION'))."
            Remove-Folder $next
            Invoke-Compose current up --detach --wait postgres redis mailpit
            Invoke-Cli current migrate
            Invoke-Compose current up --detach --wait --remove-orphans
            return
        }

        $newVersion = Get-StateValue $next 'RELIQUARY_RUNNING_VERSION'
        if (-not (Test-Installed)) {
            Write-UpdateLog "Install version $newVersion (""$Version"")."
            if (Test-Docker volume inspect "$($script:Project)_postgres-data") {
                # The volume has data, but the script has no state for it. Keep a copy of the data first.
                Write-UpdateLog "The database volume of the project $($script:Project) exists. Back up its data before the migrations."
                Backup-Databases next (Get-BackupName 'existing-data')
            }
            Invoke-Compose next up --detach --wait postgres redis mailpit
            Write-UpdateLog 'Apply the migrations.'
            Invoke-Cli next migrate
            Write-UpdateLog 'Start the API, the worker and the web app.'
            Write-UpdateLog 'At its first start, the worker imports all the cards from Scryfall. This takes some minutes and needs about 3 GB.'
            Invoke-Compose next up --detach --wait --remove-orphans
            Move-Folder $next $current
        }
        else {
            $oldVersion = Get-StateValue $current 'RELIQUARY_RUNNING_VERSION'
            Write-UpdateLog "Update from version $oldVersion to version $newVersion (""$Version"")."
            $name = Get-BackupName $oldVersion
            # Stop the writers first, so that the backup contains all the data.
            Stop-Application current
            Backup-Databases current $name
            $point = Join-Path $script:RestorePoints $name
            New-Folder $point
            Copy-Item -LiteralPath (Join-Path $current 'images.env'), (Join-Path $current 'compose.yaml') -Destination $point
            Start-Operation $point $name 'update'

            Invoke-Compose next up --detach --wait postgres redis mailpit
            Write-UpdateLog 'Apply the migrations.'
            Invoke-Cli next migrate
            Write-UpdateLog 'Start the API, the worker and the web app.'
            Invoke-Compose next up --detach --wait --remove-orphans

            $previous = Join-Path $script:State 'previous'
            Remove-Folder $previous
            Move-Folder $current $previous
            Move-Folder $next $current
            Remove-Folder $previous
            Complete-Operation
        }

        Remove-OldBackups
        Write-UpdateLog "Version $newVersion runs on $(Get-Setting 'RELIQUARY_PUBLIC_URL' 'http://localhost:8090')."
        Update-ThisScript
    }
}

function Invoke-Check([string] $Version) {
    if (-not $Version) {
        $Version = Get-Setting 'RELIQUARY_VERSION' 'latest'
    }
    Assert-Docker
    Assert-Env
    $script:Prefix = Get-Setting 'RELIQUARY_IMAGE_PREFIX' 'ghcr.io/nicolas-goyon/reliquary'
    $script:Project = Get-Setting 'RELIQUARY_PROJECT' 'reliquary-preprod'
    $folder = Join-Path ([System.IO.Path]::GetTempPath()) ("reliquary-check-" + [Guid]::NewGuid().ToString('N'))
    try {
        Initialize-State $Version $folder
        $current = Join-Path $script:State 'current'
        $newVersion = Get-StateValue $folder 'RELIQUARY_RUNNING_VERSION'
        if ((Test-Installed) -and ((Get-Images $current) -eq (Get-Images $folder))) {
            Write-UpdateLog "The instance is up to date: version $newVersion (""$Version"")."
        }
        elseif (Test-Installed) {
            Write-UpdateLog "A different version is available: $(Get-StateValue $current 'RELIQUARY_RUNNING_VERSION') -> $newVersion (""$Version"")."
            Write-UpdateLog "Run "".\reliquary-update.ps1 $Version"" to update."
        }
        else {
            Write-UpdateLog "The instance is not installed. Version $newVersion (""$Version"") is available."
        }
    }
    finally {
        Remove-Folder $folder
    }
}

function Invoke-Status {
    if (-not (Test-Installed)) {
        Stop-WithError 'The instance is not installed.'
    }
    Assert-Docker
    $script:Project = Get-Setting 'RELIQUARY_PROJECT' 'reliquary-preprod'
    $current = Join-Path $script:State 'current'
    Write-UpdateLog "Version $(Get-StateValue $current 'RELIQUARY_RUNNING_VERSION') (""$(Get-StateValue $current 'RELIQUARY_RUNNING_CHANNEL')""), installed at $(Get-StateValue $current 'RELIQUARY_INSTALLED_AT')."
    Get-Images $current | Write-Host
    Invoke-Compose current ps
    if (Test-Path -LiteralPath $script:Marker) {
        Write-UpdateLog "An earlier $(Get-MarkerValue 'RECOVER_OPERATION') did not end. Run "".\reliquary-update.ps1 --recover""."
    }
    if (Test-Path -LiteralPath $script:RestorePoints) {
        $points = @(Get-ChildItem -LiteralPath $script:RestorePoints -Directory | Sort-Object -Property Name -Descending | ForEach-Object { $_.Name })
        if ($points.Count -gt 0) {
            Write-UpdateLog "Restore points: $($points -join ' ')"
        }
    }
}

function Invoke-Backup([string] $Label) {
    if (-not $Label) {
        $Label = 'manual'
    }
    if ($Label -notmatch '^[A-Za-z0-9_-][A-Za-z0-9._-]*$') {
        Stop-WithError 'Use only letters, digits, ".", "_" and "-" in the label, and do not start it with ".".'
    }
    if (-not (Test-Installed)) {
        Stop-WithError 'The instance is not installed.'
    }
    Assert-Docker
    Assert-Env
    Read-Settings
    Invoke-WithLock {
        Assert-State
        Backup-Databases current (Get-BackupName $Label)
    }
}

function Invoke-Rollback([bool] $Confirmed) {
    if (-not (Test-Installed)) {
        Stop-WithError 'The instance is not installed.'
    }
    Assert-Docker
    Assert-Env
    Read-Settings
    Invoke-WithLock {
        Assert-State
        $current = Join-Path $script:State 'current'
        $name = Get-NewestRestorePoint
        if (-not $name) {
            Stop-WithError 'There is no restore point. The script makes one before each update.'
        }
        $point = Join-Path $script:RestorePoints $name
        if (-not (Test-Path -LiteralPath (Join-Path (Join-Path $script:BackupDirectory $name) 'manifest.json'))) {
            Stop-WithError "The backup $name of the restore point does not exist."
        }
        if ((Get-Images $point) -eq (Get-Images $current)) {
            Stop-WithError "The restore point $name has the images of the running version. Delete the folder $point, then try again."
        }
        Write-UpdateLog "The instance goes back to version $(Get-StateValue $point 'RELIQUARY_RUNNING_VERSION') with the data of the backup $name."
        Write-UpdateLog 'All the changes after this backup will be lost. A backup keeps them first.'
        if (-not $Confirmed) {
            if ([Console]::IsInputRedirected) {
                Stop-WithError 'Add --yes to confirm the rollback.'
            }
            $answer = Read-Host 'reliquary-update: type "yes" to continue'
            if ($answer -ne 'yes') {
                Stop-WithError 'Stopped. Nothing changed.'
            }
        }
        $safety = Get-BackupName 'before-rollback'
        $beforeRollback = Join-Path $script:State 'before-rollback'
        Stop-Application current
        Backup-Databases current $safety
        Copy-Folder $current $beforeRollback
        Start-Operation $beforeRollback $safety 'rollback'

        Invoke-Compose $point up --detach --wait postgres redis mailpit
        Invoke-Cli $point restore "/backups/$name" --replace
        Invoke-Cli $point migrate
        Invoke-Compose $point up --detach --wait --remove-orphans

        $new = Join-Path $script:State 'current.new'
        Copy-Folder $point $new
        Remove-Folder $current
        Move-Folder $new $current
        Remove-Folder $point
        Remove-Folder $beforeRollback
        Complete-Operation
        Write-UpdateLog "Version $(Get-StateValue $current 'RELIQUARY_RUNNING_VERSION') runs again. The backup $safety keeps the data before the rollback."
    }
}

function Invoke-Recover {
    Assert-Docker
    Assert-Env
    Read-Settings
    Invoke-WithLock {
        if (-not (Test-Path -LiteralPath $script:Marker)) {
            Write-UpdateLog 'No update and no rollback stopped. Nothing to do.'
            return
        }
        # When this recovery fails, the lock function must not start a second recovery.
        $script:Recovering = $true
        Restore-Operation
    }
}

function Invoke-ImportCards {
    if (-not (Test-Installed)) {
        Stop-WithError 'The instance is not installed.'
    }
    Assert-Docker
    Assert-Env
    Read-Settings
    Invoke-WithLock {
        Assert-State
        Write-UpdateLog 'Import all the cards from Scryfall. This step takes some minutes.'
        Invoke-Cli current jobs run cards.import --force
    }
}

function Get-RandomBytes([int] $Count) {
    $bytes = New-Object byte[] $Count
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }
    return , $bytes
}

function Invoke-Init([string] $Version) {
    if (-not $Version) {
        $Version = 'latest'
    }
    if (Test-Path -LiteralPath $script:EnvFile) {
        Stop-WithError "$($script:EnvFile) exists. Change it, or delete it first."
    }
    New-Folder $script:InstanceHome
    $template = Join-Path $PSScriptRoot '.env.example'
    if (-not (Test-Path -LiteralPath $template)) {
        Assert-Docker
        $prefix = Get-Setting 'RELIQUARY_IMAGE_PREFIX' 'ghcr.io/nicolas-goyon/reliquary'
        $template = Join-Path $script:InstanceHome '.env.example'
        if ((Get-Setting 'RELIQUARY_PULL' 'true') -ne 'false') {
            Test-Docker pull --quiet "$prefix-cli:$Version" | Out-Null
        }
        if (-not (Test-Docker image inspect "$prefix-cli:$Version")) {
            Stop-WithError "Cannot pull $prefix-cli:$Version. Check the version and the network."
        }
        $container = ((Get-DockerOutput create "$prefix-cli:$Version") -join '').Trim()
        try {
            if (-not (Test-Docker cp "$($container):/app/deploy/.env.example" $template)) {
                Stop-WithError "Cannot read the settings template from $prefix-cli:$Version."
            }
        }
        finally {
            Test-Docker rm $container | Out-Null
        }
    }
    $signingKey = [Convert]::ToBase64String((Get-RandomBytes 32))
    $password = ((Get-RandomBytes 24) | ForEach-Object { $_.ToString('x2') }) -join ''
    $text = [System.IO.File]::ReadAllText($template)
    $text = [regex]::Replace($text, '(?m)^RELIQUARY_VERSION=.*$', "RELIQUARY_VERSION=$Version")
    $text = [regex]::Replace($text, '(?m)^RELIQUARY_SIGNING_KEY=.*$', "RELIQUARY_SIGNING_KEY=$signingKey")
    $text = [regex]::Replace($text, '(?m)^RELIQUARY_POSTGRES_PASSWORD=.*$', "RELIQUARY_POSTGRES_PASSWORD=$password")
    Write-TextFile $script:EnvFile $text
    if (-not $script:OnWindows) {
        & chmod 600 $script:EnvFile
    }
    Write-UpdateLog "The script made $($script:EnvFile) with new secrets. Read it, then run "".\reliquary-update.ps1""."
}

$command = ''
$argument = ''
if ($args.Count -ge 1) { $command = [string] $args[0] }
if ($args.Count -ge 2) { $argument = [string] $args[1] }

$exitCode = 0
try {
    switch -Exact ($command) {
        '-h' { Show-Usage }
        '--help' { Show-Usage }
        '--init' { Invoke-Init $argument }
        '--check' { Invoke-Check $argument }
        '--status' { Invoke-Status }
        '--backup' { Invoke-Backup $argument }
        '--rollback' { Invoke-Rollback ($argument -eq '--yes') }
        '--recover' { Invoke-Recover }
        '--import-cards' { Invoke-ImportCards }
        '--force' { Invoke-Update $argument $true }
        default {
            if ($command.StartsWith('-')) {
                Show-Usage
                $exitCode = 2
            }
            else {
                Invoke-Update $command $false
            }
        }
    }
}
catch {
    [Console]::Error.WriteLine("reliquary-update: error: $($_.Exception.Message)")
    $exitCode = 1
}
exit $exitCode
