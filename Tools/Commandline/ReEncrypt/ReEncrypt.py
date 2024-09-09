#!/usr/bin/env python3
## Permission is hereby granted, free of charge, to any person obtaining a 
## copy of this software and associated documentation files (the "Software"), 
## to deal in the Software without restriction, including without limitation 
## the rights to use, copy, modify, merge, publish, distribute, sublicense, 
## and/or sell copies of the Software, and to permit persons to whom the 
## Software is furnished to do so, subject to the following conditions:
## 
## The above copyright notice and this permission notice shall be included in 
## all copies or substantial portions of the Software.
## 
## THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
## OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
## FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
## AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
## LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
## FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
## DEALINGS IN THE SOFTWARE.

import os
import sys, getopt
import io
import json
import zipfile
import base64
import hashlib
import pyAesCrypt
from collections import OrderedDict
from tempfile import mkstemp, mkdtemp, TemporaryFile, TemporaryDirectory, NamedTemporaryFile
import gnupg
import shutil
from joblib import Parallel, delayed
import multiprocessing

def mainReEncrypt(options):
    # locate dlist
    dlists = [name for name in os.listdir(options['orig']['path']) if name.endswith(".dlist.%s" %(options['orig']['extension']))]

    # loop over all dlists; they only need to be enencrypted, and encrypted. They have no relation to the dindex and dblock files.
    with NamedTemporaryFile() as temp_file:
        for dlist_enc in dlists:
            dlist_enc_fullpath = os.path.join(options['orig']['path'],dlist_enc)
            dlist_reenc_fullpath = os.path.join(options['new']['path'],change_ext(dlist_enc,options['orig']['extension'],options['new']['extension']))
            decrypt(options['orig'],dlist_enc_fullpath,options['orig']['passwd'],temp_file.name)
            encrypt(options['new'],temp_file.name,options['new']['passwd'], dlist_reenc_fullpath)
    
    # locate dlist
    dindex = [name for name in os.listdir(options['orig']['path']) if name.endswith(".dindex.%s" %(options['orig']['extension']))]

    num_cores = multiprocessing.cpu_count()
    Parallel(n_jobs=num_cores*2)(delayed(handleIndex)(options, dindex_enc) for dindex_enc in dindex)

def handleIndex(options, dindex_enc):
    with NamedTemporaryFile() as temp_dindex, NamedTemporaryFile() as temp_dindex_reenc, TemporaryDirectory() as temp_path_zip:
        dindex_enc_fullpath = os.path.join(options['orig']['path'],dindex_enc)
        dindex_reenc_fullpath = os.path.join(options['new']['path'],change_ext(dindex_enc,options['orig']['extension'],options['new']['extension']))
        
        decrypt(options['orig'],dindex_enc_fullpath,options['orig']['passwd'],temp_dindex.name)
            
        unzip(temp_dindex,temp_path_zip)

        vol_path = os.path.join(temp_path_zip,'vol')
        if os.path.exists(vol_path):    
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

                with NamedTemporaryFile() as temp_dblock:
                    dblock_enc_fullpath = os.path.join(options['orig']['path'],dblock)
                    dblock_reenc_fullpath = os.path.join(options['new']['path'],change_ext(dblock,options['orig']['extension'],options['new']['extension']))
                    decrypt(options['orig'],dblock_enc_fullpath,options['orig']['passwd'],temp_dblock.name)
                    encrypt(options['new'],temp_dblock.name,options['new']['passwd'], dblock_reenc_fullpath)
                    new_hash = computeHash(dblock_reenc_fullpath)
                    
                data['volumehash'] = new_hash.decode('utf8')
                data['volumesize'] = os.stat(os.path.join(options['new']['path'],change_ext(dblock,options['orig']['extension'],options['new']['extension']))).st_size
                print('dblock: %s old_hash: %s new_hash: %s' % (dblock,expected_hash.decode('utf8'),data['volumehash']))

                with open(os.path.join(vol_path,dblock),'w') as data_file:
                    json.dump(data, data_file)

                os.rename(os.path.join(vol_path, dblock), os.path.join(vol_path, change_ext(dblock,options['orig']['extension'],options['new']['extension']))) 
        
        make_zipfile(temp_dindex_reenc.name,temp_path_zip)
        encrypt(options['new'],temp_dindex_reenc.name, options['new']['passwd'],dindex_reenc_fullpath)

def change_ext(filename, ext_old, ext_new):
    return filename.replace(ext_old, ext_new)

def decrypt(options, encrypted, passw, decrypted):
    print('decrypting: %s to %s' % (encrypted, decrypted))
    if options['encryption']=='aes':
        bufferSize = 64 * 1024
        pyAesCrypt.decryptFile(encrypted, decrypted, passw, bufferSize)
    if options['encryption']=='gpg':
        gpg = gnupg.GPG()
        with open(encrypted, 'rb') as f:
            status  = gpg.decrypt_file(f, output=decrypted,passphrase=passw)
    if options['encryption']=='none':
        shutil.copy(encrypted,decrypted)
    

def encrypt(options,decrypted, passw, encrypted):
    print('encrypting: %s %s' % (decrypted, encrypted))
    if options['encryption']=='aes':
        bufferSize = 64 * 1024
        pyAesCrypt.encryptFile(decrypted, encrypted, passw, bufferSize)
    if options['encryption']=='gpg':
        gpg = gnupg.GPG()
        with open(decrypted, 'rb') as f:
            status  = gpg.encrypt_file(f, recipients=options['recipients'], output=encrypted, armor=False)
    if options['encryption']=='none':
        shutil.copy(decrypted,encrypted)

def emptydir(top):
    if(top == '/' or top == "\\"): return
    else:
        for root, dirs, files in os.walk(top, topdown=False):
            for name in files:
                os.remove(os.path.join(root, name))
            for name in dirs:
                os.rmdir(os.path.join(root, name))

def unzip(archive, path):
    emptydir(path)
    with zipfile.ZipFile(archive.name) as zf:
        zf.extractall(path)

def make_zipfile(output_filename, source_dir):
    print('zipping: %s to %s' % (source_dir, output_filename))
    emptydir(output_filename)
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
    print('hashing: %s ' % (path))
    buffersize=64 * 1024
    hasher = hashlib.sha256()
    with open(path, 'rb') as f:
        while True:
            buffer = f.read(buffersize)
            if not buffer:
                break
            hasher.update(buffer)
    return base64.b64encode(hasher.digest())

def main(argv):
    configfile = ''
    try:
        opts, args = getopt.getopt(argv,"hc:")
    except getopt.GetoptError:
        print('ReEncrypt.py -c <configfile>')
        sys.exit(2)
    for opt, arg in opts:
        if opt == '-h':
            print('ReEncrypt.py -c <configfile>')
            sys.exit(2)
        elif opt == '-c':
            configfile = arg
        else:
            print( "unhandled option")
    if (configfile == ''):
        print('ReEncrypt.py -c <configfile>')
        sys.exit(2)

    with open(configfile) as infile:
        options = json.load(infile)
        mainReEncrypt(options)
    print('Complete.')

if __name__ == "__main__":
   main(sys.argv[1:])

