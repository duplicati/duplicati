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
def bytes_from_file(filename, buffer_size=8192):
    with open(filename, "rb") as f:
        while True:
            chunk = f.read(buffer_size)
            if chunk:
                yield chunk
            else:
                break

""" Verifies a single -verification.json file """               
def verifyHashes(filename, quiet = True, buffer_size=8192):
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
        state = file["State"]

        fullpath = os.path.join(folder, filename)
        if not os.path.exists(fullpath):
            if state != 5:
                print("File missing:", fullpath)
                errorCount += 1
        else:
            checked += 1
            if not quiet:
                print("Verifying file", filename)
            hashalg = sha256()
            for b in bytes_from_file(fullpath, buffer_size):
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
    parser.add_argument("--buffer_size", type=int, default=8, help="Buffer size for file IO (in kb). \
        Default value is 8. \
        Increasing to size of blockfile can increase verification speed")
    parser.add_argument("path", type=str, nargs='?',
                    help="""path to the verification file or folder containing the verification file.\
                        Defaulf is curent path""")
    parser.add_argument("--stats_only", action='store_true', help="display statistics from json only, no verification")
    args = parser.parse_args()

    if not args.stats_only: # if stats_only arg present skip file verification
        if args.path is None:
            args.path = os.getcwd()

        if not os.path.exists(args.path):
            print("No such file or directory: ", args.path)
        else:
            if os.path.isfile(args.path):
                verifyHashes(args.path, args.quiet, args.buffer_size * 1024)
            else:
                files = 0
                for f in os.listdir(args.path):
                    if (f.endswith("-verification.json")):
                        print("Verifying file:", f)
                        files += 1
                        verifyHashes(os.path.join(args.path, f), args.quiet, args.buffer_size *1024)
                if files == 0:
                    print("No verification files in folder:", args.path)
    else:
        print("Verify skipped, displaying statistics only ")


""" Calculate statistics """   
class Statistics:

    def get_json_for_statistics(): #copied from main
        files = 0
        for f in os.listdir(args.path):
            if (f.endswith("-verification.json")):
                print("Statistics for file", f)
                files += 1
                statistics_source = Statistics.load_json_for_statistics(os.path.join(args.path, f), args.quiet, args.buffer_size *1024)
                return statistics_source
        if files == 0:
            print("No verification files in folder:", args.path)

    def load_json_for_statistics(filename, quiet = True, buffer_size=8192): #copied from verifyHashes
        if (not os.path.exists(filename)):
            print("Specified file does not exist:", filename)
            return -1
        folder = os.path.dirname(filename)
        with codecs.open(filename, "r", "utf-8-sig") as f:
            doc = json.load(f)
            statistics_source = doc
        return statistics_source

    """ Statistics legend """ 
    RemoteVolumeState = [
        # Indicates that the remote volume is being created
        "Temporary",
        # Indicates that the remote volume is being uploaded
        "Uploading",
        # Indicates that the remote volume has been uploaded
        "Uploaded",
        # Indicates that the remote volume has been uploaded, and seen by a list operation
        "Verified",
        # Indicattes that the remote volume should be deleted
        "Deleting",
        # Indicates that the remote volume was successfully deleted from the remote location
        "Deleted"]
    RemoteVolumeType = [
        # Contains data blocks
        "Blocks",
        # Contains file lists
        "Files",
        # Contains redundant lookup information
        "Index"]

    """ Utility convert bytes to readable units for statistics """
    def convert_bytes_to_readable(size):
        if size is None:
            size = 0
            return size
        if size < 0: 
            size = 0 #because for "RemoteVolumeState 5" (Deleted) is size in json "-1"
            return size
        for x in ['bytes', 'KB', 'MB', 'GB', 'TB']:
            if size < 1024.0:
                return "%3.1f %s" % (size, x)
            size /= 1024.0  
        return size


    def compute_statistics():
        index_main_loop = 0
        index_sub_loop = 0
        list_of_all_types = []
        list_of_all_states = []
        dict_with_sizes_of_all_items= {}
        total_size = 0

        #main statistics loop 
        statistics_source = Statistics.get_json_for_statistics() # call load data from json
        for file in statistics_source:
            #loop for geting size for each RemoteVolumeType to dict
            for each_type in Statistics.RemoteVolumeType: #for each json file for each type ID
                current_type = (statistics_source[index_main_loop]["Type"])
                if current_type == index_sub_loop:
                    dict_with_sizes_of_all_items[each_type] = dict_with_sizes_of_all_items.get(each_type,0) + (statistics_source[index_main_loop]["Size"])
                index_sub_loop += 1
                if index_sub_loop >2:
                    index_sub_loop = 0
            #loop for geting size for each RemoteVolumeState to dict
            for each_state in Statistics.RemoteVolumeState:
                current_type = (statistics_source[index_main_loop]["State"])
                if current_type == index_sub_loop:
                    dict_with_sizes_of_all_items[each_state] = dict_with_sizes_of_all_items.get(each_state,0) + (statistics_source[index_main_loop]["Size"])
                index_sub_loop += 1
                if index_sub_loop >5:
                    index_sub_loop = 0
            #store Type and State to list
            list_of_all_types.append(statistics_source[index_main_loop]["Type"])
            list_of_all_states.append(statistics_source[index_main_loop]["State"])
            index_main_loop += 1

        print("\n")
        index_main_loop = 0
        #Print number of files and size from dict
        for state in Statistics.RemoteVolumeState:
            print(state,"count",list_of_all_states.count(Statistics.RemoteVolumeState.index(state)),"Size ",Statistics.convert_bytes_to_readable(dict_with_sizes_of_all_items.get(state)))
            index_main_loop += 1
        print("\n")

        #Print number of files and size from dict
        for type in Statistics.RemoteVolumeType:
            print(type,"count",list_of_all_types.count(Statistics.RemoteVolumeType.index(type)),"size:",Statistics.convert_bytes_to_readable(dict_with_sizes_of_all_items.get(type)))

        #Print total size
        for key in Statistics.RemoteVolumeType:
            total_size += dict_with_sizes_of_all_items.get(key)
        print("\n""Total size:",Statistics.convert_bytes_to_readable(total_size) )

#to run statistics
Statistics.compute_statistics()


