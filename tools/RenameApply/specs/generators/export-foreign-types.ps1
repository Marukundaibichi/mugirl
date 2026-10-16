# Lists public top-level types of the engine (Assembly-CSharp and the Unity compatibility assemblies), HAR, Harmony, Facial Animation
# and VEF (assembly<TAB>namespace<TAB>name<TAB>base) for the spec linter's L03/L04 short-name rules.
param(
    [string] $Out = (Join-Path $PSScriptRoot 'foreign-types.tsv'),
    [string] $GameDir = ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../../../../'))),
    [string] $WorkshopDir
)
if (-not $WorkshopDir) { $WorkshopDir = [IO.Path]::GetFullPath((Join-Path $GameDir '../../workshop/content/294100')) }
$ErrorActionPreference = 'Stop'
$asms = @(
    (Join-Path $GameDir 'Assembly-CSharp.dll'),
    (Join-Path $GameDir 'UnityStubs.dll'),
    (Join-Path $GameDir 'Waylith.UnityCompat.dll'),
    (Join-Path $WorkshopDir '839005762/1.6/Assemblies/AlienRace.dll'),
    (Join-Path $WorkshopDir '2009463077/1.7/Assemblies/0Harmony.dll'),
    (Join-Path $WorkshopDir '1635901197/1.6/Assemblies/FacialAnimation.dll'),
    (Join-Path $WorkshopDir '2023507013/1.7/Assemblies/VEF.dll')
)
$lines = [System.Collections.Generic.List[string]]::new()
foreach ($a in $asms) {
    $fs = [System.IO.File]::OpenRead($a)
    try {
        $pe = [System.Reflection.PortableExecutable.PEReader]::new($fs)
        $md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        foreach ($h in $md.TypeDefinitions) {
            $t = $md.GetTypeDefinition($h)
            $vis = [int]($t.Attributes -band [System.Reflection.TypeAttributes]::VisibilityMask)
            if ($vis -ne 1) { continue }  # public top-level only
            $ns = $md.GetString($t.Namespace); $name = $md.GetString($t.Name)
            $base = ''
            if (-not $t.BaseType.IsNil -and $t.BaseType.Kind -eq [System.Reflection.Metadata.HandleKind]::TypeReference) {
                $base = $md.GetString($md.GetTypeReference([System.Reflection.Metadata.TypeReferenceHandle]$t.BaseType).Name)
            }
            elseif (-not $t.BaseType.IsNil -and $t.BaseType.Kind -eq [System.Reflection.Metadata.HandleKind]::TypeDefinition) {
                $base = $md.GetString($md.GetTypeDefinition([System.Reflection.Metadata.TypeDefinitionHandle]$t.BaseType).Name)
            }
            $lines.Add("$([IO.Path]::GetFileNameWithoutExtension($a))`t$ns`t$name`t$base")
        }
    }
    finally { $fs.Dispose() }
}
[IO.File]::WriteAllLines($Out, ($lines | Sort-Object -Unique), [Text.UTF8Encoding]::new($false))
"wrote $($lines.Count) types to $Out"
