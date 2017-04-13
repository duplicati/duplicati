# pyAesCrypt 0.1.2
# Copyright 2016 Marco Bellaccini - marco.bellaccini[at!]gmail.com
# small modifications by Ben Fisher to add fncallback feature
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

import os

def pyAesCryptDecrypt(path, passw, fncallback=None):
    from Crypto.Hash import SHA256
    from Crypto.Hash import HMAC
    from Crypto.Cipher import AES

    outbytes = b''
    assert path.endswith('.aes'), '%s expected to end with .aes' % path
    with open(path, "rb") as fIn:
        fdata = fIn.read(3)
        # check if file is in AES Crypt format (also min length check)
        if fdata != bytes("AES", "utf8") or os.stat(path).st_size < 136:
            fail_with_msg("Error: file is corrupted or " +
                "not an AES Crypt (or pyAesCrypt) file.")

        # check if file is in AES Crypt format, version 2
        # (the only one compatible with pyAesCrypt)
        fdata = fIn.read(1)
        if len(fdata) < 1:
            fail_with_msg("Error: file is corrupted.")
        if fdata != b"\x02":
            fail_with_msg("Error: pyAesCrypt is only compatible with version 2 of the " +
                "AES Crypt file format.")

        # skip reserved byte
        fIn.read(1)

        # skip all the extensions
        while True:
            fdata = fIn.read(2)
            if len(fdata) < 2:
                fail_with_msg("Error: file is corrupted.")
            if fdata == b"\x00\x00":
                break
            fIn.read(int.from_bytes(fdata, byteorder="big"))

        # read external iv
        iv1 = fIn.read(16)
        if len(iv1) < 16:
            fail_with_msg("Error: file is corrupted.")

        # stretch password and iv
        key=pyAesCryptStretch(passw, iv1)

        # read encrypted main iv and key
        c_iv_key = fIn.read(48)
        if len(c_iv_key) < 48:
            fail_with_msg("Error: file is corrupted.")

        # read HMAC-SHA256 of the encrypted iv and key
        hmac1 = fIn.read(32)
        if len(hmac1) < 32:
            fail_with_msg("Error: file is corrupted.")

        # compute actual HMAC-SHA256 of the encrypted iv and key
        hmac1Act = HMAC.new(key, digestmod=SHA256)
        hmac1Act.update(c_iv_key)

        # HMAC check
        if hmac1 != hmac1Act.digest():
            fail_with_msg("Error: wrong password (or file is corrupted).")

        # instantiate AES cipher
        cipher1 = AES.new(key, AES.MODE_CBC, iv1)

        # decrypt main iv and key
        iv_key = cipher1.decrypt(c_iv_key)

        # get internal iv and key
        iv0 = iv_key[:16]
        intKey = iv_key[16:]

        # instantiate another AES cipher
        cipher0 = AES.new(intKey, AES.MODE_CBC, iv0)

        # instantiate actual HMAC-SHA256 of the ciphertext
        hmac0Act = HMAC.new(intKey, digestmod=SHA256)

        # decrypt ciphertext in large pieces first, then smaller pieces
        sizeInputFile = os.stat(path).st_size
        for currentBufferSize in [64*64*AES.block_size, 64*AES.block_size, AES.block_size]:
            assert 0 == currentBufferSize % AES.block_size
            while fIn.tell() < sizeInputFile - 32 - 1 - currentBufferSize:
                # read data
                cText = fIn.read(currentBufferSize)
                # update HMAC
                hmac0Act.update(cText)
                # decrypt data
                if fncallback:
                    fncallback(cipher0.decrypt(cText))
                else:
                    outbytes += bytearray(cipher0.decrypt(cText))

        # last block reached, remove padding if needed
        # read last block
        if fIn.tell() != os.stat(path).st_size - 32 - 1:  # this is for empty files
            cText = fIn.read(AES.block_size)
            if len(cText) < AES.block_size:
                fail_with_msg("Error: file is corrupted.")
        else:
            cText = bytes()

        # update HMAC
        hmac0Act.update(cText)

        # read plaintext file size mod 16 lsb positions
        fs16 = fIn.read(1)
        if len(fs16) < 1:
            fail_with_msg("Error: file is corrupted.")

        # decrypt last block
        pText = cipher0.decrypt(cText)

        # remove padding
        toremove=((16-fs16[0])%16)
        if toremove != 0:
            pText=pText[:-toremove]

        if fncallback:
            fncallback(pText)
        else:
            outbytes += bytearray(pText)

        # read HMAC-SHA256 of the encrypted file
        hmac0 = fIn.read(32)
        if len(hmac0) < 32:
            fail_with_msg("Error: file is corrupted.")

        # HMAC check
        if hmac0 != hmac0Act.digest():
            fail_with_msg("Error: bad HMAC (file is corrupted).")
    return outbytes

def pyAesCryptStretch(passw, iv1):
    # hash the external iv and the password 8192 times
    from Crypto.Hash import SHA256
    digest=iv1+(16*b"\x00")

    for _ in range(8192):
        passHash=SHA256.new()
        passHash.update(digest)
        passHash.update(bytes(passw,"utf_16_le"))
        digest=passHash.digest()

    return digest

def fail_with_msg(s):
    print(s)
    raise Exception(s)
