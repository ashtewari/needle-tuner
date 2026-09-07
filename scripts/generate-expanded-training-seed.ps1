param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$datasetDir = Join-Path $Root 'IntentEvalHarness\Dataset'
$sourcePath = Join-Path $datasetDir 'needle-training-seed.json'
$newSourcePath = Join-Path $datasetDir 'needle-training-seed.300.json'

$seed = Get-Content $sourcePath -Raw | ConvertFrom-Json
$examples = [System.Collections.Generic.List[object]]::new()
$seenQueries = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$parameterKeyOrder = @{
    newBoxLabel = 0
    itemName = 1
    scope = 2
    boxLabel = 3
    action = 4
    secondaryBoxLabel = 5
    quantity = 6
    newName = 7
    imageKind = 8
    location = 9
    topic = 10
}

foreach ($example in $seed) {
    $examples.Add($example)
    [void]$seenQueries.Add([string]$example.input)
}

function Add-Example {
    param(
        [string]$Query,
        [string]$ExpectedIntent,
        [hashtable]$ExpectedParameters,
        [string]$Difficulty,
        [string]$Notes
    )

    if ([string]::IsNullOrWhiteSpace($Query)) {
        throw 'Query cannot be empty.'
    }

    if (-not $seenQueries.Add($Query)) {
        throw "Duplicate query: $Query"
    }

    $stableParameters = [ordered]@{}
    foreach ($key in @($ExpectedParameters.Keys | Sort-Object `
            @{ Expression = { if ($parameterKeyOrder.ContainsKey($_)) { $parameterKeyOrder[$_] } else { 100 } } }, `
            @{ Expression = { [string]$_ } })) {
        $stableParameters[$key] = $ExpectedParameters[$key]
    }

    $examples.Add([pscustomobject][ordered]@{
        input = $Query
        expectedIntent = $ExpectedIntent
        expectedParameters = $stableParameters
        difficulty = $Difficulty
        notes = $Notes
    })
}

$searchWithBoxTemplates = @(
    @{ text = 'find the {0} in {1}'; difficulty = 'medium'; note = 'Search with explicit box context.' },
    @{ text = 'look up the {0} in {1}'; difficulty = 'medium'; note = 'Lookup phrasing with a box filter.' },
    @{ text = 'search {1} for the {0}'; difficulty = 'medium'; note = 'Search verb with explicit container context.' },
    @{ text = 'check {1} for the {0}'; difficulty = 'easy'; note = 'Short search request with a location filter.' },
    @{ text = 'track down the {0} in {1}'; difficulty = 'medium'; note = 'Locate phrasing should still map to search_item.' },
    @{ text = 'locate the {0} in {1}'; difficulty = 'easy'; note = 'Direct locate phrasing with explicit box context.' }
)

$searchWithBoxCases = @(
    @{ item = 'projector remote'; box = 'media cabinet' },
    @{ item = 'backup dog meds'; box = 'mudroom cubby' },
    @{ item = 'fondue forks'; box = 'kitchen extras' },
    @{ item = 'spare shower curtain rings'; box = 'linen closet bin 2' },
    @{ item = 'bee smoker'; box = 'shed shelf b' },
    @{ item = 'camp stove igniter'; box = 'camping tote' },
    @{ item = 'portable cd player'; box = 'guest room closet' },
    @{ item = 'chalk markers'; box = 'craft cabinet' },
    @{ item = 'battery tester'; box = 'utility drawer' },
    @{ item = 'snow scraper'; box = 'garage rack 1' },
    @{ item = 'dutch oven trivet'; box = 'kitchen island drawer' },
    @{ item = 'passport photos'; box = 'travel docs box' }
)

for ($i = 0; $i -lt $searchWithBoxCases.Count; $i++) {
    $case = $searchWithBoxCases[$i]
    $template = $searchWithBoxTemplates[$i % $searchWithBoxTemplates.Count]
    $query = [string]::Format($template.text, $case.item, $case.box)
    Add-Example -Query $query -ExpectedIntent 'SEARCH_ITEM' -ExpectedParameters @{ itemName = $case.item; boxLabel = $case.box } -Difficulty $template.difficulty -Notes $template.note
}

