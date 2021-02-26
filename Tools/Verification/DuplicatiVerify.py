#!/usr/bin/env python

"""
This file is a standalone python script that is tested with python 2.7 & 
python 3.8
If Duplicati is producing a backup with the option --upload-verification-file,
it will produce a *-verification.json file on the backend, which can be verified
by this script. Simply run this script with the path to the backup
folder as input, and it will verify all *-verification.json files in the folder.
"""

from __future__ import print_function
import sys
import os
import string
import json
import base64
import codecs
from hashlib import sha256
import argparse

""" Utility function to return byte chunks from a binary file """
def bytes_from_file(filename, chunksize=8192*1024):
    with open(filename, "rb") as f:
        while True:
            chunk = f.read(chunksize)
            if chunk:
                yield chunk
            else:
                break

""" Verifies a single -verification.json file """               
def verifyHashes(filename, quiet):
    errorCount = 0
    checked = 0

    if (not os.path.exists(filename)):
        print("Specified file does not exist:", filename)
        return -1

    folder = os.path.dirname(filename)
    with codecs.open(filename, "r", "utf-8-sig") as f:
        doc = json.load(f)

    for file in doc:
        filename = file["Name"]
        hash = file["Hash"]
        size = file["Size"]

        fullpath = os.path.join(folder, filename)
        if not os.path.exists(fullpath):
            print("File missing:", fullpath)
            errorCount += 1
        else:
            checked += 1
            if not quiet:
                print("Verifying file", filename)
            hashalg = sha256()
            for b in bytes_from_file(fullpath):
                hashalg.update(b)
            hashval =  base64.b64encode(hashalg.digest())
            hashval = hashval.decode('utf-8')
            if hashval != hash:
                print("*** Hash check failed for file:", fullpath)
                errorCount += 1


    if errorCount > 0:
        print("Errors were found")
    else:
        print("No errors found")

    return errorCount
    

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description='Verify hashes of backup files.')
    parser.add_argument("--quiet", action='store_true', help="Be noisy about each file being verified")
    parser.add_argument("path", type=str, nargs='?',
                    help="""path to the verification file or folder containing the verification file.\
                        Defaulf is curent path""")
    args = parser.parse_args()

    if args.path is None:
        args.path = os.getcwd()

    if not os.path.exists(args.path):
        print("No such file or directory: ", args.path)
    else:
        if os.path.isfile(args.path):
            verifyHashes(args.path, args.quiet)
        else:
            files = 0
            for f in os.listdir(args.path):
                if (f.endswith("-verification.json")):
                    print("Verifying file:", f)
                    files += 1
                    verifyHashes(os.path.join(args.path, f), args.quiet)
            if files == 0:
                print("No verification files in folder:", args.path)
