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
                        Default { $_.Name.ToLower() }
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
    foreach ($cookie in $webView.GetCookies()) {
        if ($cookie.Domain -match "\.?isthereanydeal\.com" -and $cookie.Name -eq "user") {
            # Chrome 80+ now enforces SameSite cookies which breaks this ITAD API
            # HACK: Abuse Chrome's 2 minute timer from its "Lax + POST mitigation" https://www.chromium.org/updates/same-site/faq
            # Delete and recreate the "user" cookie to reset its creation date
            $webView.DeleteCookies("https://isthereanydeal.com/", "user")
            $webView.SetCookies("https://isthereanydeal.com/", $cookie.Domain, $cookie.Name, $cookie.Value, $cookie.Path, $cookie.Expires)
        }
    }
    $webView.Navigate("data:text/html," + $html)
    $webView.OpenDialog()
    $webView.Dispose()
}

function IsThereAnyDeal {
    ImportGamesInITAD $PlayniteApi.MainView.SelectedGames
}