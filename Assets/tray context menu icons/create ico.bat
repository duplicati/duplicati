@echo off
rem ImageMagick and inkscape needed

mkdir temp
inkscape -z -e temp/pause_16.png -w 16 -h 16 tray_context_menu_pause.svg
inkscape -z -e temp/pause_24.png -w 24 -h 24 tray_context_menu_pause.svg
inkscape -z -e temp/pause_32.png -w 32 -h 32 tray_context_menu_pause.svg
inkscape -z -e temp/pause_48.png -w 48 -h 48 tray_context_menu_pause.svg
inkscape -z -e temp/pause_256.png -w 256 -h 256 tray_context_menu_pause.svg

inkscape -z -e temp/resume_16.png -w 16 -h 16 tray_context_menu_resume.svg
inkscape -z -e temp/resume_24.png -w 24 -h 24 tray_context_menu_resume.svg
inkscape -z -e temp/resume_32.png -w 32 -h 32 tray_context_menu_resume.svg
inkscape -z -e temp/resume_48.png -w 48 -h 48 tray_context_menu_resume.svg
inkscape -z -e temp/resume_256.png -w 256 -h 256 tray_context_menu_resume.svg

inkscape -z -e temp/open_16.png -w 16 -h 16 tray_context_menu_open.svg
inkscape -z -e temp/open_24.png -w 24 -h 24 tray_context_menu_open.svg
inkscape -z -e temp/open_32.png -w 32 -h 32 tray_context_menu_open.svg
inkscape -z -e temp/open_48.png -w 48 -h 48 tray_context_menu_open.svg
inkscape -z -e temp/open_256.png -w 256 -h 256 tray_context_menu_open.svg

inkscape -z -e temp/quit_16.png -w 16 -h 16 tray_context_menu_quit.svg
inkscape -z -e temp/quit_24.png -w 24 -h 24 tray_context_menu_quit.svg
inkscape -z -e temp/quit_32.png -w 32 -h 32 tray_context_menu_quit.svg
inkscape -z -e temp/quit_48.png -w 48 -h 48 tray_context_menu_quit.svg
inkscape -z -e temp/quit_256.png -w 256 -h 256 tray_context_menu_quit.svg

convert temp/pause_16.png temp/pause_24.png temp/pause_32.png temp/pause_48.png temp/pause_256.png context_menu_pause.ico
convert temp/resume_16.png temp/resume_24.png temp/resume_32.png temp/resume_48.png temp/resume_256.png context_menu_play.ico
convert temp/open_16.png temp/open_24.png temp/open_32.png temp/open_48.png temp/open_256.png context_menu_open.ico
convert temp/quit_16.png temp/quit_24.png temp/quit_32.png temp/quit_48.png temp/quit_256.png context_menu_quit.ico

rd /s /q temp
pause