$searchNoBoxTemplates = @(
    @{ text = 'where did I put the {0}'; difficulty = 'easy'; note = 'Where-is phrasing for a single item.' },
    @{ text = 'do we still have {0}'; difficulty = 'medium'; note = 'Existence question stays on search_item.' },
    @{ text = 'which box has the {0}'; difficulty = 'medium'; note = 'Container lookup for one item.' },
    @{ text = 'is the {0} listed anywhere'; difficulty = 'medium'; note = 'Inventory lookup, not a report request.' },
    @{ text = 'did I pack the {0}'; difficulty = 'medium'; note = 'Packed phrasing should still search for an item.' },
    @{ text = 'can you find the {0}'; difficulty = 'easy'; note = 'Direct search phrasing.' }
)

$searchNoBoxItems = @(
    'garage door clicker',
    'spare hose nozzle',
    'uk travel adapter',
    'seed starter tray',
    'folding garment rack',
    'ice cream maker paddle',
    'label maker',
    'storm lantern glass',
    'small torque wrench',
    'backup phone charger'
)

for ($i = 0; $i -lt $searchNoBoxItems.Count; $i++) {
    $item = $searchNoBoxItems[$i]
    $template = $searchNoBoxTemplates[$i % $searchNoBoxTemplates.Count]
    $query = [string]::Format($template.text, $item)
    Add-Example -Query $query -ExpectedIntent 'SEARCH_ITEM' -ExpectedParameters @{ itemName = $item } -Difficulty $template.difficulty -Notes $template.note
}

$addQtyTemplates = @(
    @{ text = 'add {0} {1} to {2}'; difficulty = 'easy'; note = 'Direct add with quantity and box.' },
    @{ text = 'put {0} {1} in {2}'; difficulty = 'easy'; note = 'Short add phrasing with quantity.' },
    @{ text = 'stash {0} {1} in {2}'; difficulty = 'medium'; note = 'Stash phrasing should still map to add_item.' },
    @{ text = 'store {0} {1} in {2}'; difficulty = 'medium'; note = 'Store verb but still an add operation.' },
    @{ text = 'log {0} {1} under {2}'; difficulty = 'medium'; note = 'Register/log phrasing with quantity.' }
)

$addQtyCases = @(
    @{ qty = 3; item = 'extension ladders'; box = 'shed wall hooks' },
    @{ qty = 5; item = 'moving blankets'; box = 'garage loft' },
    @{ qty = 2; item = 'pet carriers'; box = 'hall closet floor' },
    @{ qty = 9; item = 'canning lids'; box = 'pantry overflow' },
    @{ qty = 4; item = 'clipboards'; box = 'office shelf 3' },
    @{ qty = 7; item = 'paint rollers'; box = 'workshop cabinet' },
    @{ qty = 6; item = 'rechargeable headlamps'; box = 'camping gear crate' },
    @{ qty = 2; item = 'suit garment bags'; box = 'guest room closet' },
    @{ qty = 11; item = 'tea light candles'; box = 'holiday decor' },
    @{ qty = 8; item = 'furnace filters'; box = 'basement utility shelf' }
)

for ($i = 0; $i -lt $addQtyCases.Count; $i++) {
    $case = $addQtyCases[$i]
    $template = $addQtyTemplates[$i % $addQtyTemplates.Count]
    $query = [string]::Format($template.text, $case.qty, $case.item, $case.box)
    Add-Example -Query $query -ExpectedIntent 'ADD_ITEM' -ExpectedParameters @{ itemName = $case.item; boxLabel = $case.box; quantity = $case.qty } -Difficulty $template.difficulty -Notes $template.note
}

$addSimpleTemplates = @(
    @{ text = 'add the {0} to {1}'; difficulty = 'easy'; note = 'Direct add request without quantity.' },
    @{ text = 'put the {0} in {1}'; difficulty = 'easy'; note = 'Simple placement request.' },
    @{ text = 'register the {0} under {1}'; difficulty = 'medium'; note = 'Register phrasing should map to add_item.' },
    @{ text = 'log the {0} in {1}'; difficulty = 'medium'; note = 'Logging a new item should remain add_item.' }
)

$addSimpleCases = @(
    @{ item = 'folding step stool'; box = 'laundry room shelf' },
    @{ item = 'mahjong set'; box = 'family room cabinet' },
    @{ item = 'travel steamer'; box = 'main closet top shelf' },
    @{ item = 'plant mister'; box = 'sunroom cart' },
    @{ item = 'bird seed scoop'; box = 'shed pegboard bin' },
    @{ item = 'laser level'; box = 'tool chest drawer 2' },
    @{ item = 'first aid refill pack'; box = 'bathroom linen tower' },
    @{ item = 'croquet set'; box = 'garage sports shelf' }
)

