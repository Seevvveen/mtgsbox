param(
	[Parameter( Mandatory = $true )]
	[string] $BulkPath,

	[Parameter( Mandatory = $true )]
	[string] $LegacyFixturePath,

	[Parameter( Mandatory = $true )]
	[string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$layoutNames = @{
	normal = 'Normal'
	split = 'Split'
	flip = 'Flip'
	transform = 'Transform'
	modal_dfc = 'ModalDfc'
	meld = 'Meld'
	leveler = 'Leveler'
	class = 'Class'
	case = 'Case'
	saga = 'Saga'
	adventure = 'Adventure'
	prepare = 'Prepare'
	mutate = 'Mutate'
	prototype = 'Prototype'
	battle = 'Battle'
	planar = 'Planar'
	scheme = 'Scheme'
	vanguard = 'Vanguard'
	token = 'Token'
	double_faced_token = 'DoubleFacedToken'
	emblem = 'Emblem'
	augment = 'Augment'
	host = 'Host'
	art_series = 'ArtSeries'
	reversible_card = 'ReversibleCard'
}

$bulkSelections = @(
	@{ Id = 'normal-colored'; Layout = 'normal'; Name = 'Nissa, Worldsoul Speaker' },
	@{ Id = 'normal-land-no-cost'; Layout = 'normal'; Name = 'Escape Tunnel' },
	@{ Id = 'normal-variable-cost'; Layout = 'normal'; Name = 'Lattice Library' },
	@{ Id = 'normal-hybrid-cost'; Layout = 'normal'; Name = 'Safewright Quest' },
	@{ Id = 'normal-phyrexian-cost'; Layout = 'normal'; Name = 'Spike, Tournament Grinder' },
	@{ Id = 'normal-zero-cost'; Layout = 'normal'; Name = 'Spidersilk Net' },
	@{ Id = 'produced-mana-multiple'; Layout = 'normal'; Name = 'Nantuko Elder' },
	@{ Id = 'produced-mana-funny-t'; Layout = 'normal'; Name = 'Sole Performer' },
	@{ Id = 'layout-adventure'; Layout = 'adventure'; Name = 'Twice Upon a Time // Unlikely Meeting' },
	@{ Id = 'layout-art-series'; Layout = 'art_series'; Name = 'Brightglass Gearhulk // Brightglass Gearhulk' },
	@{ Id = 'layout-augment'; Layout = 'augment'; Name = 'Humming-' },
	@{ Id = 'layout-case'; Layout = 'case'; Name = 'Case of the Uneaten Feast' },
	@{ Id = 'layout-class'; Layout = 'class'; Name = "Stormchaser's Talent" },
	@{ Id = 'layout-double-faced-token'; Layout = 'double_faced_token'; Name = 'Foraging Squirrels // Foraging Squirrels (cont''d)' },
	@{ Id = 'layout-emblem'; Layout = 'emblem'; Name = 'Koth of the Hammer Emblem' },
	@{ Id = 'layout-host'; Layout = 'host'; Name = 'Labro Bot' },
	@{ Id = 'layout-leveler'; Layout = 'leveler'; Name = 'Zulaport Enforcer' },
	@{ Id = 'layout-meld'; Layout = 'meld'; Name = 'Phyrexian Dragon Engine' },
	@{ Id = 'layout-mutate'; Layout = 'mutate'; Name = 'Illuna, Apex of Wishes' },
	@{ Id = 'layout-planar'; Layout = 'planar'; Name = 'The Great Aerie' },
	@{ Id = 'layout-prepare'; Layout = 'prepare'; Name = 'Adventurous Eater // Have a Bite' },
	@{ Id = 'layout-prototype'; Layout = 'prototype'; Name = 'Goring Warplow' },
	@{ Id = 'layout-saga'; Layout = 'saga'; Name = 'Long List of the Ents' },
	@{ Id = 'layout-scheme'; Layout = 'scheme'; Name = "What's Yours Is Now Mine" },
	@{ Id = 'layout-token'; Layout = 'token'; Name = 'Tyranid' },
	@{ Id = 'layout-vanguard'; Layout = 'vanguard'; Name = 'Titania' }
)

$legacySelections = @(
	@{ Id = 'layout-split'; Layout = 'split'; Name = 'Wear // Tear' },
	@{ Id = 'layout-flip'; Layout = 'flip'; Name = "Erayo, Soratami Ascendant // Erayo's Essence" },
	@{ Id = 'layout-transform'; Layout = 'transform'; Name = 'Arlinn Kord // Arlinn, Embraced by the Moon' },
	@{ Id = 'layout-modal-dfc-spell'; Layout = 'modal_dfc'; Name = 'Extus, Oriq Overlord // Awaken the Blood Avatar' },
	@{ Id = 'layout-reversible-card'; Layout = 'reversible_card'; Name = 'Propaganda // Propaganda' },
	@{ Id = 'layout-modal-dfc-land'; Layout = 'modal_dfc'; Name = 'Beyeen Veil // Beyeen Coast' }
)

function Get-SelectionKey(
	[string] $Layout,
	[string] $Name
)
{
	return "$Layout`n$Name"
}

function Read-BulkSelections
{
	$wanted = @{}

	foreach ( $selection in $bulkSelections )
	{
		$key = Get-SelectionKey $selection.Layout $selection.Name
		$wanted[$key] = $selection
	}

	$found = @{}
	$input = [System.IO.File]::OpenRead( $BulkPath )
	$gzip = [System.IO.Compression.GZipStream]::new(
		$input,
		[System.IO.Compression.CompressionMode]::Decompress )
	$reader = [System.IO.StreamReader]::new( $gzip )

	try
	{
		while ( ($line = $reader.ReadLine()) -ne $null )
		{
			if ( [string]::IsNullOrWhiteSpace( $line ) )
			{
				continue
			}

			$document = [System.Text.Json.JsonDocument]::Parse( $line )

			try
			{
				$root = $document.RootElement
				$layout = $root.GetProperty( 'layout' ).GetString()
				$name = $root.GetProperty( 'name' ).GetString()
				$key = Get-SelectionKey $layout $name

				if ( $wanted.ContainsKey( $key ) )
				{
					$found[$key] = $line | ConvertFrom-Json
				}
			}
			finally
			{
				$document.Dispose()
			}
		}
	}
	finally
	{
		$reader.Dispose()
		$gzip.Dispose()
		$input.Dispose()
	}

	foreach ( $selection in $bulkSelections )
	{
		$key = Get-SelectionKey $selection.Layout $selection.Name

		if ( !$found.ContainsKey( $key ) )
		{
			throw "Could not find bulk fixture '$($selection.Name)' ($($selection.Layout))."
		}
	}

	return $found
}

function Read-LegacySelections
{
	$raw = [System.IO.File]::ReadAllText( $LegacyFixturePath )
	$found = @{}
	$cards = @()

	try
	{
		$parsed = $raw | ConvertFrom-Json

		if ( $null -ne $parsed.PSObject.Properties['cases'] )
		{
			$cards = @(
				$parsed.cases |
				Where-Object {
					$_.kind -eq 'valid' -and
					$null -ne $_.card
				} |
				ForEach-Object { $_.card }
			)
		}
		elseif ( $null -ne $parsed.PSObject.Properties['layout'] )
		{
			$cards = @($parsed)
		}
	}
	catch
	{
		$chunks = [regex]::Split(
			$raw,
			'(?m)(?<=^\})\r?\n(?=^\{)' )
		$cards = @(
			$chunks |
				ForEach-Object { $_ | ConvertFrom-Json }
		)
	}

	foreach ( $card in $cards )
	{
		$key = Get-SelectionKey $card.layout $card.name

		if ( !$found.ContainsKey( $key ) )
		{
			$found[$key] = $card
		}
	}

	foreach ( $selection in $legacySelections )
	{
		$key = Get-SelectionKey $selection.Layout $selection.Name

		if ( !$found.ContainsKey( $key ) )
		{
			throw "Could not find legacy fixture '$($selection.Name)' ($($selection.Layout))."
		}
	}

	return $found
}

function New-MinimalCard(
	[string] $Id,
	[string] $Name,
	[string] $Layout = 'normal',
	[AllowNull()]
	[string] $ManaCost = '',
	[AllowNull()]
	[object[]] $CardFaces = $null,
	[AllowNull()]
	[string[]] $ProducedMana = $null
)
{
	$setId = '30000000-0000-4000-8000-000000000001'
	$apiUri = "https://api.scryfall.com/cards/$Id"

	return [pscustomobject][ordered]@{
		object = 'card'
		id = $Id
		oracle_id = $null
		name = $Name
		lang = 'en'
		released_at = '2000-01-01'
		uri = $apiUri
		scryfall_uri = "https://scryfall.com/card/test/1"
		layout = $Layout
		highres_image = $false
		image_status = 'missing'
		image_uris = $null
		mana_cost = $ManaCost
		card_faces = $CardFaces
		cmc = 0
		type_line = 'Card'
		color_identity = @()
		colors = @()
		oracle_text = ''
		keywords = @()
		legalities = [pscustomobject]@{}
		games = @('paper')
		reserved = $false
		produced_mana = $ProducedMana
		foil = $false
		nonfoil = $true
		finishes = @('nonfoil')
		promo = $false
		reprint = $false
		variation = $false
		digital = $false
		collector_number = '1'
		set_id = $setId
		set = 'test'
		set_name = 'Synthetic Test Set'
		set_type = 'funny'
		set_uri = "https://api.scryfall.com/sets/$setId"
		set_search_uri =
			'https://api.scryfall.com/cards/search?q=e%3Atest'
		scryfall_set_uri = 'https://scryfall.com/sets/test'
		rulings_uri = "$apiUri/rulings"
		prints_search_uri =
			'https://api.scryfall.com/cards/search?q=set%3Atest'
		border_color = 'black'
		card_back_id = $null
		frame = '2015'
		frame_effects = @()
		rarity = 'common'
		full_art = $false
		oversized = $false
		textless = $false
		booster = $false
		story_spotlight = $false
		prices = [pscustomobject][ordered]@{
			usd = $null
			usd_foil = $null
			usd_etched = $null
			eur = $null
			eur_foil = $null
			eur_etched = $null
			tix = $null
		}
		related_uris = [pscustomobject]@{}
		purchase_uris = $null
		future_fixture_field = [pscustomobject]@{
			note = 'Exercises JsonExtensionData preservation.'
		}
	}
}

function Copy-Card( [object] $Card )
{
	return $Card |
		ConvertTo-Json -Depth 100 -Compress |
		ConvertFrom-Json
}

function Get-FaceCosts( [object] $Card )
{
	$cardFacesProperty = $Card.PSObject.Properties['card_faces']

	if ( $null -ne $cardFacesProperty -and
			$null -ne $cardFacesProperty.Value -and
			@($cardFacesProperty.Value).Count -gt 0 )
	{
		return @(
			$cardFacesProperty.Value |
			ForEach-Object {
				if ( $null -eq $_.mana_cost )
				{
					''
				}
				else
				{
					$_.mana_cost
				}
			}
		)
	}

	$manaCostProperty = $Card.PSObject.Properties['mana_cost']

	if ( $null -eq $manaCostProperty -or
		$null -eq $manaCostProperty.Value )
	{
		return @('')
	}

	return @($manaCostProperty.Value)
}

function Get-ProducedManaExpectation( [object] $Card )
{
	$property = $Card.PSObject.Properties['produced_mana']

	if ( $null -eq $property -or $null -eq $property.Value )
	{
		return [ordered]@{
			produced_mana_state = 'null'
			produced_mana = $null
		}
	}

	$values = @($property.Value)

	return [ordered]@{
		produced_mana_state =
			if ( $values.Count -eq 0 ) { 'empty' } else { 'values' }
		produced_mana = $values
	}
}

$cases = [System.Collections.Generic.List[object]]::new()

function Add-ValidCase(
	[string] $Id,
	[string] $Source,
	[object] $Card
)
{
	if ( !$layoutNames.ContainsKey( $Card.layout ) )
	{
		throw "No expected CardLayout name is registered for '$($Card.layout)'."
	}

	$producedMana = Get-ProducedManaExpectation $Card

	$cases.Add(
		[ordered]@{
			id = $Id
			kind = 'valid'
			source = $Source
			expected = [ordered]@{
				layout = $layoutNames[$Card.layout]
				face_mana_costs = @(Get-FaceCosts $Card)
				produced_mana_state =
					$producedMana.produced_mana_state
				produced_mana =
					$producedMana.produced_mana
			}
			card = $Card
		}
	)
}

function Add-InvalidCase(
	[string] $Id,
	[string] $ExpectedErrorContains,
	[object] $Card
)
{
	$cases.Add(
		[ordered]@{
			id = $Id
			kind = 'invalid'
			source = 'synthetic_edge'
			expected_error_contains = $ExpectedErrorContains
			card = $Card
		}
	)
}

$bulkCards = Read-BulkSelections
$legacyCards = Read-LegacySelections

foreach ( $selection in $bulkSelections )
{
	$key = Get-SelectionKey $selection.Layout $selection.Name
	Add-ValidCase $selection.Id 'local_scryfall_bulk' $bulkCards[$key]
}

foreach ( $selection in $legacySelections )
{
	$key = Get-SelectionKey $selection.Layout $selection.Name
	Add-ValidCase $selection.Id 'legacy_fixture' $legacyCards[$key]
}

$emptyProducedManaCard =
	Copy-Card $bulkCards[
		(Get-SelectionKey 'normal' 'Nissa, Worldsoul Speaker') ]
$emptyProducedManaCard.id = '10000000-0000-4000-8000-000000000001'
$emptyProducedManaCard.name = 'Fixture: Explicit Empty Produced Mana'

$emptyProducedManaCard |
	Add-Member `
		-NotePropertyName future_fixture_field `
		-NotePropertyValue ([pscustomobject]@{ value = 17 })

$emptyProducedManaCard.image_uris |
	Add-Member `
		-NotePropertyName future_image_field `
		-NotePropertyValue 'image-extension'

$emptyProducedManaCard.prices |
	Add-Member `
		-NotePropertyName future_price_field `
		-NotePropertyValue 'price-extension'

$emptyProducedManaCard.preview |
	Add-Member `
		-NotePropertyName future_preview_field `
		-NotePropertyValue $true

$emptyProducedManaCard.all_parts[0] |
	Add-Member `
		-NotePropertyName future_related_card_field `
		-NotePropertyValue 42

if ( $null -eq $emptyProducedManaCard.PSObject.Properties['oracle_id'] )
{
	$emptyProducedManaCard |
		Add-Member -NotePropertyName oracle_id -NotePropertyValue $null
}
else
{
	$emptyProducedManaCard.oracle_id = $null
}

if ( $null -eq $emptyProducedManaCard.PSObject.Properties['produced_mana'] )
{
	$emptyProducedManaCard |
		Add-Member -NotePropertyName produced_mana -NotePropertyValue @()
}
else
{
	$emptyProducedManaCard.produced_mana = @()
}

Add-ValidCase 'produced-mana-empty' 'synthetic_edge' $emptyProducedManaCard

$missingCostCard = New-MinimalCard `
	'10000000-0000-4000-8000-000000000002' `
	'Fixture: Missing Top-Level Cost' `
	'normal' `
	$null
Add-ValidCase 'normal-null-cost' 'synthetic_edge' $missingCostCard

$unknownIdentifierCard = New-MinimalCard `
	'10000000-0000-4000-8000-000000000003' `
	'Fixture: Unknown Syntactically Valid Symbol' `
	'normal' `
	'{D}'
Add-ValidCase 'normal-unknown-symbol-identifier' 'synthetic_edge' $unknownIdentifierCard

$battleCard = New-MinimalCard `
	'10000000-0000-4000-8000-000000000004' `
	'Fixture: Battle Layout' `
	'battle' `
	'{3}{G}'
Add-ValidCase 'layout-battle' 'synthetic_layout' $battleCard

$nullFaceCostCard = New-MinimalCard `
	'10000000-0000-4000-8000-000000000005' `
	'Fixture: Null Face Cost' `
	'modal_dfc' `
	$null `
	@(
		[pscustomobject][ordered]@{
			object = 'card_face'
			name = 'Fixture Front'
			mana_cost = '{W}'
			future_face_field = 'face-extension'
		},
		[pscustomobject][ordered]@{
			object = 'card_face'
			name = 'Fixture Back'
			mana_cost = $null
		}
	)
Add-ValidCase 'face-null-cost-becomes-none' 'synthetic_edge' $nullFaceCostCard

$combinedWithoutFaces = New-MinimalCard `
	'20000000-0000-4000-8000-000000000001' `
	'Fixture Invalid: Combined Cost Without Faces' `
	'adventure' `
	'{1}{R} // {U}'
Add-InvalidCase `
	'combined-cost-without-faces' `
	'contains multiple face costs' `
	$combinedWithoutFaces

$emptyFaces = New-MinimalCard `
	'20000000-0000-4000-8000-000000000002' `
	'Fixture Invalid: Empty Faces' `
	'transform' `
	$null `
	@()
Add-InvalidCase `
	'empty-card-faces' `
	"'card_faces' cannot be an empty array" `
	$emptyFaces

$nullFace = New-MinimalCard `
	'20000000-0000-4000-8000-000000000003' `
	'Fixture Invalid: Null Face Entry' `
	'transform' `
	$null `
	@($null)
Add-InvalidCase `
	'null-card-face' `
	"'card_faces[0]' cannot be null" `
	$nullFace

$malformedCost = New-MinimalCard `
	'20000000-0000-4000-8000-000000000004' `
	'Fixture Invalid: Malformed Cost' `
	'normal' `
	'{2}{R'
Add-InvalidCase `
	'malformed-mana-cost' `
	'contains invalid mana cost' `
	$malformedCost

$whitespaceCost = New-MinimalCard `
	'20000000-0000-4000-8000-000000000005' `
	'Fixture Invalid: Whitespace Cost' `
	'normal' `
	'   '
Add-InvalidCase `
	'whitespace-mana-cost' `
	'contains invalid mana cost' `
	$whitespaceCost

$unknownLayout = New-MinimalCard `
	'20000000-0000-4000-8000-000000000006' `
	'Fixture Invalid: Unknown Layout' `
	'future_layout' `
	'{W}'
Add-InvalidCase `
	'unknown-layout' `
	'Unknown Scryfall layout value' `
	$unknownLayout

$invalidGuid = New-MinimalCard `
	'not-a-guid' `
	'Fixture Invalid: Bad GUID' `
	'normal' `
	'{W}'
Add-InvalidCase `
	'invalid-scryfall-id' `
	"field 'id' contains invalid GUID" `
	$invalidGuid

$missingColorIdentity = New-MinimalCard `
	'20000000-0000-4000-8000-000000000008' `
	'Fixture Invalid: Missing Color Identity' `
	'normal' `
	'{W}'
$missingColorIdentity.color_identity = $null
Add-InvalidCase `
	'missing-color-identity' `
	"field 'color_identity' is missing" `
	$missingColorIdentity

$unknownFrame = New-MinimalCard `
	'20000000-0000-4000-8000-000000000009' `
	'Fixture Invalid: Unknown Frame' `
	'normal' `
	'{W}'
$unknownFrame.frame = '2099'
Add-InvalidCase `
	'unknown-frame' `
	'Unknown Scryfall frame value' `
	$unknownFrame

$unknownBorder = New-MinimalCard `
	'20000000-0000-4000-8000-000000000010' `
	'Fixture Invalid: Unknown Border' `
	'normal' `
	'{W}'
$unknownBorder.border_color = 'neon'
Add-InvalidCase `
	'unknown-border-color' `
	'Unknown Scryfall border_color value' `
	$unknownBorder

$unknownRarity = New-MinimalCard `
	'20000000-0000-4000-8000-000000000011' `
	'Fixture Invalid: Unknown Rarity' `
	'normal' `
	'{W}'
$unknownRarity.rarity = 'priceless'
Add-InvalidCase `
	'unknown-rarity' `
	'Unknown Scryfall rarity value' `
	$unknownRarity

$unknownFrameEffect = New-MinimalCard `
	'20000000-0000-4000-8000-000000000012' `
	'Fixture Invalid: Unknown Frame Effect' `
	'normal' `
	'{W}'
$unknownFrameEffect.frame_effects = @('future_effect')
Add-InvalidCase `
	'unknown-frame-effect' `
	'Unknown Scryfall frame_effects value' `
	$unknownFrameEffect

$suite = [ordered]@{
	schema_version = 1
	description =
		'Card normalization and database JSON round-trip fixtures.'
	notes = @(
		'Real cards are selected from the local Scryfall oracle bulk file.',
		'Synthetic cases cover source states not present in the current bulk file.',
		'Invalid cases assert intentional rejection and diagnostic messages.'
	)
	cases = $cases
}

$outputDirectory = [System.IO.Path]::GetDirectoryName( $OutputPath )

if ( ![string]::IsNullOrWhiteSpace( $outputDirectory ) )
{
	[System.IO.Directory]::CreateDirectory( $outputDirectory ) |
		Out-Null
}

$json = $suite | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText(
	$OutputPath,
	$json + [Environment]::NewLine,
	[System.Text.UTF8Encoding]::new( $false ) )

Write-Output "Wrote $($cases.Count) test cases to '$OutputPath'."
