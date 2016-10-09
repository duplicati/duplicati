@echo off
rem ImageMagick and inkscape needed

mkdir temp
inkscape -z -e temp/application_16.png -w 16 -h 16 application_icon.svg
inkscape -z -e temp/application_20.png -w 20 -h 20 application_icon.svg
inkscape -z -e temp/application_24.png -w 24 -h 24 application_icon.svg
inkscape -z -e temp/application_32.png -w 32 -h 32 application_icon.svg
inkscape -z -e temp/application_40.png -w 40 -h 40 application_icon.svg
inkscape -z -e temp/application_48.png -w 48 -h 48 application_icon.svg
inkscape -z -e temp/application_64.png -w 64 -h 64 application_icon.svg
inkscape -z -e temp/application_96.png -w 96 -h 96 application_icon.svg
inkscape -z -e temp/application_256.png -w 256 -h 256 application_icon.svg

mkdir osx_appicon
inkscape -z -e osx_appicon/icon_16x16.png -w 16 -h 16 application_icon.svg
inkscape -z -e osx_appicon/icon_16x16@2x.png -w 32 -h 32 application_icon.svg
inkscape -z -e osx_appicon/icon_32x32.png -w 32 -h 32 application_icon.svg
inkscape -z -e osx_appicon/icon_32x32@2x.png -w 64 -h 64 application_icon.svg
inkscape -z -e osx_appicon/icon_64x64.png -w 64 -h 64 application_icon.svg
inkscape -z -e osx_appicon/icon_64x64@2x.png -w 128 -h 128 application_icon.svg
inkscape -z -e osx_appicon/icon_128x128.png -w 128 -h 128 application_icon.svg
inkscape -z -e osx_appicon/icon_128x128@2x.png -w 256 -h 256 application_icon.svg
inkscape -z -e osx_appicon/icon_256x256.png -w 256 -h 256 application_icon.svg
inkscape -z -e osx_appicon/icon_256x256@2x.png -w 512 -h 512 application_icon.svg
inkscape -z -e osx_appicon/icon_512x512.png -w 512 -h 512 application_icon.svg
inkscape -z -e osx_appicon/icon_512x512@2x.png -w 1024 -h 1024 application_icon.svg

rem On OSX, the icns is created by:
rem    mv osx_appicon osx_appicon.iconset
rem    iconutil -c icns osx_appicon.iconset

convert temp/application_16.png temp/application_20.png temp/application_24.png temp/application_32.png temp/application_40.png temp/application_48.png temp/application_64.png temp/application_96.png temp/application_256.png application_icon.ico

rd /s /q temp
pause