for ($i = 0; $i -lt $addSimpleCases.Count; $i++) {
    $case = $addSimpleCases[$i]
    $template = $addSimpleTemplates[$i % $addSimpleTemplates.Count]
    $query = [string]::Format($template.text, $case.item, $case.box)
    Add-Example -Query $query -ExpectedIntent 'ADD_ITEM' -ExpectedParameters @{ itemName = $case.item; boxLabel = $case.box } -Difficulty $template.difficulty -Notes $template.note
}

$updateQtyTemplates = @(
    @{ text = 'set {0} quantity to {1}'; difficulty = 'easy'; note = 'Explicit quantity correction.' },
    @{ text = 'change the {0} count to {1}'; difficulty = 'easy'; note = 'Count update phrasing.' },
    @{ text = 'update {0} quantity to {1}'; difficulty = 'easy'; note = 'Update verb with a new count.' }
)

$updateQtyCases = @(
    @{ item = 'camp chairs'; qty = 6 },
    @{ item = 'printer paper reams'; qty = 14 },
    @{ item = 'pool noodles'; qty = 5 },
    @{ item = 'taper candles'; qty = 18 },
    @{ item = 'extension cords'; qty = 9 },
    @{ item = 'storage cube bins'; qty = 12 },
    @{ item = 'chalkboard easels'; qty = 2 },
    @{ item = 'glass meal prep containers'; qty = 11 },
    @{ item = 'bike water bottles'; qty = 4 },
    @{ item = 'drawer organizers'; qty = 7 }
)

for ($i = 0; $i -lt $updateQtyCases.Count; $i++) {
    $case = $updateQtyCases[$i]
    $template = $updateQtyTemplates[$i % $updateQtyTemplates.Count]
    $query = [string]::Format($template.text, $case.item, $case.qty)
    Add-Example -Query $query -ExpectedIntent 'UPDATE_ITEM' -ExpectedParameters @{ itemName = $case.item; quantity = $case.qty } -Difficulty $template.difficulty -Notes $template.note
}

$updateMoveTemplates = @(
    @{ text = 'move {0} to {1}'; difficulty = 'medium'; note = 'Relocation of an existing item.' },
    @{ text = 'relocate the {0} to {1}'; difficulty = 'medium'; note = 'Relocate phrasing should map to update_item.' },
    @{ text = 'shift the {0} into {1}'; difficulty = 'medium'; note = 'Move request with an existing item assumption.' },
    @{ text = 'put the {0} over in {1}'; difficulty = 'medium'; note = 'Conversational move phrasing.' }
)

$updateMoveCases = @(
    @{ item = 'archery targets'; box = 'garage back wall' },
    @{ item = 'wedding albums'; box = 'master closet safe shelf' },
    @{ item = 'spare hdmi cables'; box = 'office drawer' },
    @{ item = 'bird feeder parts'; box = 'shed hardware bin' },
    @{ item = 'ski socks'; box = 'winter sports tote' },
    @{ item = 'backup blankets'; box = 'guest room trunk' },
    @{ item = 'water shoes'; box = 'beach gear bin' },
    @{ item = 'receipt scanner'; box = 'office shelf 1' }
)

for ($i = 0; $i -lt $updateMoveCases.Count; $i++) {
    $case = $updateMoveCases[$i]
    $template = $updateMoveTemplates[$i % $updateMoveTemplates.Count]
    $query = [string]::Format($template.text, $case.item, $case.box)
    Add-Example -Query $query -ExpectedIntent 'UPDATE_ITEM' -ExpectedParameters @{ itemName = $case.item; boxLabel = $case.box } -Difficulty $template.difficulty -Notes $template.note
}

$updateRenameTemplates = @(
    @{ text = 'rename {0} to {1}'; difficulty = 'easy'; note = 'Straight rename update.' },
    @{ text = 'change {0} name to {1}'; difficulty = 'medium'; note = 'Rename using name-change wording.' },
    @{ text = 'update {0} to {1}'; difficulty = 'medium'; note = 'Compact rename phrasing.' },
    @{ text = 'switch {0} over to {1}'; difficulty = 'medium'; note = 'Informal rename phrasing.' }
)

