@echo off
setlocal enabledelayedexpansion

:: Update the JSON file
(
    echo {
    echo    "Username": "",
    echo    "Server": "",
    echo    "BasePath": "",
    echo    "Port": 22
    echo }
) > webroot/config.json