[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$element = [Windows.Automation.AutomationElement]
$type = [Windows.Automation.ControlType]
$scope = [Windows.Automation.TreeScope]

function Find-ById($parent, $searchScope, [string]$id) {
    $condition = [Windows.Automation.PropertyCondition]::new($element::AutomationIdProperty, $id)
    $parent.FindFirst($searchScope, $condition)
}

$window = Find-ById $element::RootElement $scope::Children 'YxrlSettingsWindow'
if (-not $window) { throw '请先打开 Windows 版 YXRL 的设置窗口。' }
$gallery = Find-ById $window $scope::Descendants 'Preset'
if (-not $gallery) { throw '请选择「任务栏时钟」设置页。' }

$itemType = [Windows.Automation.PropertyCondition]::new($element::ControlTypeProperty, $type::ListItem)
$textType = [Windows.Automation.PropertyCondition]::new($element::ControlTypeProperty, $type::Text)
$cards = $gallery.FindAll($scope::Children, $itemType)
if ($cards.Count -ne 9) { throw "应当有 9 张样式卡，实际有 $($cards.Count) 张。" }

foreach ($card in $cards) {
    $name = $card.Current.Name
    $lines = @($card.FindAll($scope::Descendants, $textType) | ForEach-Object {
        $node = $_.Current
        if ($node.BoundingRectangle.Width -gt 0 -and $node.BoundingRectangle.Height -gt 0 -and
            $node.Name.Trim() -and $node.Name -ne $name -and $node.Name -ne [string][char]0xE73E) {
            $node.Name
        }
    })
    if ($lines.Count -ne 2) { throw "「$name」应显示两行时钟内容，实际有 $($lines.Count) 行。" }
    [pscustomobject]@{ 样式 = $name; 上行 = $lines[0]; 下行 = $lines[1] }
}