$updateRenameCases = @(
    @{ old = 'old road atlas'; new = 'car emergency atlas' },
    @{ old = 'spare dorm lamp'; new = 'guest room lamp' },
    @{ old = 'winter bin misc'; new = 'winter accessories' },
    @{ old = 'kids art tub'; new = 'art class supplies' },
    @{ old = 'old phone dock'; new = 'bedside phone dock' },
    @{ old = 'backup cable bag'; new = 'travel cable bag' },
    @{ old = 'yard games mixed'; new = 'backyard games' },
    @{ old = 'small sewing box'; new = 'mending kit box' }
)

for ($i = 0; $i -lt $updateRenameCases.Count; $i++) {
    $case = $updateRenameCases[$i]
    $template = $updateRenameTemplates[$i % $updateRenameTemplates.Count]
    $query = [string]::Format($template.text, $case.old, $case.new)
    Add-Example -Query $query -ExpectedIntent 'UPDATE_ITEM' -ExpectedParameters @{ itemName = $case.old; newName = $case.new } -Difficulty $template.difficulty -Notes $template.note
}

$deleteSimpleTemplates = @(
    @{ text = 'remove the {0} from inventory'; difficulty = 'easy'; note = 'Direct delete request.' },
    @{ text = 'delete the {0} entry'; difficulty = 'easy'; note = 'Entry deletion phrasing.' },
    @{ text = 'erase the {0} listing'; difficulty = 'medium'; note = 'Erase phrasing should still route to delete_item.' },
    @{ text = 'i got rid of the {0}, delete it'; difficulty = 'easy'; note = 'Remove after item disposal.' }
)

$deleteSimpleItems = @(
    'broken hummingbird feeder',
    'expired sunscreen',
    'warped cutting board',
    'old cable modem',
    'cracked plant saucer',
    'bent roasting rack',
    'stained shower liner',
    'dead string lights',
    'missing-piece puzzle',
    'rusty trowel',
    'leaky water bottle',
    'outgrown booster seat'
)

for ($i = 0; $i -lt $deleteSimpleItems.Count; $i++) {
    $item = $deleteSimpleItems[$i]
    $template = $deleteSimpleTemplates[$i % $deleteSimpleTemplates.Count]
    $query = [string]::Format($template.text, $item)
    Add-Example -Query $query -ExpectedIntent 'DELETE_ITEM' -ExpectedParameters @{ itemName = $item } -Difficulty $template.difficulty -Notes $template.note
}

$deleteBoxTemplates = @(
    @{ text = 'remove the {0} from {1}'; difficulty = 'medium'; note = 'Delete request with box context.' },
    @{ text = 'delete the {0} out of {1}'; difficulty = 'medium'; note = 'Deletion scoped to a specific box.' },
    @{ text = 'take the {0} out of {1} in inventory'; difficulty = 'medium'; note = 'Removal phrasing with explicit box context.' }
)

$deleteBoxCases = @(
    @{ item = 'duplicate usb hub'; box = 'office drawer' },
    @{ item = 'melted ice pack'; box = 'camping cooler bin' },
    @{ item = 'chipped salad bowl'; box = 'kitchen extras' },
    @{ item = 'stale dog treats'; box = 'mudroom pantry' },
    @{ item = 'broken cassette player'; box = 'basement archive shelf' },
    @{ item = 'frayed yoga strap'; box = 'guest room closet' }
)

for ($i = 0; $i -lt $deleteBoxCases.Count; $i++) {
    $case = $deleteBoxCases[$i]
    $template = $deleteBoxTemplates[$i % $deleteBoxTemplates.Count]
    $query = [string]::Format($template.text, $case.item, $case.box)
    Add-Example -Query $query -ExpectedIntent 'DELETE_ITEM' -ExpectedParameters @{ itemName = $case.item; boxLabel = $case.box } -Difficulty $template.difficulty -Notes $template.note
}

$manageCreateTemplates = @(
    @{ text = 'create a box called {0}'; difficulty = 'easy'; note = 'Explicit box creation.' },
    @{ text = 'set up a new container for {0}'; difficulty = 'easy'; note = 'Create container phrasing.' },
    @{ text = 'make a tote for {0}'; difficulty = 'easy'; note = 'Container creation without the verb create.' }
)

$manageCreateBoxes = @(
    'estate paperwork 2026',
    'pool maintenance kit',
    'classroom donations',
    'winter pet gear',
    'camera tripods',
    'garden seed archive'
)

