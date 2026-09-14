function Get-TauntMethod([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production declaration: $signature" }
    $brace = $source.IndexOf('{', $start); $depth = 1; $end = $brace + 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ } elseif ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production declaration: $signature" }
    return $source.Substring($start, $end - $start)
}
function Export-ActualMatchText([string]$matchPath, [string]$outputPath, [bool]$audioFault = $false) {
    $source = [IO.File]::ReadAllText($matchPath)
    $event = [regex]::Matches($source, '(?m)^\s*internal event Action<string> OnTextCallout;')
    $report = [regex]::Matches($source, '(?m)^\s*internal void ReportTextCallout\(string label\)\s*\r?\n\s*=>[^\r\n]+;')
    if ($event.Count -ne 1 -or $report.Count -ne 1) { throw 'Actual match text declarations changed; update reviewed extraction' }
    $raise = Get-TauntMethod $source 'private static void RaiseSafe('
    $reportText = $report[0].Value
    if ($audioFault) {
        if (!$reportText.Contains('OnTextCallout?.Invoke(label)')) { throw 'Audio fault anchor changed' }
        $reportText = $reportText.Replace('OnTextCallout?.Invoke(label)', 'OnCallout?.Invoke(label)')
    }
    # These exact declarations are compiled; no manually reproduced event-dispatch body.
    $actual = "using System;`nnamespace ArenaMode.Modes { internal sealed partial class ArenaMatch {`n" +
        $event[0].Value + "`n" + $reportText + "`n" + $raise + "`n} }`n"
    [IO.File]::WriteAllText($outputPath, $actual)
}
