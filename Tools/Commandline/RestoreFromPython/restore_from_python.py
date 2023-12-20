#!/usr/bin/env python3

# by Ben Fisher, https://github.com/downpoured
# a Python script to restore files from Duplicati
# similar to Duplicati.RecoveryTool, but with no dependencies on Mono/.NET
# uses streaming apis to restore a large number of files and use limited RAM.
# supports backups using AES encryption (.aes) or No Encryption (.zip),
# if data uses GPG/other encryption, decrypt files to .zip before running this tool.

import argparse
import datetime
from datetime import datetime as dt, timedelta as td
import os
import sys
import io
import json
import ijson
import sqlite3
import zipfile
import codecs
import getpass
import fnmatch
import base64
import hashlib
import time
import traceback
from collections import OrderedDict
from pyaescrypt import pyAesCryptDecrypt, fail_with_msg

def mainRestore(options):
    d = options.backup_directory
    outdir = options.output_directory
    passw = options.password
    scope = options.scope_directory
    # locate dlist
    dlists = [name for name in os.listdir(d) if (name.endswith('.dlist.zip') or
        name.endswith('.dlist.zip.aes'))]

    if dlists:
        dlist = sorted(dlists, reverse=True)[0]
        print('using %s which looks like the most recent dlist.' % dlist)

        # decrypt dlist file to disk
        if dlist.endswith('.dlist.zip.aes'):
            with open(os.path.join(d, 'py-restore-dlist-decr.zip'), 'wb') as f:
                pyAesCryptDecrypt(os.path.join(d, dlist), passw, f.write)
                dlist = os.path.join(d, 'py-restore-dlist-decr.zip')
    else:
        fail_with_msg('No .dlist.zip files found.')

    # create cache
    largestDBlock = max(os.path.getsize(os.path.join(d, name))
        for name in os.listdir(d) if '.dblock.zip' in name)
    maximum = int(options.max_cache_size) * 1024 * 1024
    amountInCache = max(1, maximum // largestDBlock)
    if options.debug: print("max cache size: %d, largest db block: %d, amount in cache: %d" % (maximum, largestDBlock, amountInCache))
    cacheDecrypted = MemoizeDecorator(pyAesCryptDecrypt, amountInCache, options.debug)

    # read some metadata from the manifest
    db, numberToName = createDb(d, 'py-restore-index.sqlite', passw, cacheDecrypted)
    dbopts = (db, numberToName, cacheDecrypted, passw)
    if options.debug: print("numbertoname=%s" % numberToName)
    opts = getArchiveOptions(d, dlist)

    if options.debug:
        print("options archive: %s" % opts)

    # restore files
    i = 0
    msgs = 0
    print('Restoring files...')
    for item in enumerateDlistFiles(d, dlist):
        if options.debug:
            print("begin restore for file: %s" % item['path'])

        if item['type'] == 'File' and fnmatch.fnmatch(item['path'], scope):
            # print a dot every 10 files to show we're still working
            i += 1
            if not options.debug and i % 10 == 0:
                sys.stdout.write('.')
                sys.stdout.flush()

            if item['path'].startswith('\\\\'):
                # windows network share
                outPath = outdir + item['path'][1:]
            elif item['path'][1:2] == ':' and item['path'][2:3] == '\\':
                # windows absolute path
                outPath = outdir + '\\' + item['path'][0] + item['path'][2:]
            else:
                outPath = outdir + item['path']

            try:
                restoreOneFile(d, dbopts, opts, item, outPath, options.debug)
            except Exception as e:
                _, _, tb = sys.exc_info()
                msgs += 1
                te = traceback.extract_tb(tb)
                fs = te[len(te)-1]
                print(toAscii('\nWhen restoring %s to %s: %s (%s at line %d)' %
                    (item['path'], outPath, str(e), os.path.split(fs.filename)[1], fs.lineno)))

        elif item['type'] == 'Symlink':
            print(toAscii('Symlink existed at ' + item['path']))

    db.close()
    print('\n\n%d warnings/errors seen.' % msgs)

def restoreOneFile(d, dbopts, opts, listEntry, outPath, debug):
    # create destination directory
    if not os.path.isdir(os.path.split(outPath)[0]):
        os.makedirs(os.path.split(outPath)[0])

    # write to file
    with open(outPath, 'wb') as f:
        if 'blocklists' not in listEntry or not listEntry['blocklists']:
            # small files store data in one block
            if listEntry["size"] != 0:
                if debug: print("get one block hash %s" % listEntry['hash'])
                data = getContentBlock(d, dbopts, listEntry['hash'], debug)
                f.write(data)
            elif debug:
                print("file empty, skip to restore metadata")
        else:
            # large files point to a list of blockids, each of which points
            # to another list of blockids
            if debug: print("Hash blocks list %s" % listEntry['blocklists'])
            for blhi, blh in enumerate(listEntry['blocklists']):
                blockhashoffset = blhi * opts['hashes-per-block'] * opts['blocksize']
                if debug:
                    print("hash: %s num_hash: %d, blockhashoffset: %d" % (blh, blhi, blockhashoffset))
                binaryHashes = getContentBlock(d, dbopts, blh, debug)
                if debug:
                    print("got %d binary hashes" % (len(binaryHashes)/opts['hash-size']))
                for bi, start in enumerate(range(0, len(binaryHashes), opts['hash-size'])):
                    thehash = binaryHashes[start: start + opts['hash-size']]
                    thehash = base64.b64encode(thehash)
                    data = getContentBlock(d, dbopts, thehash, debug)
                    f.seek(blockhashoffset + bi * opts['blocksize'])
                    f.write(data)

    # verify file size
    if listEntry['size'] != os.path.getsize(outPath):
        raise Exception('Restored %s. expected filesize %d and got %d' %
            (outPath, listEntry['size'], os.path.getsize(outPath)))

    # verify file checksum
    hasher = opts['file-hasher']()
    computeHash(outPath, hasher)
    expected = listEntry['hash'].encode('utf8')
    x = hasher.digest()
    got = base64.b64encode(x)
    if debug:
        print("restored file: %s expected hash=%s, result=%s" % (outPath, expected, got))
    if expected != got:
        raise Exception('Restored %s. expected checksum %s and got %s' %
            (outPath, expected, got))
    restore_metadata(d, dbopts, listEntry['metahash'], outPath, debug)

def restore_unix(outPath, js, debug):
    ugp = js.get("unix:uid-gid-perm")
    if debug: print("restore rights/perm with: %s" % ugp)
    uid, gid, perm = [int(x) for x in ugp.split("-")]
    os.chmod(outPath, perm)
    os.chown(outPath, uid, gid)

def restore_windows_metadata(outPath, js, debug):
    if debug: print("TODO: restore windows metadata from : %s" % str(js))

# TODO:restore metadata
def restore_metadata(d, dbopts, metahash, outPath, debug):
    if debug:
        print("begin restore metadata for file: %s" % outPath)
    data = getContentBlock(d, dbopts, metahash, debug)
    js = json.loads(data)
    lws = int(js["CoreLastWritetime"])/10
    ct = dt(1,1,1,tzinfo=datetime.timezone.utc) + td(microseconds=lws)
    # do not use mktime, it uses local time
    mtime = ct.timestamp()
    os.utime(outPath, (mtime, mtime))
    if (js.get("unix:owner-name")):
        restore_unix(outPath, js, debug)
    else:
        restore_windows(outPath, js, debug)


def getContentBlock(d, dbopts, blockId, debug):
    if isinstance(blockId, bytes):
        blockId = blockId.decode('utf8')
    db, numberToName, cacheDecrypted, passw = dbopts
    name = getFilenameFromBlockId(db, numberToName, blockId, debug)
    if debug: print("getting content from hash %s in block file %s"  % (blockId, name))
    with openAsZipFile(d, name, passw, cacheDecrypted) as z:
        with z.open(base64PlainToBase64Url(blockId), 'r') as zipContents:
            return zipContents.read()

def openAsZipFile(d, name, passw, cacheDecrypted):
    fullpath = os.path.join(d, name)
    assertTrue(os.path.exists(fullpath), 'missing %s' % fullpath)
    if name.endswith('.zip'):
        return zipfile.ZipFile(fullpath, 'r')
    else:
        data = io.BytesIO(cacheDecrypted(fullpath, passw))
        return zipfile.ZipFile(data, 'r')

def enumerateDlistFiles(d, dlist):
    convertStreamToUtf8 = codecs.getreader('utf-8-sig')
    with zipfile.ZipFile(os.path.join(d, dlist), 'r') as z:
        with z.open('filelist.json', 'r') as zipentry:
            with convertStreamToUtf8(zipentry) as zipentryutf8:
                for item in streamJsonArrayItems(zipentryutf8):
                    yield item

def streamJsonArrayItems(f):
    # read items from a json array -- without loading the entire file into memory
    level = 0
    currentObject = ijson.ObjectBuilder()
    parsed = ijson.parse(f)

    # eat the initial start_array event
    assertEqual('start_array', next(parsed)[1])

    # construct objects. use level in order to support objects within objects
    for _, event, value in parsed:
        currentObject.event(event, value)
        if event == 'start_map':
            level += 1
        elif event == 'end_map':
            level -= 1
            if level == 0:
                yield currentObject.value
                currentObject = ijson.ObjectBuilder()

    # ignore the final end_array event.

# the DB caches a relationship between blockIDs and dblock files.
def createDb(d, db_filename, passw, cacheDecrypted):
    # get a summary of the current dblocks
    zipfilenames = [s for s in os.listdir(d) if
        s.endswith('.dblock.zip') or s.endswith('.dblock.zip.aes')]
    zipfilenames.sort()
    filenamesAndSizes = ';'.join(zipfilenames)
    filenamesAndSizes += ';'.join(map(str,
        [os.path.getsize(os.path.join(d, s)) for s in zipfilenames]))
    needNew = True
    dbpath = os.path.join(d, db_filename)
    if os.path.exists(dbpath):
        # check that the dblocks we have match the dblocks this db has.
        dbCheckIfComplete = sqlite3.connect(dbpath)
        cursor = dbCheckIfComplete.cursor()
        needNew = not cursor.execute('''SELECT FileNum FROM BlockIdToFile
            WHERE BlockId=?''', [filenamesAndSizes.encode('utf8')]).fetchone()
        cursor.close()
        dbCheckIfComplete.close()

    db = sqlite3.connect(dbpath)
    cursor = db.cursor()
    cursor.execute("PRAGMA temp_store = memory")
    cursor.execute("PRAGMA page_size = 16384")
    cursor.execute("PRAGMA cache_size = 1000")
    cursor.close()
    numberToName = OrderedDict((n + 1, v) for n, v in enumerate(zipfilenames))
    if needNew:
        print('Creating index, this may take some time...')
        createBlockIdsToFilenames(d, db, passw, cacheDecrypted,
            numberToName, filenamesAndSizes)
    else:
        print('Able to re-use existing index.')

    return db, numberToName

def createBlockIdsToFilenames(d, db, passw, cache, numberToName, filenamesAndSizes):
    # create an index mapping blockId to filename
    with db:
        c = db.cursor()
        c.execute('''CREATE TABLE IF NOT EXISTS BlockIdToFile (
            BlockId TEXT,
            FileNum INTEGER)''')
        c.execute('''CREATE INDEX IF NOT EXISTS IxBlockId ON BlockIdToFile(BlockId)''')
        c.execute('''DELETE FROM BlockIdToFile WHERE 1''')
        for num in numberToName:
            name = numberToName[num]
            sys.stdout.write('.')
            sys.stdout.flush()
            with openAsZipFile(d, name, passw, cache) as z:
                for entryname in z.namelist():
                    if entryname == 'manifest': continue
                    entryname = base64UrlToBase64Plain(entryname)
                    c.execute('INSERT INTO BlockIdToFile (BlockId, FileNum) VALUES (?, ?)',
                             [entryname.encode('utf8'), num])

        # write a summary of the current dblocks
        c.execute('INSERT INTO BlockIdToFile (BlockId, FileNum) VALUES (?, ?)',
                 [filenamesAndSizes.encode('utf8'), -1])
        c.close()
        db.commit()

    return numberToName

def base64PlainToBase64Url(data):
    if isinstance(data, bytes): return data.replace(b'+', b'-').replace(b'/', b'_')
    else: return data.replace('+', '-').replace('/', '_')

def base64UrlToBase64Plain(data):
    if isinstance(data, bytes): return data.replace(b'-', b'+').replace(b'_', b'/')
    else: return data.replace('-', '+').replace('_', '/')

def computeHash(path, hasher, buffersize=64 * 1024):
    with open(path, 'rb') as f:
        while True:
            buffer = f.read(buffersize)
            if not buffer:
                break
            hasher.update(buffer)

def getFilenameFromBlockId(db, numberToName, blockId, debug):
    c = db.cursor()
    if isinstance(blockId, str):
        blockId = blockId.encode('utf8')
    rows = c.execute('SELECT FileNum FROM BlockIdToFile WHERE BlockId=?', [blockId])
    s = None
    for row in rows:
        return numberToName[row[0]]
    assertTrue(False, 'block id %s not found' % blockId)
    c.close()

def toAscii(s):
    import unicodedata
    s = unicodedata.normalize('NFKD', str(s))
    return s.encode('ascii', 'ignore').decode('ascii')

def assertEqual(v, expect, context=''):
    if v != expect:
        s = 'Not equal: ' + context + ' Expected ' + expect + ' but got ' + v
        raise AssertionError(toAscii(s))

def assertTrue(condition, *context):
    if not condition:
        s = ' '.join(context) if context else ''
        raise AssertionError(toAscii(s))

# code.activestate.com/recipes/496879-memoize-decorator-function-with-cache-size-limit/
def MemoizeDecorator(fn, cachesize, debug):
    cache = OrderedDict()
    def memoize_wrapper(*args, **kwargs):
        import pickle
        key = pickle.dumps((args, kwargs))
        try:
            return cache[key]
        except KeyError:
            if debug:
                t = time.time()
            result = fn(*args, **kwargs)
            cache[key] = result
            if debug:
                t2 = time.time()
                print("block cached, key: %s, bytes: %d necessary time %3.3f" % (key, len(result), round(t2-t,3)))
            if len(cache) > memoize_wrapper._limit:
                # remove like in a FIFO queue
                cache.popitem(False)
            return result

    memoize_wrapper._limit = cachesize
    memoize_wrapper._cache = cache
    return memoize_wrapper

def getHasherObject(hashalg):
    hashalg = hashalg.lower()
    if hashalg == 'sha1': return hashlib.sha1
    elif hashalg == 'md5': return hashlib.md5
    elif hashalg == 'sha256': return hashlib.sha256
    elif hashalg == 'sha384': return hashlib.sha384
    elif hashalg == 'sha512': return hashlib.sha512
    else: assertTrue(False, 'unknown hash algorithm %s' % hashalg)

def getArchiveOptions(d, dlist):
    opts = {}
    convertStreamToUtf8 = codecs.getreader('utf-8-sig')
    with zipfile.ZipFile(os.path.join(d, dlist), 'r') as z:
        with z.open('manifest', 'r') as zipentry:
            with convertStreamToUtf8(zipentry) as zipentryutf8:
                alljson = zipentryutf8.read()
                manifest = json.loads(alljson)
                assertEqual(manifest['BlockHash'], manifest['FileHash'],
                    'script currently needs same hash method for blockhash and filehash')
                opts['blocksize'] = int(manifest['Blocksize'])
                opts['block-hasher'] = getHasherObject(manifest['BlockHash'])
                opts['file-hasher'] = getHasherObject(manifest['FileHash'])
                opts['hash-size'] = opts['block-hasher']().digest_size
                opts['hashes-per-block'] = opts['blocksize'] // opts['hash-size']
    return opts

def parse_options():
    parser = argparse.ArgumentParser(
        description="Restore Duplicati files using python"
    )
    parser.add_argument(
        "-b",
        "--backup-directory",
        metavar="<backup directory>",
        help="full path to a directory with Duplicati\'s .aes or .zip files",
    )
    parser.add_argument(
        "-s",
        "--scope-directory",
        metavar="<scope directory>",
        help="* or pattern like /path/to/files/*",
    )
    parser.add_argument(
        "-o",
        "--output-directory",
        metavar="<output directory>",
        help="full path to an empty destination directory",
    )
    parser.add_argument(
        "-p",
        "--password",
        metavar="<cipher phrase",
        help="cipher phrase",
    )
    parser.add_argument(
        "-a",
        "--aesdecrypt",
        action="store_true",
        help="use aesdecrypt if available",
    )
    parser.add_argument(
        "-c",
        "--max-cache-size",
        metavar="<max cache size",
        default = 200,
        help="maximum cache size in MB (increase for faster restores, at the cost of higher RAM usage)",
    )

    parser.add_argument(
        "-d", "--debug", action="store_true", help="more debug output"
    )
    options = parser.parse_args()
    return options


def decrypt_dir(d, aes_filenames, password, debug):
    # is aesdecrypt available on the PATH ?
    from shutil import which
    if not which('aescrypt'):
        return d
    if debug: print("aescrypt is found, try to use it")
    new_path = os.path.join(d, "decrypted")
    if os.path.exists(new_path):
        return new_path
    if debug: print("aescrypt is found, try to use it")
    os.mkdir(new_path)
    for aes in aes_filenames:
        zip_path = os.path.join(new_path, os.path.splitext(aes)[0])
        aes_path = os.path.join(d, aes)
        os.system("aescrypt -d -p %s -o %s %s" % (password, zip_path, aes_path))
    if debug: print("decrypted files in %s" % new_path)
    return new_path


def main():
    options = parse_options()
    print('Welcome to Python Duplicati recovery.')

    d = options.backup_directory
    if not d: d = input('Please type the full path to a directory with Duplicati\'s .aes or .zip files:')
    assertTrue(os.path.isdir(d), 'Directory not found')

    # get password
    passw = options.password
    aes_filenames = [s for s in os.listdir(d) if s.endswith('.aes')]
    if aes_filenames:
        if not passw: passw = str(getpass.getpass("Password:"))
    options.password = passw

    options.backup_directory = decrypt_dir(d, aes_filenames, options.password, options.debug) if options.password and aes_filenames else d

    scope = options.scope_directory
    if not scope: scope = input('Please type * to restore all files, or a pattern like /path/to/files/* to ' +
        'restore the files in a certain directory)')
    options.scope_directory = scope
    outdir = options.output_directory
    if not outdir: outdir = input('Please enter the path to an empty destination directory:')
    assertTrue(os.path.isdir(outdir), 'Output directory not found')
    assertTrue(len(os.listdir(outdir)) == 0, 'Output directory not empty')
    if sys.platform.startswith('win') and len(outdir) > 40:
        print('note: paths on windows have limited length, you might want to consider a shorter output path.')
    options.output_directory = outdir

    # get password
    passw = options.password
    if any(name.endswith('.aes') for name in os.listdir(d)):
        if not passw: passw = str(getpass.getpass("Password:"))
    options.password = passw

    mainRestore(options)
    print('Complete.')

if __name__ == '__main__':
    main()
