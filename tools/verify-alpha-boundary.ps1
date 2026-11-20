param([switch]$Baseline)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskArguments=@((Join-Path $PSScriptRoot 'verify-alpha-boundary.mjs'),$taskRoot)
if($Baseline){$taskArguments+='--baseline'}
& node @taskArguments
if($LASTEXITCODE -ne 0){throw 'OBS capture failed'}
Add-Type -AssemblyName System.Drawing
$taskFailures=0
foreach($taskName in @('Canvas','Runtime')){
foreach($taskScale in @('native','small','large')){
 $taskTag=if($Baseline){'baseline'}else{'fixed'}
 $taskFile=Join-Path $taskRoot "artifacts/alpha-boundary/$taskName-$taskTag-$taskScale.png"
 $taskBitmap=[Drawing.Bitmap]::FromFile($taskFile)
 try {
  $taskEdge=switch($taskScale){native{1100}small{484}large{1777}}
  $taskMinimum=255
  foreach($taskY in ($taskEdge-2)..($taskEdge+3)){
   foreach($taskX in @(220,300,500)){
    $taskMinimum=[Math]::Min($taskMinimum,$taskBitmap.GetPixel($taskX,$taskY).R)
   }
  }
  $taskPass=$taskMinimum -ge 254
  if(!$taskPass){$taskFailures++}
  "$(if($taskPass){'PASS'}else{'FAIL'}) $taskName $taskScale transparent pink boundary on white: minimum red=$taskMinimum (expected 254..255)"
 }finally{$taskBitmap.Dispose()}
}
}
if($taskFailures){exit 1}
