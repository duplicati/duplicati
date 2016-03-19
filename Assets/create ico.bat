@echo off
rem ImageMagick and inkscape needed

mkdir pngs
inkscape -z -e pngs\error_16.png -w 16 -h 16 tray_icons_error.svg
inkscape -z -e pngs\error_24.png -w 24 -h 24 tray_icons_error.svg
inkscape -z -e pngs\error_32.png -w 32 -h 32 tray_icons_error.svg
inkscape -z -e pngs\error_48.png -w 48 -h 48 tray_icons_error.svg
inkscape -z -e pngs\error_256.png -w 256 -h 256 tray_icons_error.svg

inkscape -z -e pngs\inactive_16.png -w 16 -h 16 tray_icons_inactive.svg
inkscape -z -e pngs\inactive_24.png -w 24 -h 24 tray_icons_inactive.svg
inkscape -z -e pngs\inactive_32.png -w 32 -h 32 tray_icons_inactive.svg
inkscape -z -e pngs\inactive_48.png -w 48 -h 48 tray_icons_inactive.svg
inkscape -z -e pngs\inactive_256.png -w 256 -h 256 tray_icons_inactive.svg

inkscape -z -e pngs\paused_16.png -w 16 -h 16 tray_icons_paused.svg
inkscape -z -e pngs\paused_24.png -w 24 -h 24 tray_icons_paused.svg
inkscape -z -e pngs\paused_32.png -w 32 -h 32 tray_icons_paused.svg
inkscape -z -e pngs\paused_48.png -w 48 -h 48 tray_icons_paused.svg
inkscape -z -e pngs\paused_256.png -w 256 -h 256 tray_icons_paused.svg

inkscape -z -e pngs\running_16.png -w 16 -h 16 tray_icons_running.svg
inkscape -z -e pngs\running_24.png -w 24 -h 24 tray_icons_running.svg
inkscape -z -e pngs\running_32.png -w 32 -h 32 tray_icons_running.svg
inkscape -z -e pngs\running_48.png -w 48 -h 48 tray_icons_running.svg
inkscape -z -e pngs\running_256.png -w 256 -h 256 tray_icons_running.svg

convert pngs\error_16.png pngs\error_24.png pngs\error_32.png pngs\error_48.png pngs\error_256.png error.ico
convert pngs\inactive_16.png pngs\inactive_24.png pngs\inactive_32.png pngs\inactive_48.png pngs\inactive_256.png inactive.ico
convert pngs\paused_16.png pngs\paused_24.png pngs\paused_32.png pngs\paused_48.png pngs\paused_256.png paused.ico
convert pngs\running_16.png pngs\running_24.png pngs\running_32.png pngs\running_48.png pngs\running_256.png running.ico
