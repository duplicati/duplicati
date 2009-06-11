@ECHO OFF

SET VERSION=1.0.0.154
SET AL_SWITCH=/t:lib /out:Duplicati.resources.dll /product:Duplicati /version:%VERSION% /keyfile:..\..\GUI\Duplicati.snk

for /D %%j in (*) do SET LANGUAGE=%%j& call :run_lang & SET LANGUAGE=
echo ----------
echo All done!
echo ----------
GOTO quit

:run_lang
pushd %LANGUAGE%
echo al.exe /culture:%LANGUAGE% %AL_SWITCH% ^^> compile_temp.bat

SET CURDIR=
SET CURPREFIX=
SET TMPFILEDIR=
call :run_resgen

for /D %%j in (*) do SET D=%%j& call :run_subdir & SET D=

call compile_temp.bat
del /q compile_temp.bat

ECHO Done with %LANGUAGE%!
popd
GOTO :eof



:run_subdir
pushd %D%
SET TMPFILEDIR=..\
SET CURDIR=%D%\
SET CURPREFIX=.%D%
call :run_resgen
popd
GOTO :eof

:run_resgen
del /Q *.resources
for %%i in (*.%LANGUAGE%.resx) do resgen.exe %%i
for %%i in (*.%LANGUAGE%.resources) do echo /embed:%CURDIR%%%i,Duplicati.GUI%CURPREFIX%.%%i ^^>> %TMPFILEDIR%compile_temp.bat
GOTO :eof

:quit