param([int]$ProcessId, [string]$Title = "", [string]$Name, [ValidateSet('invoke', 'select', 'toggle', 'list', 'value', 'windows')][string]$Action = 'invoke', [string]$Value = "")

# Drives GameSync's own windows through UI Automation, the way a screen reader would: picks a window of the process by
# part of its title (-Title GameSync; a dialog Windows opens for it, such as its folder picker, is one of them), then
# invokes an element by its name, selects one (a pill tab), ticks or unticks one (toggle, a check box), or types into
# it (value). list prints the named elements;
# windows, the windows.
#   powershell -File tools\window\uia.ps1 -ProcessId 1234 -Title GameSync -Name "Save manager" -Action invoke
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$byPid = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $ProcessId)
$windows = $A::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $byPid)
if ($Action -eq 'windows') { foreach ($w in $windows) { "window: '$($w.Current.Name)'" }; return }
$target = $null
foreach ($w in $windows) { if ($w.Current.Name -like "*$Title*") { $target = $w; break } }
if (-not $target) {
    # Windows' own dialogs may sit under the app's window rather than beside it.
    foreach ($w in $windows) {
        $inner = $w.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($i in $inner) { if ($i.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $i.Current.Name -like "*$Title*") { $target = $i; break } }
        if ($target) { break }
    }
}
if (-not $target) { "no window like '$Title'"; return }
if ($Action -eq 'list') {
    foreach ($e in $target.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($e.Current.Name) { "{0,-24} {1}" -f $e.Current.ControlType.ProgrammaticName.Replace('ControlType.', ''), $e.Current.Name }
    }
    return
}
$byName = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $Name)
$element = $null
foreach ($e in $target.FindAll([System.Windows.Automation.TreeScope]::Descendants, $byName)) {
    $want = switch ($Action) {
        'value' { [System.Windows.Automation.ValuePattern]::Pattern }
        'select' { [System.Windows.Automation.SelectionItemPattern]::Pattern }
        'toggle' { [System.Windows.Automation.TogglePattern]::Pattern }
        default { [System.Windows.Automation.InvokePattern]::Pattern }
    }
    $object = $null
    if ($e.TryGetCurrentPattern($want, [ref]$object)) { $element = $e; break }
}
if (-not $element) { "no element named '$Name' that can $Action"; return }
switch ($Action) {
    'invoke' { $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    'select' { $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
    'toggle' { $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
    'value' { $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value) }
}
"$Action '$Name' in '$($target.Current.Name)': done"
