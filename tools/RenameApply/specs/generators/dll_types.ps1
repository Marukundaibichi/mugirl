<#
  Dumps every type definition of an assembly in METADATA ORDER (the order GenTypes.AllTypes / AllSubclassesNonAbstract and
  GenTypes.AllTypesWithAttribute enumerate it) as TSV: index, full metadata name (Outer+Inner), flags (abstract/interface/
  static), base type full name, custom attribute type names. Used by applied_to_manifest.py (do the map targets exist?) and by
  order_diff.py (reflection-order families). Usage: dll_types.ps1 -Dll <MugirlRace.dll> -Out <tsv>
#>
param([Parameter(Mandatory)] [string] $Dll, [Parameter(Mandatory)] [string] $Out)
$ErrorActionPreference = 'Stop'
$fs = [System.IO.File]::OpenRead($Dll)
try {
    $pe = [System.Reflection.PortableExecutable.PEReader]::new($fs)
    $md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    function TypeDefName([System.Reflection.Metadata.TypeDefinitionHandle] $h) {
        $t = $md.GetTypeDefinition($h)
        $name = $md.GetString($t.Name)
        $decl = $t.GetDeclaringType()
        if (-not $decl.IsNil) { return (TypeDefName $decl) + '+' + $name }
        $ns = $md.GetString($t.Namespace)
        if ($ns) { return "$ns.$name" } else { return $name }
    }
    function TypeRefName([System.Reflection.Metadata.TypeReferenceHandle] $h) {
        $r = $md.GetTypeReference($h)
        $name = $md.GetString($r.Name)
        if ($r.ResolutionScope.Kind -eq [System.Reflection.Metadata.HandleKind]::TypeReference) {
            return (TypeRefName ([System.Reflection.Metadata.TypeReferenceHandle] $r.ResolutionScope)) + '+' + $name
        }
        $ns = $md.GetString($r.Namespace)
        if ($ns) { return "$ns.$name" } else { return $name }
    }
    function HandleName($h) {
        if ($h.IsNil) { return '' }
        switch ($h.Kind) {
            ([System.Reflection.Metadata.HandleKind]::TypeDefinition) { return TypeDefName ([System.Reflection.Metadata.TypeDefinitionHandle] $h) }
            ([System.Reflection.Metadata.HandleKind]::TypeReference) { return TypeRefName ([System.Reflection.Metadata.TypeReferenceHandle] $h) }
            ([System.Reflection.Metadata.HandleKind]::TypeSpecification) {
                $spec = $md.GetTypeSpecification([System.Reflection.Metadata.TypeSpecificationHandle] $h)
                $blob = $md.GetBlobReader($spec.Signature)
                $null = $blob.ReadSignatureHeader
                if ($blob.ReadByte() -eq 0x15) {   # GENERICINST: next is CLASS/VALUETYPE + TypeDefOrRef
                    $null = $blob.ReadByte()
                    return (HandleName ($blob.ReadTypeHandle())) + '<>'
                }
                return '(typespec)'
            }
        }
        return ''
    }
    $lines = [System.Collections.Generic.List[string]]::new()
    $i = 0
    foreach ($h in $md.TypeDefinitions) {
        $t = $md.GetTypeDefinition($h)
        $full = TypeDefName $h
        if ($full -eq '<Module>') { continue }
        $a = $t.Attributes
        $flags = @()
        if ($a -band [System.Reflection.TypeAttributes]::Interface) { $flags += 'interface' }
        if (($a -band [System.Reflection.TypeAttributes]::Abstract) -and ($a -band [System.Reflection.TypeAttributes]::Sealed)) { $flags += 'static' }
        elseif ($a -band [System.Reflection.TypeAttributes]::Abstract) { $flags += 'abstract' }
        $attrs = @()
        foreach ($ca in $t.GetCustomAttributes()) {
            $c = $md.GetCustomAttribute($ca)
            if ($c.Constructor.Kind -eq [System.Reflection.Metadata.HandleKind]::MemberReference) {
                $attrs += HandleName ($md.GetMemberReference([System.Reflection.Metadata.MemberReferenceHandle] $c.Constructor).Parent)
            }
            elseif ($c.Constructor.Kind -eq [System.Reflection.Metadata.HandleKind]::MethodDefinition) {
                $attrs += TypeDefName ($md.GetMethodDefinition([System.Reflection.Metadata.MethodDefinitionHandle] $c.Constructor).GetDeclaringType())
            }
        }
        $lines.Add("$i`t$full`t$($flags -join ',')`t$(HandleName $t.BaseType)`t$($attrs -join ',')")
        $i++
    }
    [IO.File]::WriteAllLines($Out, $lines, [Text.UTF8Encoding]::new($false))
    "wrote $($lines.Count) types to $Out"
}
finally { $fs.Dispose() }