for ($i = 0; $i -lt $manageCreateBoxes.Count; $i++) {
    $box = $manageCreateBoxes[$i]
    $template = $manageCreateTemplates[$i % $manageCreateTemplates.Count]
    $query = [string]::Format($template.text, $box)
    Add-Example -Query $query -ExpectedIntent 'MANAGE_BOX' -ExpectedParameters @{ action = 'create'; boxLabel = $box } -Difficulty $template.difficulty -Notes $template.note
}

$manageRenameTemplates = @(
    @{ text = 'rename {0} to {1}'; difficulty = 'medium'; note = 'Rename box action.' },
    @{ text = 'change the box name from {0} to {1}'; difficulty = 'medium'; note = 'Explicit box rename wording.' },
    @{ text = 'update {0} box to {1}'; difficulty = 'medium'; note = 'Rename box with compact phrasing.' }
)

$manageRenameCases = @(
    @{ old = 'garage overflow 1'; new = 'garage bulk storage' },
    @{ old = 'kids school stuff'; new = 'school memory bin' },
    @{ old = 'craft paper misc'; new = 'paper craft supplies' },
    @{ old = 'holiday lights extra'; new = 'spare holiday lights' },
    @{ old = 'dog walk shelf'; new = 'pet outing shelf' },
    @{ old = 'small appliance maybe'; new = 'small kitchen appliances' }
)

for ($i = 0; $i -lt $manageRenameCases.Count; $i++) {
    $case = $manageRenameCases[$i]
    $template = $manageRenameTemplates[$i % $manageRenameTemplates.Count]
    $query = [string]::Format($template.text, $case.old, $case.new)
    Add-Example -Query $query -ExpectedIntent 'MANAGE_BOX' -ExpectedParameters @{ action = 'rename'; boxLabel = $case.old; newBoxLabel = $case.new } -Difficulty $template.difficulty -Notes $template.note
}

$manageDeleteTemplates = @(
    @{ text = 'delete the empty box called {0}'; difficulty = 'easy'; note = 'Delete a container, not an item.' },
    @{ text = 'remove the box labeled {0}'; difficulty = 'medium'; note = 'Container deletion phrasing.' },
    @{ text = 'get rid of the empty tote marked {0}'; difficulty = 'medium'; note = 'Delete action for a box with descriptive wording.' }
)

$manageDeleteBoxes = @(
    'temp garage sale',
    'misc adapters old',
    'broken decor',
    'duplicate pantry jars',
    'retired lesson plans'
)

for ($i = 0; $i -lt $manageDeleteBoxes.Count; $i++) {
    $box = $manageDeleteBoxes[$i]
    $template = $manageDeleteTemplates[$i % $manageDeleteTemplates.Count]
    $query = [string]::Format($template.text, $box)
    Add-Example -Query $query -ExpectedIntent 'MANAGE_BOX' -ExpectedParameters @{ action = 'delete'; boxLabel = $box } -Difficulty $template.difficulty -Notes $template.note
}

$manageMergeTemplates = @(
    @{ text = 'merge {0} and {1}'; difficulty = 'medium'; note = 'Box merge request.' },
    @{ text = 'combine {0} with {1}'; difficulty = 'medium'; note = 'Container merge phrasing.' },
    @{ text = 'join {0} into {1}'; difficulty = 'medium'; note = 'Merge action with directional wording.' }
)

$manageMergeCases = @(
    @{ first = 'pool toys overflow'; second = 'summer pool gear' },
    @{ first = 'gift wrap backup'; second = 'gift wrap station' },
    @{ first = 'old tax files'; second = 'archived tax files' },
    @{ first = 'camp kitchen extras'; second = 'camp kitchen' },
    @{ first = 'guest bedding spare'; second = 'guest bedding' },
    @{ first = 'kids art overflow'; second = 'kids art supplies' }
)

for ($i = 0; $i -lt $manageMergeCases.Count; $i++) {
    $case = $manageMergeCases[$i]
    $template = $manageMergeTemplates[$i % $manageMergeTemplates.Count]
    $query = [string]::Format($template.text, $case.first, $case.second)
    Add-Example -Query $query -ExpectedIntent 'MANAGE_BOX' -ExpectedParameters @{ action = 'merge'; boxLabel = $case.first; secondaryBoxLabel = $case.second } -Difficulty $template.difficulty -Notes $template.note
}

