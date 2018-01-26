#!/usr/bin/env python3

# by Ben Fisher, https://github.com/downpoured
# a Python script to restore files from Duplicati
# similar to Duplicati.RecoveryTool, but with no dependencies on Mono/.NET
# uses streaming apis to restore a large number of files and use limited RAM.
# supports backups using AES encryption (.aes) or No Encryption (.zip),
# if data uses GPG/other encryption, decrypt files to .zip before running this tool.

import os
import sys
import io
import json
import sqlite3
import zipfile
import codecs
import getpass
#import fnmatch
import base64
import hashlib
import pyAesCrypt
from collections import OrderedDict
from tempfile import mkstemp, mkdtemp, TemporaryFile, TemporaryDirectory, NamedTemporaryFile
from pprint import pprint
from subprocess import Popen, PIPE, run
#import subprocess
#import pexpect
import gnupg
import shutil



##
def mainRestore(options):
    # locate dlist
    dlists = [name for name in os.listdir(options['orig']['path']) if name.endswith(".dlist.%s" %(options['orig']['extension']))]

    # loop over all dlists; they only need to be enencrypted, and encrypted. They have no relation to the dindex and dblock files.
    with NamedTemporaryFile() as temp_file:
        for dlist_enc in dlists:
            decrypt(options['orig'],os.path.join(options['orig']['path'],dlist_enc),options['orig']['passwd'],temp_file.name)
            encrypt(options['new'],temp_file.name,options['new']['passwd'], os.path.join(options['new']['path'],change_ext(dlist_enc,options['orig']['extension'],options['new']['extension'])))
    
    # locate dlist
    dindex = [name for name in os.listdir(options['orig']['path']) if name.endswith(".dindex.%s" %(options['orig']['extension']))]

    with NamedTemporaryFile() as temp_dindex, NamedTemporaryFile() as temp_dindex_reenc, TemporaryDirectory() as temp_path_zip:
        for dindex_enc in dindex:
            decrypt(options['orig'],os.path.join(options['orig']['path'],dindex_enc),options['orig']['passwd'],temp_dindex.name)
            
            unzip(temp_dindex,temp_path_zip)

            vol_path = os.path.join(temp_path_zip,'vol')
            
            for dblock in os.listdir(vol_path):
                data = []
                with open(os.path.join(vol_path, dblock)) as data_file:
                    data = json.load(data_file, object_pairs_hook=OrderedDict)

                expected_hash = data['volumehash'].encode('utf8')
                expected_volumesize = data['volumesize']

                if (options['verify_hash']):
                    actual_hash = computeHash(os.path.join(options['orig']['path'],dblock))
                    actual_volumesize=os.stat(os.path.join(options['orig']['path'],dblock)).st_size
                    print('dblock: %s expected_hash: %s calc_hash: %s exact: %s' % (dblock,expected_hash.decode('utf8'),actual_hash.decode('utf8'),expected_hash==actual_hash))

                with NamedTemporaryFile(delete=False) as temp_dblock:
                    decrypt(options['orig'],os.path.join(options['orig']['path'],dblock),options['orig']['passwd'],temp_dblock.name)
                    encrypt(options['new'],temp_dblock.name,options['new']['passwd'], os.path.join(options['new']['path'],change_ext(dblock,options['orig']['extension'],options['new']['extension'])))
                new_hash = computeHash(os.path.join(options['new']['path'],change_ext(dblock,options['orig']['extension'],options['new']['extension'])))
                
                data['volumehash'] = new_hash.decode('utf8')
                data['volumesize'] = os.stat(os.path.join(options['new']['path'],change_ext(dblock,options['orig']['extension'],options['new']['extension']))).st_size
                print('dblock: %s old_hash: %s new_hash: %s' % (dblock,expected_hash.decode('utf8'),data['volumehash']))
                #print(data['volumehash'])

                with open(os.path.join(vol_path,dblock),'w') as data_file:
                    json.dump(data, data_file)

                os.rename(os.path.join(vol_path, dblock), os.path.join(vol_path, change_ext(dblock,options['orig']['extension'],options['new']['extension']))) 
     
            make_zipfile(temp_dindex_reenc.name,temp_path_zip)
            encrypt(options['new'],temp_dindex_reenc.name, options['new']['passwd'],os.path.join(options['new']['path'],change_ext(dindex_enc,options['orig']['extension'],options['new']['extension'])))

def change_ext(filename, ext_old, ext_new):
    return filename.replace(ext_old, ext_new)

def decrypt(options, encrypted, passw, decrypted):
    if options['encryption']=='aes':
        bufferSize = 64 * 1024
        pyAesCrypt.decryptFile(encrypted, decrypted, passw, bufferSize)
    if options['encryption']:
        gpg = gnupg.GPG()
        with open(encrypted, 'rb') as f:
            status  = gpg.decrypt_file(f, output=decrypted,passphrase=passw)
    if options['encryption']:
        shutil.copy(encrypted,decrypted)
    

def encrypt(options,decrypted, passw, encrypted):
    if options['encryption']:
        bufferSize = 64 * 1024
        pyAesCrypt.encryptFile(decrypted, encrypted, passw, bufferSize)
    if options['encryption']:
        gpg = gnupg.GPG()
        with open(decrypted, 'rb') as f:
            status  = gpg.encrypt_file(f, recipients=options['recipients'], output=encrypted, armor=False)
    if options['encryption']:
        shutil.copy(decrypted,encrypted)
        #print('ok: %s' % status.ok)
        #print('status: %s' % status.status)
        #print('stderr: %s' % status.stderr)

def unzip(archive, path):
    with zipfile.ZipFile(archive.name) as zf:
        zf.extractall(path)

def make_zipfile(output_filename, source_dir):
    relroot=source_dir
    with zipfile.ZipFile(output_filename, "w", zipfile.ZIP_DEFLATED) as zip:
        for root, dirs, files in os.walk(source_dir):
            # add directory (needed for empty dirs)
            zip.write(root, os.path.relpath(root, relroot))
            for file in files:
                filename = os.path.join(root, file)
                if os.path.isfile(filename): # regular files only
                    arcname = os.path.join(os.path.relpath(root, relroot), file)
                    zip.write(filename, arcname)
                    

def rezip(temp_path_z, path):
    zf = zipfile.ZipFile(path, "w")
    for dirname, subdirs, files in os.walk(temp_path_z):
        zf.write(dirname)
        for filename in files:
            zf.write(os.path.join(dirname, filename))
    zf.close()

def zipdir(path,ziph):
    for root, dirs, files in os.walk(path):
        for file in files:
            ziph.write(os.path.join(root, file))
            
def computeHash(path):
    buffersize=64 * 1024
    hasher = hashlib.sha256()
    with open(path, 'rb') as f:
        while True:
            buffer = f.read(buffersize)
            if not buffer:
                break
            hasher.update(buffer)
    return base64.b64encode(hasher.digest())

def main():
    options = {}
    options['orig'] = {}
    options['new'] = {}
    options['verify_hash'] = False
    options['orig']['encryption'] = 'aes'
    options['orig']['extension'] = 'zip.aes'
    options['orig']['passwd'] = '123456'
    options['orig']['path'] = '/mnt/c/Duplicati/test_aes'
    options['new']['encryption'] = 'gpg'
    options['new']['extension'] = 'zip.gpg'
    options['new']['passwd'] = ''
    options['new']['path'] = '/mnt/c/Duplicati/test_de4'
    options['new']['recipients'] = ['user@host.com']
    mainRestore(options)
    print('Complete.')

if __name__ == '__main__':
    main()
