$exeApp = "C:\tools\abs.exe"
if([System.IO.File]::Exists($exeApp)){
    Remove-Item $exeApp -Force
}
Invoke-Expression "dotnet publish -c Release --arch x64 --nologo --self-contained true"
Copy-Item "C:\my-coding-projects\abstain\bin\Release\net10.0\win-x64\native\abstain.exe" -Destination "C:\tools" -Force
Rename-Item "C:\tools\abstain.exe" "abs.exe" -ErrorAction SilentlyContinue
$path =  "C:\tools\Data\abstain.db";
if(![System.IO.File]::Exists($path)){
    Copy-Item "C:\my-coding-projects\abstain\bin\Debug\net10.0\Data\abstain.db" -Destination "C:\tools\Data"
}