$viewStaticCases = @(
    @{ input = 'show all items I have tracked'; scope = 'all_items'; difficulty = 'easy'; note = 'Broad all-items inventory request.' },
    @{ input = 'list every item in my inventory'; scope = 'all_items'; difficulty = 'easy'; note = 'Inventory-wide item listing.' },
    @{ input = 'list every box in storage'; scope = 'all_boxes'; difficulty = 'easy'; note = 'Broad box-list request.' }
)

foreach ($case in $viewStaticCases) {
    Add-Example -Query $case.input -ExpectedIntent 'VIEW_INVENTORY' -ExpectedParameters @{ scope = $case.scope } -Difficulty $case.difficulty -Notes $case.note
}

$viewBoxesByLocation = @(
    @{ input = 'show me every box in the garage'; location = 'garage' },
    @{ input = 'what boxes do I have in the attic'; location = 'attic' },
    @{ input = 'list the boxes in the basement'; location = 'basement' },
    @{ input = 'show all boxes in the guest room'; location = 'guest room' },
    @{ input = 'which boxes are stored in the shed'; location = 'shed' }
)

foreach ($case in $viewBoxesByLocation) {
    Add-Example -Query $case.input -ExpectedIntent 'VIEW_INVENTORY' -ExpectedParameters @{ scope = 'all_boxes'; location = $case.location } -Difficulty 'medium' -Notes 'Location-scoped box listing.'
}

$viewBoxContents = @(
    'garage battery bin',
    'holiday ribbon tote',
    'office reference shelf',
    'camp kitchen crate',
    'bath bomb drawer',
    'backyard game trunk',
    'pet meds box'
)

$viewBoxContentTemplates = @(
    'what is inside the {0}',
    'show the contents of {0}',
    'what''s in {0}',
    'list what is inside {0}'
)

for ($i = 0; $i -lt $viewBoxContents.Count; $i++) {
    $box = $viewBoxContents[$i]
    $template = $viewBoxContentTemplates[$i % $viewBoxContentTemplates.Count]
    $query = [string]::Format($template, $box)
    Add-Example -Query $query -ExpectedIntent 'VIEW_INVENTORY' -ExpectedParameters @{ scope = 'box_contents'; boxLabel = $box } -Difficulty 'easy' -Notes 'Inspecting one box contents.'
}

$viewReports = @(
    @{ input = 'give me a report for garage storage'; location = 'garage' },
    @{ input = 'i need a storage report for the basement'; location = 'basement' },
    @{ input = 'give me an inventory report for the shed'; location = 'shed' },
    @{ input = 'show a report for attic storage'; location = 'attic' }
)

foreach ($case in $viewReports) {
    Add-Example -Query $case.input -ExpectedIntent 'VIEW_INVENTORY' -ExpectedParameters @{ scope = 'report'; location = $case.location } -Difficulty 'medium' -Notes 'Reporting query over a location.'
}

$viewCounts = @(
    'how many items are tracked right now',
    'count everything in the inventory',
    'what is the total item count',
    'how many things do i have logged'
)

foreach ($query in $viewCounts) {
    Add-Example -Query $query -ExpectedIntent 'VIEW_INVENTORY' -ExpectedParameters @{ scope = 'count' } -Difficulty 'easy' -Notes 'Inventory count request.'
}

$uploadItemCases = @(
    @{ input = 'attach a photo to the upright mixer'; item = 'upright mixer'; kind = 'photo'; difficulty = 'easy'; note = 'Photo attachment for an item.' },
    @{ input = 'scan this receipt for the water softener filter'; item = 'water softener filter'; kind = 'receipt'; difficulty = 'medium'; note = 'Receipt scanning against an item.' },
    @{ input = 'upload an image for the blue duffel bag'; item = 'blue duffel bag'; kind = 'image'; difficulty = 'easy'; note = 'Generic image upload for an item.' },
    @{ input = 'take a picture of the sewing machine'; item = 'sewing machine'; kind = 'photo'; difficulty = 'easy'; note = 'Photo capture phrasing for an item.' },
    @{ input = 'attach the receipt image to the electric kettle'; item = 'electric kettle'; kind = 'receipt'; difficulty = 'medium'; note = 'Receipt image attachment to an item.' },
    @{ input = 'upload a photo for the stroller rain cover'; item = 'stroller rain cover'; kind = 'photo'; difficulty = 'easy'; note = 'Photo upload targeting an item.' },
    @{ input = 'add an image for the travel crib'; item = 'travel crib'; kind = 'image'; difficulty = 'easy'; note = 'Image attachment for an item.' },
    @{ input = 'scan the receipt for my cordless vac'; item = 'cordless vac'; kind = 'receipt'; difficulty = 'medium'; note = 'Receipt scanning request.' },
    @{ input = 'snap a photo of the juicer'; item = 'juicer'; kind = 'photo'; difficulty = 'easy'; note = 'Snap phrasing should map to upload_photo.' },
    @{ input = 'upload an image for the karaoke machine'; item = 'karaoke machine'; kind = 'image'; difficulty = 'easy'; note = 'Generic image attachment.' }
)

