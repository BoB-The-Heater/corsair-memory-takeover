param([ValidateSet('core','ui','preferences','rules','startup')][string]$Suite = 'core')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testDirectory = Join-Path $PSScriptRoot 'test-output'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$testName = if ($Suite -eq 'ui') { 'UiTests' } elseif ($Suite -eq 'preferences') { 'PreferencesTests' } elseif ($Suite -eq 'startup') { 'StartupTests' } else { 'CoreTests' }
$main = if ($Suite -eq 'core' -or $Suite -eq 'rules') { 'Tests' } else { $testName }
$testBinary = Join-Path $testDirectory ($testName + '.exe')
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
if ($Suite -eq 'preferences') { $sources = @((Join-Path $PSScriptRoot 'src\UiPreferences.cs')) }
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Management.dll','System.ServiceProcess.dll','System.Web.Extensions.dll','Microsoft.CSharp.dll')
$compilerArgs = @('/nologo','/target:exe',('/main:' + $main),'/platform:x64','/codepage:65001',('/out:' + $testBinary),('/win32icon:' + (Join-Path $PSScriptRoot 'assets\app.ico'))) + @($references | ForEach-Object { '/reference:' + $_ }) + $sources + (Join-Path $PSScriptRoot ('tests\' + $testName + '.cs'))
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
if ($Suite -eq 'rules') { & $testBinary '--rules-tests' (Join-Path $PSScriptRoot 'tests\fixtures\connected.log') (Join-Path $PSScriptRoot 'tests\fixtures\disconnected.log') }
elseif ($Suite -ne 'core') { & $testBinary }
else { & $testBinary (Join-Path $PSScriptRoot 'tests\fixtures\connected.log') (Join-Path $PSScriptRoot 'tests\fixtures\disconnected.log') }
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
