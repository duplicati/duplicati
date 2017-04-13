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
import ijson
import sqlite3
import zipfile
import codecs
import getpass
import fnmatch
import base64
import hashlib
from collections import OrderedDict
from pyaescrypt import pyAesCryptDecrypt, fail_with_msg

# increase for faster restores, at the cost of higher RAM usage.
maxCacheSizeInMB = 100

def mainRestore(d, outdir, passw, scope):
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
    amountInCache = max(1, (maxCacheSizeInMB * 1024 * 1024) // largestDBlock)
    cacheDecrypted = MemoizeDecorator(pyAesCryptDecrypt, amountInCache)

    # read some metadata from the manifest
    db, numberToName = createDb(d, 'py-restore-index.sqlite', passw, cacheDecrypted)
    dbopts = (db, numberToName, cacheDecrypted, passw)
    opts = getArchiveOptions(d, dlist)

    # restore files
    i = 0
    msgs = 0
    print('Restoring files...')
    for item in enumerateDlistFiles(d, dlist):
        if item['type'] == 'File' and fnmatch.fnmatch(item['path'], scope):
            # print a dot every 10 files to show we're still working
            i += 1
            if i % 10 == 0:
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
                restoreOneFile(d, dbopts, opts, item, outPath)
            except Exception as e:
                msgs += 1
                print(toAscii('\nWhen restoring %s to %s: %s' %
                    (item['path'], outPath, str(e))))
        elif item['type'] == 'Symlink':
            print(toAscii('Symlink existed at ' + item['path']))

    db.close()
    print('\n\n%d warnings/errors seen.' % msgs)

def restoreOneFile(d, dbopts, opts, listEntry, outPath):
    # create destination directory
    if not os.path.isdir(os.path.split(outPath)[0]):
        os.makedirs(os.path.split(outPath)[0])

    # write to file
    with open(outPath, 'wb') as f:
        if 'blocklists' not in listEntry or not listEntry['blocklists']:
            # small files store data in one block
            data = getContentBlock(d, dbopts, listEntry['hash'])
            f.write(data)
        else:
            # large files point to a list of blockids, each of which points
            # to another list of blockids
            for blhi, blh in enumerate(listEntry['blocklists']):
                blockhashoffset = blhi * opts['hashes-per-block'] * opts['blocksize']
                binaryHashes = getContentBlock(d, dbopts, blh)
                for bi, start in enumerate(range(0, len(binaryHashes), opts['hash-size'])):
                    thehash = binaryHashes[start: start + opts['hash-size']]
                    thehash = base64.b64encode(thehash)
                    data = getContentBlock(d, dbopts, thehash)
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
    got = base64.b64encode(hasher.digest())
    if expected != got:
        raise Exception('Restored %s. expected checksum %s and got %s' %
            (outPath, expected, got))

def getContentBlock(d, dbopts, blockId):
    if isinstance(blockId, bytes):
        blockId = blockId.decode('utf8')
    db, numberToName, cacheDecrypted, passw = dbopts
    name = getFilenameFromBlockId(db, numberToName, blockId)
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

def createDb(d, filename, passw, cacheDecrypted):
    # get a summary of the current dblocks
    zipfilenames = [s for s in os.listdir(d) if
        s.endswith('.dblock.zip') or s.endswith('.dblock.zip.aes')]
    zipfilenames.sort()
    filenamesAndSizes = ';'.join(zipfilenames)
    filenamesAndSizes += ';'.join(map(str,
        [os.path.getsize(os.path.join(d, s)) for s in zipfilenames]))
    needNew = True
    dbpath = os.path.join(d, filename)
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

def getFilenameFromBlockId(db, numberToName, blockId):
    c = db.cursor()
    if isinstance(blockId, str):
        blockId = blockId.encode('utf8')
    rows = c.execute('SELECT FileNum FROM BlockIdToFile WHERE BlockId=?', [blockId])
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
def MemoizeDecorator(fn, cachesize):
    cache = OrderedDict()
    def memoize_wrapper(*args, **kwargs):
        import pickle
        key = pickle.dumps((args, kwargs))
        try:
            return cache[key]
        except KeyError:
            result = fn(*args, **kwargs)
            cache[key] = result
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
                opts['hashes-per-block'] = (opts['blocksize'] //
                    (opts['block-hasher']().digest_size))
    return opts

def main():
    print('Welcome to Python Duplicati recovery.')
    d = input('Please type the full path to a directory with Duplicati\'s .aes or .zip files:')
    assertTrue(os.path.isdir(d), 'Directory not found')
    scope = input('Please type * to restore all files, or a pattern like /path/to/files/* to ' +
        'restore the files in a certain directory)')
    outdir = input('Please enter the path to an empty destination directory:')
    assertTrue(os.path.isdir(outdir), 'Output directory not found')
    assertTrue(len(os.listdir(outdir)) == 0, 'Output directory not empty')
    if sys.platform.startswith('win') and len(outdir) > 40:
        print('note: paths on windows have limited length, you might want to consider a shorter output path.')

    # get password
    passw = None
    if any(name.endswith('.aes') for name in os.listdir(d)):
        passw = str(getpass.getpass("Password:"))

    mainRestore(d, outdir, passw, scope)
    print('Complete.')

if __name__ == '__main__':
    main()