foreach ($case in $uploadItemCases) {
    Add-Example -Query $case.input -ExpectedIntent 'UPLOAD_PHOTO' -ExpectedParameters @{ itemName = $case.item; imageKind = $case.kind } -Difficulty $case.difficulty -Notes $case.note
}

$uploadBoxCases = @(
    @{ input = 'take a photo of the ski gear trunk'; box = 'ski gear trunk'; kind = 'photo'; difficulty = 'easy'; note = 'Box photo request.' },
    @{ input = 'scan this receipt for pantry backup'; box = 'pantry backup'; kind = 'receipt'; difficulty = 'medium'; note = 'Receipt upload against a box.' },
    @{ input = 'upload an image for the office archive shelf'; box = 'office archive shelf'; kind = 'image'; difficulty = 'easy'; note = 'Image attachment to a box.' },
    @{ input = 'add a photo to garage shelf 6'; box = 'garage shelf 6'; kind = 'photo'; difficulty = 'easy'; note = 'Photo request for a location-like box.' },
    @{ input = 'attach a receipt image to the holiday decor tote'; box = 'holiday decor tote'; kind = 'receipt'; difficulty = 'medium'; note = 'Receipt image for a box.' },
    @{ input = 'snap a photo of the basement keepsakes bin'; box = 'basement keepsakes bin'; kind = 'photo'; difficulty = 'easy'; note = 'Another box-photo phrasing.' },
    @{ input = 'upload an image for the spare bedding box'; box = 'spare bedding box'; kind = 'image'; difficulty = 'easy'; note = 'Generic image request for a box.' },
    @{ input = 'scan the receipt for the camping kitchen crate'; box = 'camping kitchen crate'; kind = 'receipt'; difficulty = 'medium'; note = 'Receipt scan request scoped to a box.' }
)

foreach ($case in $uploadBoxCases) {
    Add-Example -Query $case.input -ExpectedIntent 'UPLOAD_PHOTO' -ExpectedParameters @{ boxLabel = $case.box; imageKind = $case.kind } -Difficulty $case.difficulty -Notes $case.note
}

$helpCases = @(
    @{ input = 'how should i label boxes by room'; topic = 'labeling'; difficulty = 'easy'; note = 'Help about labeling strategy.' },
    @{ input = 'what can this app help me do'; topic = 'capabilities'; difficulty = 'easy'; note = 'Capabilities question.' },
    @{ input = 'what do i do first when i start organizing'; topic = 'getting started'; difficulty = 'medium'; note = 'Onboarding-oriented help request.' },
    @{ input = 'what is the best way to organize seasonal bins'; topic = 'organizing'; difficulty = 'medium'; note = 'Organizational guidance request.' },
    @{ input = 'what features are available for tracking stuff'; topic = 'features'; difficulty = 'easy'; note = 'Product feature explanation request.' },
    @{ input = 'how do photos and receipts work in here'; topic = 'photos'; difficulty = 'medium'; note = 'Help about photo and receipt attachments.' },
    @{ input = 'tips for naming boxes so i can find them later'; topic = 'labeling'; difficulty = 'medium'; note = 'Naming guidance should map to general_help.' },
    @{ input = 'explain what kinds of inventory views i can use'; topic = 'features'; difficulty = 'medium'; note = 'Feature explanation focused on views and reports.' },
    @{ input = 'i am new here, how should i start boxing things up'; topic = 'getting started'; difficulty = 'medium'; note = 'Getting-started guidance.' },
    @{ input = 'how should i organize backup pantry supplies'; topic = 'organizing'; difficulty = 'easy'; note = 'Strategy request rather than a concrete action.' },
    @{ input = 'what does whichbox actually do'; topic = 'capabilities'; difficulty = 'easy'; note = 'Another capabilities variant.' },
    @{ input = 'show me how labeling is supposed to work'; topic = 'labeling'; difficulty = 'medium'; note = 'Labeling help request.' },
    @{ input = 'what are the main inventory features'; topic = 'features'; difficulty = 'easy'; note = 'Feature summary request.' },
    @{ input = 'how do i attach receipt photos correctly'; topic = 'photos'; difficulty = 'medium'; note = 'Help request about receipt/photo workflow.' }
)

