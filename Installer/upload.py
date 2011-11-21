#!/usr/bin/env python

import sys
import os
import glob

def main():
  
  source_folder = os.path.join('bin', 'release')
  
  new_version = None
  suggest_string = ''

  for testfile in glob.glob( os.path.join(source_folder, 'Duplicati *.zip') ):
    version_info = testfile[testfile.rfind('Duplicati ') + len('Duplicati '):- len('.zip')]
    if new_version is None:
      new_version = version_info
      suggest_string = '[' + new_version + ']'
    else:
      sys.stdout.write('More than one file matching, no auto suggests for you!\n')
      suggest_string = ''
      new_version = None
      break

  sys.stdout.write('Please enter file version number ' + suggest_string + ': ')
  file_version = sys.stdin.readline().rstrip()
  
  if (file_version is None or len(file_version) == 0):
    file_version = new_version
	
  if (file_version is None or len(file_version) == 0):
    sys.stdout.write('No name given, exiting\n')  
    sys.exit(-1)
  
  summary_base = "Duplicati " + file_version
  filename_base = "Duplicati " + file_version
  
  sys.stdout.write('Please enter description [' + summary_base + ']: ')
  tmp = file_version = sys.stdin.readline().rstrip()
  if (len(tmp) != 0):
    sumary_base = tmp

  filename_zip = filename_base + ".zip"
  filename_msi_x86 = filename_base + ".msi"
  filename_msi_x64 = filename_base + ".x64.msi"
  filename_deb = filename_base + ".deb"
  filename_dmg = filename_base + ".dmg"
  filename_rpm = filename_base + ".noarch.rpm"
  filename_tgz = filename_base + ".tgz"
  
  filename_zip = os.path.join(source_folder, filename_zip)
  filename_msi_x86 = os.path.join(source_folder, filename_msi_x86)
  filename_msi_x64 = os.path.join(source_folder, filename_msi_x64)
  filename_deb = os.path.join(source_folder, filename_deb)
  filename_dmg = os.path.join(source_folder, filename_dmg)
  filename_rpm = os.path.join(source_folder, filename_rpm)
  filename_tgz = os.path.join(source_folder, filename_tgz)

  username = None
  password = None
  
  upload_count = 0
  
  try:  
    with open('account_info', 'r') as f:
      username = f.readline().strip()
      password = f.readline().strip()
  except IOError as e:
    None
  
  if (username is None or len(username) == 0 or password is None or len(password) == 0):
    sys.stdout.write("username or password was empty\n")
    sys.stdout.write("please make a file called account_info with the\n")
    sys.stdout.write("username on the first line and the\n")
    sys.stdout.write("google code password on the second line\n")
    return -1
  
  if (os.path.exists(filename_zip)):
    sys.stdout.write('Uploading file: ' + filename_zip + '\n')  
    os.system('python googlecode_upload.py --summary="' + summary_base + ' - Binaries" --project="duplicati" --labels="Type-Archive,OpSys-All" "' + filename_zip + '" --user="' + username + '" --password="' + password + '"')
    upload_count+=1

  if (os.path.exists(filename_msi_x86)):
    sys.stdout.write('Uploading file: ' + filename_msi_x86 + '\n')  
    os.system('python googlecode_upload.py --summary="' + summary_base + ' - Windows" --project="duplicati" --labels="Type-Installer,OpSys-Windows" "' + filename_msi_x86 + '" --user="' + username + '" --password="' + password + '"')
    upload_count+=1

  if (os.path.exists(filename_msi_x64)):
    sys.stdout.write('Uploading file: ' + filename_msi_x64 + '\n')  
    os.system('python googlecode_upload.py --summary="' + summary_base + ' - Windows 64bit" --project="duplicati" --labels="Type-Installer,OpSys-Windows" "' + filename_msi_x64 + '" --user="' + username + '" --password="' + password + '"')
    upload_count+=1

  if (os.path.exists(filename_deb)):
    sys.stdout.write('Uploading file: ' + filename_deb + '\n')  
    os.system('python googlecode_upload.py --summary="' + summary_base + ' - Debian package" --project="duplicati" --labels="Type-Installer,OpSys-Linux" "' + filename_deb + '" --user="' + username + '" --password="' + password + '"')
    upload_count+=1

  if (os.path.exists(filename_rpm)):
    sys.stdout.write('Uploading file: ' + filename_rpm + '\n')  
    os.system('python googlecode_upload.py --summary="' + summary_base + ' - Fedora package" --project="duplicati" --labels="Type-Installer,OpSys-Linux" "' + filename_rpm + '" --user="' + username + '" --password="' + password + '"')
    upload_count+=1

  if (os.path.exists(filename_dmg)):
    sys.stdout.write('Uploading file: ' + filename_dmg + '\n')  
    os.system('python googlecode_upload.py --summary="' + summary_base + ' - Mac OSX image" --project="duplicati" --labels="Type-Installer,OpSys-OSX" "' + filename_dmg + '" --user="' + username + '" --password="' + password + '"')
    upload_count+=1
	
  if (os.path.exists(filename_tgz)):
    sys.stdout.write('Uploading file: ' + filename_tgz + '\n')  
    os.system('python googlecode_upload.py --summary="' + summary_base + ' - Linux tgz package" --project="duplicati" --labels="Type-Installer,OpSys-Linux" "' + filename_tgz + '" --user="' + username + '" --password="' + password + '"')
    upload_count+=1

  if (upload_count == 0):
    sys.stdout.write('No files uploaded, wrong filename?')
  else:
    sys.stdout.write('All uploaded !')
  
if __name__ == '__main__':
  sys.exit(main())

