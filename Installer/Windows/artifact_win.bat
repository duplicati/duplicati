@rem start this batch from the root Duplicati git directory

@rem Installation instructions when building locally (March 2023)
@rem The life expectancy of any URL on MS servers is about one or two years
@rem so links can become obsolete fast.
@rem   - setup a Windows 2022 VM 
@rem     (should work for Win10 but a 2022 VM is valid for 6 months vs 1 for Win10)
@rem   - install git from https://git-scm.com/download/win
@rem   - add to the PATH the directory c:\program files\git
@rem   - install .NET SDK 4.7 from https://dotnet.microsoft.com/en-us/download/visual-studio-sdks
@rem   - install msbuild (visual studio community)
@rem   - install nuget from https://learn.microsoft.com/en-us/nuget/install-nuget-client-tools
@rem   - install wix 3 from https://wixtoolset.org
@rem   - add to the PATH msbuild, wix3 and nuget

for /f "tokens=2 delims==" %%a in ('wmic os get localdatetime /value') do set dt=%%a
set RELEASE_TIMESTAMP=%dt:~0,4%-%dt:~4,2%-%dt:~6,2%

set RELEASE_INC_VERSION=$(cat Updates/build_version.txt)
for /f %%a in ('type updates\build_version.txt') do set RELEASE_INC_VERSION=%%a
set /a RELEASE_INC_VERSION=%RELEASE_INC_VERSION%+1

set RELEASE_TYPE=canary

set RELEASE_VERSION=2.0.7.%RELEASE_INC_VERSION%
set RELEASE_NAME=%RELEASE_VERSION%_%RELEASE_TYPE%_%RELEASE_TIMESTAMP%

set RELEASE_FILE_NAME=duplicati-%RELEASE_NAME%

set RUNTMP=%USERPROFILE%
set ZIPBUILDFILE=%1
if "%ZIPBUILDFILE%" == "" (
  where /q bash.exe
  if ERRORLEVEL 1 (
    git-bash -x Installer\bundleduplicati.sh %RELEASE_NAME% 1
  ) ELSE (
    bash -x Installer\bundleduplicati.sh %RELEASE_NAME% 1
  )
  set ZIPBUILDFILE=%RUNTMP%\%RELEASE_NAME%
)
pushd Installer\Windows
call build-msi %ZIPBUILDFILE%
if not exist "%RUNTMP%\artifacts" (
  mkdir %RUNTMP%\artifacts
)
move duplicati.msi %RUNTMP%\artifacts\duplicati-%RELEASE_NAME%-%2.msi
move duplicati-32bit.msi %RUNTMP%\artifacts\duplicati-32bit-%RELEASE_NAME%-%2.msi
popd