foreach ($case in $helpCases) {
    Add-Example -Query $case.input -ExpectedIntent 'GENERAL_HELP' -ExpectedParameters @{ topic = $case.topic } -Difficulty $case.difficulty -Notes $case.note
}

$unclearCases = @(
    @{ input = 'do the thing from yesterday'; difficulty = 'hard'; note = 'Missing referent; abstain.' },
    @{ input = 'add the kettle and delete the toaster and find the mugs'; difficulty = 'hard'; note = 'Conflicting multi-intent request; abstain.' },
    @{ input = 'book a cleaner for next week'; difficulty = 'medium'; note = 'Out-of-scope household service request.' },
    @{ input = 'uhhh maybe the blue one'; difficulty = 'hard'; note = 'Insufficient grounding to any tool.' },
    @{ input = 'qwerty shelf maybe maybe'; difficulty = 'hard'; note = 'Gibberish-like fragment with no concrete tool action.' },
    @{ input = 'text my sister where the ski boots are'; difficulty = 'medium'; note = 'Share/notify request is unsupported.' },
    @{ input = 'find it then move it somewhere better'; difficulty = 'hard'; note = 'Multi-intent with unresolved references; abstain.' },
    @{ input = 'how much space do i even have left'; difficulty = 'medium'; note = 'Unsupported capacity question.' },
    @{ input = 'tomorrow please'; difficulty = 'easy'; note = 'Too vague to ground to any tool.' },
    @{ input = 'do i own too much stuff lol'; difficulty = 'medium'; note = 'Subjective unsupported question.' },
    @{ input = 'merge or delete those boxes idk'; difficulty = 'hard'; note = 'Conflicted manage-box action without concrete targets.' },
    @{ input = 'send me a pdf of the inventory'; difficulty = 'medium'; note = 'Unsupported export request.' },
    @{ input = 'asdfghjkl'; difficulty = 'hard'; note = 'Pure gibberish; abstain.' },
    @{ input = 'put that over there'; difficulty = 'hard'; note = 'Deictic references without item or destination.' },
    @{ input = 'call me when you find the ladder'; difficulty = 'medium'; note = 'Unsupported follow-up notification request.' },
    @{ input = 'whatever was in the thing by the place'; difficulty = 'hard'; note = 'No recoverable item or box grounding.' }
)

foreach ($case in $unclearCases) {
    Add-Example -Query $case.input -ExpectedIntent 'UNCLEAR' -ExpectedParameters @{} -Difficulty $case.difficulty -Notes $case.note
}

$targetCounts = [ordered]@{
    SEARCH_ITEM = 40
    ADD_ITEM = 30
    UPDATE_ITEM = 53
    DELETE_ITEM = 30
    MANAGE_BOX = 44
    VIEW_INVENTORY = 43
    UPLOAD_PHOTO = 32
    GENERAL_HELP = 27
    UNCLEAR = 46
}

$currentCounts = @{}
foreach ($group in ($examples | Group-Object expectedIntent)) {
    $currentCounts[[string]$group.Name] = [int]$group.Count
}

foreach ($entry in $targetCounts.GetEnumerator()) {
    $actual = if ($currentCounts.ContainsKey($entry.Key)) { $currentCounts[$entry.Key] } else { 0 }
    if ($actual -ne $entry.Value) {
        throw "Count mismatch for $($entry.Key): expected $($entry.Value), actual $actual"
    }
}

if ($examples.Count -ne 345) {
    throw "Expected 345 total examples, found $($examples.Count)"
}

$examples | ConvertTo-Json -Depth 6 | Set-Content -Path $newSourcePath -Encoding utf8NoBOM

Write-Host "Wrote $($examples.Count) examples to $newSourcePath"
foreach ($entry in $targetCounts.GetEnumerator()) {
    Write-Host ("{0}={1}" -f $entry.Key, $entry.Value)
}