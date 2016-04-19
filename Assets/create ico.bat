@echo off
rem ImageMagick and inkscape needed

mkdir temp
inkscape -z -e temp\application_16.png -w 16 -h 16 application_icon.svg
inkscape -z -e temp\application_20.png -w 20 -h 20 application_icon.svg
inkscape -z -e temp\application_24.png -w 24 -h 24 application_icon.svg
inkscape -z -e temp\application_32.png -w 32 -h 32 application_icon.svg
inkscape -z -e temp\application_40.png -w 40 -h 40 application_icon.svg
inkscape -z -e temp\application_48.png -w 48 -h 48 application_icon.svg
inkscape -z -e temp\application_64.png -w 64 -h 64 application_icon.svg
inkscape -z -e temp\application_96.png -w 96 -h 96 application_icon.svg
inkscape -z -e temp\application_256.png -w 256 -h 256 application_icon.svg

inkscape -z -e temp\error_16.png -w 16 -h 16 tray_icons_error.svg
inkscape -z -e temp\error_24.png -w 24 -h 24 tray_icons_error.svg
inkscape -z -e temp\error_32.png -w 32 -h 32 tray_icons_error.svg
inkscape -z -e temp\error_48.png -w 48 -h 48 tray_icons_error.svg
inkscape -z -e temp\error_256.png -w 256 -h 256 tray_icons_error.svg

inkscape -z -e temp\inactive_16.png -w 16 -h 16 tray_icons_inactive.svg
inkscape -z -e temp\inactive_24.png -w 24 -h 24 tray_icons_inactive.svg
inkscape -z -e temp\inactive_32.png -w 32 -h 32 tray_icons_inactive.svg
inkscape -z -e temp\inactive_48.png -w 48 -h 48 tray_icons_inactive.svg
inkscape -z -e temp\inactive_256.png -w 256 -h 256 tray_icons_inactive.svg

inkscape -z -e temp\paused_16.png -w 16 -h 16 tray_icons_paused.svg
inkscape -z -e temp\paused_24.png -w 24 -h 24 tray_icons_paused.svg
inkscape -z -e temp\paused_32.png -w 32 -h 32 tray_icons_paused.svg
inkscape -z -e temp\paused_48.png -w 48 -h 48 tray_icons_paused.svg
inkscape -z -e temp\paused_256.png -w 256 -h 256 tray_icons_paused.svg

inkscape -z -e temp\running_16.png -w 16 -h 16 tray_icons_running.svg
inkscape -z -e temp\running_24.png -w 24 -h 24 tray_icons_running.svg
inkscape -z -e temp\running_32.png -w 32 -h 32 tray_icons_running.svg
inkscape -z -e temp\running_48.png -w 48 -h 48 tray_icons_running.svg
inkscape -z -e temp\running_256.png -w 256 -h 256 tray_icons_running.svg

convert temp\application_16.png temp\application_20.png temp\application_24.png temp\application_32.png temp\application_40.png temp\application_48.png temp\application_64.png temp\application_96.png temp\application_256.png application_icon.ico
convert temp\error_16.png temp\error_24.png temp\error_32.png temp\error_48.png temp\error_256.png error.ico
convert temp\inactive_16.png temp\inactive_24.png temp\inactive_32.png temp\inactive_48.png temp\inactive_256.png inactive.ico
convert temp\paused_16.png temp\paused_24.png temp\paused_32.png temp\paused_48.png temp\paused_256.png paused.ico
convert temp\running_16.png temp\running_24.png temp\running_32.png temp\running_48.png temp\running_256.png running.ico

rd /s /q temp
