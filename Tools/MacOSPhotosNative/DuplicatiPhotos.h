// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

#ifndef DUPLICATI_PHOTOS_H
#define DUPLICATI_PHOTOS_H

#include <stddef.h>
#include <stdint.h>
#include <sys/types.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct DuplicatiPhotosAssetMetadata {
    char *identifier;
    char *filename;
    char *uti;
    int64_t size;
    int32_t mediaType;
    int32_t pixelWidth;
    int32_t pixelHeight;
    double creationDateSeconds;
    double modificationDateSeconds;
} DuplicatiPhotosAssetMetadata;

int DuplicatiPhotosEnumerateAssets(DuplicatiPhotosAssetMetadata **assetsOut, size_t *countOut, char **errorMessageOut);
void DuplicatiPhotosFreeAssets(DuplicatiPhotosAssetMetadata *assets, size_t count);

int DuplicatiPhotosOpenAsset(const char *identifier, void **handleOut, char **errorMessageOut);
ssize_t DuplicatiPhotosReadAsset(void *handle, uint8_t *buffer, size_t bufferLength, char **errorMessageOut);
int DuplicatiPhotosGetAssetSize(void *handle, int64_t *sizeOut, char **errorMessageOut);
void DuplicatiPhotosCloseAsset(void *handle);

void DuplicatiPhotosFreeString(char *value);

#ifdef __cplusplus
}
#endif

#endif // DUPLICATI_PHOTOS_H
