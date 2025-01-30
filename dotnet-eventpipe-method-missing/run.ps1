@('net6.0', 'net8.0', 'net9.0') | ForEach-Object {
    Write-Host ''
    Write-Host "Running app on $($_)..." -ForegroundColor Yellow
    dotnet run --framework $($_) -c Release
    Write-Host "Done running on $($_)" -ForegroundColor Yellow
    Write-Host ''
}
