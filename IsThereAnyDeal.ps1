function ConvertGamesToITAD ($allGames) {
    foreach ($group in $allGames | Group-Object -Property Name) {
        $games = $group.Group
        $playtime = ($games.Playtime | Sort-Object)[-1]
        $status = ($games.CompletionStatus | Sort-Object)[-1]
        @{
            title = $games[0].Name
            status = ([string]$status).ToLower()
            playtime = $playtime / 60
            copies = @(foreach ($game in $games) {
                @{
                    type = switch ($game.Source) {
                        "Battle.net" { "battlenet" }
                        "itch.io" { "itchio" }
                        { !$_ } { "playnite" }
                        Default { $_.ToLower() }
                    }
                    owned = 1
                }
            })
        }
    }
}

function ImportGamesInITAD ($games) {
    $data = @{
        version = "02"
        data = @(ConvertGamesToITAD $games)
    } | ConvertTo-Json -Depth 5
    $b64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($data))

    $html = "<!DOCTYPE html>
    <body onload='form.submit()'>
    <form id='form' action='https://isthereanydeal.com/collection/import/' method='post'>
    <input type='hidden' name='file' value='$b64'>
    <input type='hidden' name='upload' value='Import ITAD Collection'>
    </form>
    </body>"

    $webView = $PlayniteApi.WebViews.CreateView(1000, 800)
    $webView.Navigate("data:text/html," + $html)
    $webView.OpenDialog()
}

function IsThereAnyDeal {
    ImportGamesInITAD $PlayniteApi.MainView.SelectedGames
}