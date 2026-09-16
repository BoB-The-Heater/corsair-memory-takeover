$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testDirectory = Join-Path $PSScriptRoot 'test-output'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$testBinary = Join-Path $testDirectory 'CoreTests.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Management.dll','System.ServiceProcess.dll','System.Web.Extensions.dll','Microsoft.CSharp.dll')
$compilerArgs = @('/nologo','/target:exe','/main:Tests','/platform:x64','/codepage:65001',('/out:' + $testBinary)) + @($references | ForEach-Object { '/reference:' + $_ }) + $sources + (Join-Path $PSScriptRoot 'tests\CoreTests.cs')
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $testBinary (Join-Path $PSScriptRoot 'tests\fixtures\connected.log') (Join-Path $PSScriptRoot 'tests\fixtures\disconnected.log')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
