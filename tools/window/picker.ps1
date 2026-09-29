param([int]$ProcessId, [string]$Title, [string]$Path)

# Windows' folder or file picker, opened by the app: puts a path in its Folder box (control 1152) or File name box
# (control 1148) and presses its OK or Open button (control 1), with window messages to those two controls only (no
# keystrokes, so nothing lands in another window).
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -Namespace Win -Name Msg -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
public static extern System.IntPtr SendMessage(System.IntPtr hwnd, uint msg, System.IntPtr wParam, string lParam);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern System.IntPtr PostMessage(System.IntPtr hwnd, uint msg, System.IntPtr wParam, System.IntPtr lParam);
"@
$A = [System.Windows.Automation.AutomationElement]
$T = [System.Windows.Automation.TreeScope]
$C = [System.Windows.Automation.ControlType]
$byPid = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $ProcessId)
$dialog = $null
foreach ($w in $A::RootElement.FindAll($T::Children, $byPid)) {
    foreach ($i in $w.FindAll($T::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($i.Current.ControlType -eq $C::Window -and $i.Current.Name -like "*$Title*") { $dialog = $i; break }
    }
    if ($dialog) { break }
}
if (-not $dialog) { "no picker like '$Title'"; return }

$box = $null
$ok = $null
foreach ($e in $dialog.FindAll($T::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    if ($e.Current.AutomationId -in @('1152', '1148') -and $e.Current.ClassName -eq 'Edit') { $box = $e }
    if ($e.Current.AutomationId -eq '1' -and $e.Current.ClassName -eq 'Button') { $ok = $e }
}
if (-not $box -or -not $ok) { "no Folder box or OK button"; return }
[Win.Msg]::SendMessage([System.IntPtr]$box.Current.NativeWindowHandle, 0x000C, [System.IntPtr]::Zero, $Path) | Out-Null   # WM_SETTEXT
[Win.Msg]::PostMessage([System.IntPtr]$ok.Current.NativeWindowHandle, 0x00F5, [System.IntPtr]::Zero, [System.IntPtr]::Zero) | Out-Null   # BM_CLICK
"picked $Path"
