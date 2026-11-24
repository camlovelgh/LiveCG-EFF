param(
    [Parameter(Mandatory=$true)][int]$ParentId,
    [Parameter(Mandatory=$true)][string]$Bundle,
    [switch]$ShowProgress
)

$ErrorActionPreference = 'Stop'
$staging = [IO.Path]::GetFullPath($PSScriptRoot)
$install = [IO.Path]::GetDirectoryName($staging)
$launcher = Join-Path $install 'LiveCG-EFF.exe'
$backup = Join-Path $staging 'backup'
$completed = New-Object 'System.Collections.Generic.List[object]'
$lock = $null
$canClean = $false
$failure = $null
$progressWindow = $null

function Show-InstallProgress([int]$Percent, [string]$Text) {
    if ($null -eq $progressWindow) { return }
    $progressBar.Value = $Percent
    $progressLabel.Text = $Text
    [Windows.Forms.Application]::DoEvents()
}

function Assert-OrdinaryPath([string]$Path) {
    while ($Path -ne $install) {
        $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
        if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "폴더 연결을 통해 프로그램을 교체할 수 없습니다. $Path"
        }
        $Path = [IO.Path]::GetDirectoryName($Path)
    }
}

try {
    $parent = Get-Process -Id $ParentId -ErrorAction SilentlyContinue
    if ($parent -and !$parent.WaitForExit(60000)) { throw 'LiveCG-EFF가 아직 종료되지 않았습니다.' }

    if ((Split-Path $staging -Leaf) -notmatch '^\.livecg-update-[0-9a-f]{32}$' -or
        [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Bundle)) -ne (Join-Path $staging 'release') -or
        !(Test-Path -LiteralPath $launcher -PathType Leaf)) {
        throw '설치 폴더 또는 업데이트 파일 경로를 확인할 수 없습니다.'
    }
    Assert-OrdinaryPath $staging
    $canClean = $true
    if ($ShowProgress) {
        Add-Type -AssemblyName System.Windows.Forms
        Add-Type -AssemblyName System.Drawing
        $progressWindow = New-Object Windows.Forms.Form
        $progressWindow.Text = "$(Split-Path $Bundle -Leaf) 업데이트 · SunjooAn"
        $progressWindow.ClientSize = [Drawing.Size]::new(420, 108)
        $progressWindow.StartPosition = 'CenterScreen'
        $progressWindow.FormBorderStyle = 'FixedDialog'
        $progressWindow.ControlBox = $false
        $progressLabel = New-Object Windows.Forms.Label
        $progressLabel.SetBounds(20, 20, 380, 25)
        $progressBar = New-Object Windows.Forms.ProgressBar
        $progressBar.SetBounds(20, 54, 380, 20)
        $progressWindow.Controls.AddRange(@($progressLabel, $progressBar))
        $progressWindow.Show()
        Show-InstallProgress 0 '설치 파일 확인 중…'
    }
    $lock = [IO.FileStream]::new((Join-Path $install '.livecg-update.lock'), [IO.FileMode]::OpenOrCreate,
        [IO.FileAccess]::ReadWrite, [IO.FileShare]::None, 4096, [IO.FileOptions]::DeleteOnClose)
    $program = Join-Path $install 'app\LiveCG-EFF.exe'
    if (Get-Process -Name LiveCG-EFF -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $program }) {
        throw '같은 폴더에서 실행 중인 다른 LiveCG-EFF 창을 닫고 업데이트를 다시 시도하세요.'
    }

    $files = foreach ($entry in @('app', 'licenses', '사용안내', '먼저 읽어주세요.txt', 'LiveCG-OBS-Hotkeys.lua', 'LiveCG-EFF.exe')) {
        $source = Join-Path $Bundle $entry
        if (Test-Path -LiteralPath $source -PathType Container) {
            Get-ChildItem -LiteralPath $source -File -Recurse -Force
        } elseif (Test-Path -LiteralPath $source -PathType Leaf) {
            Get-Item -LiteralPath $source
        }
    }
    $pending = foreach ($file in $files) {
        $relative = $file.FullName.Substring($Bundle.Length + 1)
        $destination = Join-Path $install $relative
        Assert-OrdinaryPath $destination
        [pscustomobject]@{
            Source = $file.FullName
            Destination = $destination
            Backup = Join-Path $backup $relative
            Existed = [IO.File]::Exists($destination)
        }
    }
    $total = @($pending).Count
    $displayed = -1
    foreach ($file in $pending) {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file.Destination)) | Out-Null
        if ($file.Existed) {
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file.Backup)) | Out-Null
            [IO.File]::Replace($file.Source, $file.Destination, $file.Backup)
        } else {
            [IO.File]::Move($file.Source, $file.Destination)
        }
        $completed.Add($file)
        $percent = [int][Math]::Floor(100 * $completed.Count / $total)
        if ($percent -ne $displayed) {
            Show-InstallProgress $percent "설치 중 · ${percent}% ($($completed.Count) / $total 파일)"
            $displayed = $percent
        }
    }
} catch {
    $failure = $_.Exception.Message
    for ($index = $completed.Count - 1; $index -ge 0; $index--) {
        $file = $completed[$index]
        try {
            if ($file.Existed) {
                $discard = Join-Path $staging ([Guid]::NewGuid().ToString('N'))
                [IO.File]::Replace($file.Backup, $file.Destination, $discard)
            }
            else { [IO.File]::Delete($file.Destination) }
        } catch {
            $canClean = $false
            $failure += "`n복원 실패: $($file.Destination)"
        }
    }
} finally {
    if ($lock) { $lock.Dispose() }
    if ($progressWindow) { $progressWindow.Dispose() }
}

if ($failure) {
    if (!$canClean) { $failure += "`n백업 폴더: $backup" }
    Add-Type -AssemblyName System.Windows.Forms
    [void][Windows.Forms.MessageBox]::Show($failure, 'LiveCG-EFF 업데이트')
}
if ($canClean -and $staging -eq [IO.Path]::GetFullPath($PSScriptRoot) -and
    (Split-Path $staging -Leaf) -match '^\.livecg-update-[0-9a-f]{32}$') {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    if ((Test-Path -LiteralPath $launcher -PathType Leaf) -and
        !(Get-Process -Name LiveCG-EFF -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $program })) {
        Start-Process -FilePath $launcher -WorkingDirectory $install
    }
}
if ($failure) { exit 1 }
