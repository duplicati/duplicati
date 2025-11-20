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

#import "DuplicatiPhotos.h"

#import <CoreFoundation/CoreFoundation.h>
#import <Foundation/Foundation.h>
#import <Photos/Photos.h>
#include <math.h>
#include <pthread.h>
#include <stdlib.h>
#include <string.h>

// Static variable to control logging, initialized from environment variable
static int g_loggingEnabled = -1;
static pthread_mutex_t g_loggingMutex = PTHREAD_MUTEX_INITIALIZER;

static void DuplicatiPhotosInitializeLogging(void) {
    if (g_loggingEnabled == -1) {
        g_loggingEnabled = getenv("DEBUG_PHOTOKIT") ? 1 : 0;
    }
}

#define DLog(...) do { \
    if (g_loggingEnabled == -1) DuplicatiPhotosInitializeLogging(); \
    if (g_loggingEnabled) { \
        pthread_mutex_lock(&g_loggingMutex); \
        NSLog(__VA_ARGS__); \
        pthread_mutex_unlock(&g_loggingMutex); \
    } \
} while(0)

static char *DuplicatiPhotosCopyCString(NSString *value) {
    DLog(@"DuplicatiPhotos: DuplicatiPhotosCopyCString called with value: %@", value ?: @"(null)");
    if (!value) {
        return NULL;
    }

    const char *utf8 = [value cStringUsingEncoding:NSUTF8StringEncoding];
    if (!utf8) {
        return NULL;
    }

    size_t length = strlen(utf8);
    char *copy = malloc(length + 1);
    if (!copy) {
        DLog(@"DuplicatiPhotos: DuplicatiPhotosCopyCString failed to allocate memory");
        return NULL;
    }

    memcpy(copy, utf8, length);
    copy[length] = '\0';
    DLog(@"DuplicatiPhotos: DuplicatiPhotosCopyCString returning copy of length %zu", length);
    return copy;
}

static char *DuplicatiPhotosCopyError(const char *message) {
    DLog(@"DuplicatiPhotos: DuplicatiPhotosCopyError called with message: %s", message ?: "(null)");
    if (!message) {
        return NULL;
    }

    size_t length = strlen(message);
    char *copy = malloc(length + 1);
    if (!copy) {
        DLog(@"DuplicatiPhotos: DuplicatiPhotosCopyError failed to allocate memory");
        return NULL;
    }

    memcpy(copy, message, length);
    copy[length] = '\0';
    DLog(@"DuplicatiPhotos: DuplicatiPhotosCopyError returning copy");
    return copy;
}

static char *DuplicatiPhotosCopyErrorFromString(NSString *value) {
    DLog(@"DuplicatiPhotos: DuplicatiPhotosCopyErrorFromString called with value: %@", value ?: @"(null)");
    if (!value) {
        return NULL;
    }

    return DuplicatiPhotosCopyCString(value);
}

static PHAssetResource *DuplicatiPhotosSelectResource(PHAsset *asset) {
    DLog(@"DuplicatiPhotos: DuplicatiPhotosSelectResource called for asset: %@", asset.localIdentifier);
    NSArray<PHAssetResource *> *resources = [PHAssetResource assetResourcesForAsset:asset];
    DLog(@"DuplicatiPhotos: Found %lu resources for asset", (unsigned long)resources.count);
    if (resources.count == 0) {
        return nil;
    }

    PHAssetResource *selected = resources.firstObject;
    for (PHAssetResource *candidate in resources) {
        PHAssetResourceType type = candidate.type;
        DLog(@"DuplicatiPhotos: Evaluating resource type: %ld", (long)type);
        if (type == PHAssetResourceTypeFullSizePhoto ||
            type == PHAssetResourceTypePhoto ||
            type == PHAssetResourceTypeFullSizePairedVideo ||
            type == PHAssetResourceTypeVideo ||
            type == PHAssetResourceTypeAudio) {
            selected = candidate;
            DLog(@"DuplicatiPhotos: Selected resource type: %ld", (long)type);
            break;
        }
    }

    DLog(@"DuplicatiPhotos: Returning selected resource: %@", selected.originalFilename);
    return selected;
}

int DuplicatiPhotosEnumerateAssets(DuplicatiPhotosAssetMetadata **assetsOut, size_t *countOut, char **errorMessageOut) {
    DLog(@"DuplicatiPhotos: DuplicatiPhotosEnumerateAssets called");
    if (!assetsOut || !countOut) {
        DLog(@"DuplicatiPhotos: Invalid arguments to DuplicatiPhotosEnumerateAssets");
        if (errorMessageOut) {
            *errorMessageOut = DuplicatiPhotosCopyError("invalid arguments");
        }
        return -1;
    }

    *assetsOut = NULL;
    *countOut = 0;
    if (errorMessageOut) {
        *errorMessageOut = NULL;
    }

    @autoreleasepool {
        PHAuthorizationStatus status = [PHPhotoLibrary authorizationStatus];
        DLog(@"DuplicatiPhotos: Authorization status: %ld", (long)status);
        if (status == PHAuthorizationStatusDenied || status == PHAuthorizationStatusRestricted) {
            if (errorMessageOut) {
                *errorMessageOut = DuplicatiPhotosCopyError("Photos access denied or restricted");
            }
            return -1;
        } else if (status == PHAuthorizationStatusNotDetermined) {
            DLog(@"DuplicatiPhotos: Requesting authorization");
            // Request authorization synchronously (blocking)
            __block PHAuthorizationStatus newStatus = PHAuthorizationStatusNotDetermined;
            dispatch_semaphore_t semaphore = dispatch_semaphore_create(0);
            [PHPhotoLibrary requestAuthorizationForAccessLevel:PHAccessLevelReadWrite handler:^(PHAuthorizationStatus authStatus) {
                newStatus = authStatus;
                dispatch_semaphore_signal(semaphore);
            }];
            dispatch_semaphore_wait(semaphore, DISPATCH_TIME_FOREVER);
            DLog(@"DuplicatiPhotos: Authorization result: %ld", (long)newStatus);
            if (newStatus != PHAuthorizationStatusAuthorized) {
                if (errorMessageOut) {
                    *errorMessageOut = DuplicatiPhotosCopyError("Photos access not granted, go to System Settings to allow access");
                }
                return -1;
            }
        }

        PHFetchOptions *options = [[PHFetchOptions alloc] init];
        PHFetchResult<PHAsset *> *assets = [PHAsset fetchAssetsWithOptions:options];
        DLog(@"DuplicatiPhotos: Fetched %lu assets", (unsigned long)assets.count);
        if (assets.count == 0) {
            return 0;
        }

        NSMutableArray<NSDictionary *> *results = [NSMutableArray arrayWithCapacity:assets.count];
        for (NSUInteger idx = 0; idx < assets.count; idx++) {
            PHAsset *asset = [assets objectAtIndex:idx];
            PHAssetResource *resource = DuplicatiPhotosSelectResource(asset);
            if (!resource) {
                DLog(@"DuplicatiPhotos: Asset %lu has no valid resource", (unsigned long)idx);
                continue;
            }

            NSString *identifier = asset.localIdentifier ?: @"";
            NSString *filename = resource.originalFilename ?: @"";
            NSString *uti = resource.uniformTypeIdentifier;
            NSNumber *sizeValue = [resource valueForKey:@"fileSize"];
            NSNumber *creation = asset.creationDate ? @([asset.creationDate timeIntervalSince1970]) : nil;
            NSNumber *modification = asset.modificationDate ? @([asset.modificationDate timeIntervalSince1970]) : nil;

            NSDictionary *entry = @{ @"identifier": identifier,
                                     @"filename": filename,
                                     @"uti": uti ?: (id)[NSNull null],
                                     @"size": sizeValue ?: (id)[NSNull null],
                                     @"mediaType": @(asset.mediaType),
                                     @"pixelWidth": @(asset.pixelWidth),
                                     @"pixelHeight": @(asset.pixelHeight),
                                     @"creation": creation ?: (id)[NSNull null],
                                     @"modification": modification ?: (id)[NSNull null] };
            [results addObject:entry];
        }

        DLog(@"DuplicatiPhotos: After filtering, %lu assets with resources", (unsigned long)results.count);
        if (results.count == 0) {
            return 0;
        }

        DLog(@"DuplicatiPhotos: Allocating buffer for %lu assets, size per asset: %zu", (unsigned long)results.count, sizeof(DuplicatiPhotosAssetMetadata));
        DuplicatiPhotosAssetMetadata *buffer = calloc(results.count, sizeof(DuplicatiPhotosAssetMetadata));
        if (!buffer) {
            DLog(@"DuplicatiPhotos: Failed to allocate buffer");
            if (errorMessageOut) {
                *errorMessageOut = DuplicatiPhotosCopyError("out of memory");
            }
            return -1;
        }
        DLog(@"DuplicatiPhotos: Buffer allocated successfully at %p", buffer);

        for (NSUInteger idx = 0; idx < results.count; idx++) {
            DLog(@"DuplicatiPhotos: Processing asset %lu of %lu", (unsigned long)idx, (unsigned long)results.count);
            NSDictionary *entry = results[idx];
            
            DLog(@"DuplicatiPhotos: Copying identifier for asset %lu", (unsigned long)idx);
            buffer[idx].identifier = DuplicatiPhotosCopyCString(entry[@"identifier"]);
            
            DLog(@"DuplicatiPhotos: Copying filename for asset %lu", (unsigned long)idx);
            buffer[idx].filename = DuplicatiPhotosCopyCString(entry[@"filename"]);
            
            DLog(@"DuplicatiPhotos: Processing UTI for asset %lu", (unsigned long)idx);
            id utiValue = entry[@"uti"];
            buffer[idx].uti = (utiValue && utiValue != [NSNull null]) ? DuplicatiPhotosCopyCString(utiValue) : NULL;
            
            DLog(@"DuplicatiPhotos: Processing size for asset %lu", (unsigned long)idx);
            id sizeValue = entry[@"size"];
            buffer[idx].size = (sizeValue && sizeValue != [NSNull null]) ? [sizeValue longLongValue] : -1;
            
            DLog(@"DuplicatiPhotos: Processing mediaType for asset %lu", (unsigned long)idx);
            buffer[idx].mediaType = [entry[@"mediaType"] intValue];
            
            DLog(@"DuplicatiPhotos: Processing dimensions for asset %lu", (unsigned long)idx);
            buffer[idx].pixelWidth = [entry[@"pixelWidth"] intValue];
            buffer[idx].pixelHeight = [entry[@"pixelHeight"] intValue];
            
            DLog(@"DuplicatiPhotos: Processing creation date for asset %lu", (unsigned long)idx);
            id creation = entry[@"creation"];
            buffer[idx].creationDateSeconds = (creation && creation != [NSNull null]) ? [creation doubleValue] : NAN;
            
            DLog(@"DuplicatiPhotos: Processing modification date for asset %lu", (unsigned long)idx);
            id modification = entry[@"modification"];
            buffer[idx].modificationDateSeconds = (modification && modification != [NSNull null]) ? [modification doubleValue] : NAN;
            
            DLog(@"DuplicatiPhotos: Completed processing asset %lu", (unsigned long)idx);
        }

        DLog(@"DuplicatiPhotos: All assets processed, setting output parameters");
        *assetsOut = buffer;
        *countOut = results.count;
        DLog(@"DuplicatiPhotos: Returning success with %lu assets", (unsigned long)results.count);
        return 0;
    }
}

void DuplicatiPhotosFreeAssets(DuplicatiPhotosAssetMetadata *assets, size_t count) {
    DLog(@"DuplicatiPhotos: DuplicatiPhotosFreeAssets called with count: %zu", count);
    if (!assets) {
        DLog(@"DuplicatiPhotos: DuplicatiPhotosFreeAssets called with NULL assets");
        return;
    }

    for (size_t idx = 0; idx < count; idx++) {
        if (assets[idx].identifier) {
            free(assets[idx].identifier);
        }
        if (assets[idx].filename) {
            free(assets[idx].filename);
        }
        if (assets[idx].uti) {
            free(assets[idx].uti);
        }
    }

    free(assets);
}

@interface DuplicatiPhotosReader : NSObject
@property (nonatomic, strong) NSMutableData *buffer;
@property (nonatomic, strong) NSCondition *condition;
@property (nonatomic, assign) BOOL completed;
@property (nonatomic, assign) BOOL cancelled;
@property (nonatomic, strong) NSError *error;
@property (nonatomic, assign) PHAssetResourceDataRequestID requestId;
@property (nonatomic, strong) PHAssetResource *resource;
@end

@implementation DuplicatiPhotosReader

- (instancetype)initWithResource:(PHAssetResource *)resource {
    DLog(@"DuplicatiPhotos: DuplicatiPhotosReader initWithResource called for: %@", resource.originalFilename);
    self = [super init];
    if (self) {
        _resource = resource;
        _buffer = [NSMutableData data];
        _condition = [[NSCondition alloc] init];
        _completed = NO;
        _cancelled = NO;
        _requestId = PHInvalidAssetResourceDataRequestID;

        PHAssetResourceRequestOptions *options = [[PHAssetResourceRequestOptions alloc] init];
        options.networkAccessAllowed = YES;

        DLog(@"DuplicatiPhotos: Starting data request for resource: %@", resource.originalFilename);
        __weak typeof(self) weakSelf = self;
        PHAssetResourceManager *manager = [PHAssetResourceManager defaultManager];
        _requestId = [manager requestDataForAssetResource:resource
                                                 options:options
                                     dataReceivedHandler:^(NSData * _Nonnull data) {
            @autoreleasepool {
                DLog(@"DuplicatiPhotos: Data received handler called on thread %@", [NSThread currentThread]);
                __strong typeof(weakSelf) strongSelf = weakSelf;
                if (!strongSelf || !data || strongSelf.cancelled) {
                    DLog(@"DuplicatiPhotos: Data received handler skipped (cancelled or invalid)");
                    return;
                }

                DLog(@"DuplicatiPhotos: Received data chunk of size: %lu", (unsigned long)data.length);
                [strongSelf.condition lock];
                DLog(@"DuplicatiPhotos: Locked condition, current buffer size: %lu, appending data", (unsigned long)strongSelf.buffer.length);
                [strongSelf.buffer appendData:data];
                DLog(@"DuplicatiPhotos: Data appended, new buffer size: %lu", (unsigned long)strongSelf.buffer.length);
                [strongSelf.condition signal];
                DLog(@"DuplicatiPhotos: Signaled condition, unlocking");
                [strongSelf.condition unlock];
                DLog(@"DuplicatiPhotos: Data received handler completed");
            }
        }
        completionHandler:^(NSError * _Nullable error) {
            @autoreleasepool {
                __strong typeof(weakSelf) strongSelf = weakSelf;
                if (!strongSelf)
                    return;

                if (error) {
                    DLog(@"DuplicatiPhotos: Completion handler called with error: %@", error.localizedDescription);
                } else {
                    DLog(@"DuplicatiPhotos: Completion handler called successfully");
                }
                [strongSelf.condition lock];
                strongSelf.completed = YES;
                strongSelf.error = error;
                [strongSelf.condition broadcast];
                [strongSelf.condition unlock];
            }
        }];
    }
    return self;
}

- (void)dealloc {
    DLog(@"DuplicatiPhotos: DuplicatiPhotosReader dealloc called");
    [self close];
}

- (void)close {
    DLog(@"DuplicatiPhotos: DuplicatiPhotosReader close called");
    if (self.cancelled) {
        DLog(@"DuplicatiPhotos: Already cancelled, skipping close");
        return;
    }

    self.cancelled = YES;
    if (self.requestId != PHInvalidAssetResourceDataRequestID)
    {
        PHAssetResourceManager *manager = [PHAssetResourceManager defaultManager];
        [manager cancelDataRequest:self.requestId];
    }

    [self.condition lock];
    self.completed = YES;
    [self.condition broadcast];
    [self.condition unlock];
}

- (ssize_t)readInto:(uint8_t *)destination length:(size_t)length error:(char **)errorMessageOut {
    DLog(@"DuplicatiPhotos: readInto called with length: %zu", length);
    if (!destination) {
        DLog(@"DuplicatiPhotos: Invalid destination buffer");
        if (errorMessageOut) {
            *errorMessageOut = DuplicatiPhotosCopyError("invalid buffer");
        }
        return -1;
    }

    if (length == 0) {
        DLog(@"DuplicatiPhotos: Zero length read requested");
        return 0;
    }

    DLog(@"DuplicatiPhotos: Locking condition for read on thread %@", [NSThread currentThread]);
    [self.condition lock];
    DLog(@"DuplicatiPhotos: Condition locked, buffer length: %lu, completed: %d", (unsigned long)self.buffer.length, self.completed);
    while (self.buffer.length == 0 && !self.completed) {
        DLog(@"DuplicatiPhotos: Waiting for data...");
        [self.condition wait];
        DLog(@"DuplicatiPhotos: Woke up from wait, buffer length: %lu, completed: %d", (unsigned long)self.buffer.length, self.completed);
    }

    if (self.buffer.length == 0) {
        DLog(@"DuplicatiPhotos: Buffer is empty after wait");
        NSError *error = self.error;
        [self.condition unlock];

        if (error) {
            DLog(@"DuplicatiPhotos: Returning error: %@", error.localizedDescription);
            if (errorMessageOut) {
                *errorMessageOut = DuplicatiPhotosCopyErrorFromString(error.localizedDescription ?: @"read error");
            }
            return -1;
        }

        DLog(@"DuplicatiPhotos: No error, returning EOF");
        return 0;
    }

    size_t toCopy = MIN((size_t)self.buffer.length, length);
    DLog(@"DuplicatiPhotos: Copying %zu bytes to destination from buffer of size %lu", toCopy, (unsigned long)self.buffer.length);
    memcpy(destination, self.buffer.bytes, toCopy);
    DLog(@"DuplicatiPhotos: memcpy completed, removing copied bytes from buffer");
    [self.buffer replaceBytesInRange:NSMakeRange(0, toCopy) withBytes:NULL length:0];
    DLog(@"DuplicatiPhotos: Buffer updated, new size: %lu, unlocking", (unsigned long)self.buffer.length);
    [self.condition unlock];
    DLog(@"DuplicatiPhotos: Returning %zd bytes", (ssize_t)toCopy);
    return (ssize_t)toCopy;
}

@end

int DuplicatiPhotosOpenAsset(const char *identifier, void **handleOut, char **errorMessageOut) {
    DLog(@"DuplicatiPhotos: DuplicatiPhotosOpenAsset called with identifier: %s", identifier ?: "(null)");
    if (!identifier || !handleOut) {
        DLog(@"DuplicatiPhotos: Invalid arguments to DuplicatiPhotosOpenAsset");
        if (errorMessageOut) {
            *errorMessageOut = DuplicatiPhotosCopyError("invalid arguments");
        }
        return -1;
    }

    *handleOut = NULL;
    if (errorMessageOut) {
        *errorMessageOut = NULL;
    }

    @autoreleasepool {
        NSString *identifierString = [NSString stringWithUTF8String:identifier];
        if (!identifierString) {
            if (errorMessageOut) {
                *errorMessageOut = DuplicatiPhotosCopyError("invalid identifier");
            }
            return -1;
        }

        PHFetchResult<PHAsset *> *fetchResult = [PHAsset fetchAssetsWithLocalIdentifiers:@[identifierString] options:nil];
        if (fetchResult.count == 0) {
            if (errorMessageOut) {
                *errorMessageOut = DuplicatiPhotosCopyError("asset not found");
            }
            return -1;
        }

        PHAsset *asset = [fetchResult objectAtIndex:0];
        PHAssetResource *resource = DuplicatiPhotosSelectResource(asset);
        if (!resource) {
            if (errorMessageOut) {
                *errorMessageOut = DuplicatiPhotosCopyError("asset resource unavailable");
            }
            return -1;
        }

        DuplicatiPhotosReader *reader = [[DuplicatiPhotosReader alloc] initWithResource:resource];
        if (!reader) {
            if (errorMessageOut) {
                *errorMessageOut = DuplicatiPhotosCopyError("unable to create reader");
            }
            return -1;
        }

        *handleOut = (void *)CFBridgingRetain(reader);
        return 0;
    }
}

ssize_t DuplicatiPhotosReadAsset(void *handle, uint8_t *buffer, size_t bufferLength, char **errorMessageOut) {
    DLog(@"DuplicatiPhotos: DuplicatiPhotosReadAsset called with bufferLength: %zu", bufferLength);
    if (!handle) {
        DLog(@"DuplicatiPhotos: Invalid handle to DuplicatiPhotosReadAsset");
        if (errorMessageOut) {
            *errorMessageOut = DuplicatiPhotosCopyError("invalid handle");
        }
        return -1;
    }

    DuplicatiPhotosReader *reader = (__bridge DuplicatiPhotosReader *)handle;
    ssize_t result = [reader readInto:buffer length:bufferLength error:errorMessageOut];
    DLog(@"DuplicatiPhotos: DuplicatiPhotosReadAsset returning: %zd", result);
    return result;
}

int DuplicatiPhotosGetAssetSize(void *handle, int64_t *sizeOut, char **errorMessageOut) {
    DLog(@"DuplicatiPhotos: DuplicatiPhotosGetAssetSize called");
    if (!handle || !sizeOut) {
        DLog(@"DuplicatiPhotos: Invalid arguments to DuplicatiPhotosGetAssetSize");
        if (errorMessageOut) {
            *errorMessageOut = DuplicatiPhotosCopyError("invalid arguments");
        }
        return -1;
    }

    if (errorMessageOut) {
        *errorMessageOut = NULL;
    }

    @autoreleasepool {
        DuplicatiPhotosReader *reader = (__bridge DuplicatiPhotosReader *)handle;
        PHAssetResource *resource = reader.resource;
        
        if (!resource) {
            if (errorMessageOut) {
                *errorMessageOut = DuplicatiPhotosCopyError("resource not available");
            }
            return -1;
        }

        NSNumber *sizeValue = [resource valueForKey:@"fileSize"];
        if (!sizeValue) {
            if (errorMessageOut) {
                *errorMessageOut = DuplicatiPhotosCopyError("size not available");
            }
            return -1;
        }

        *sizeOut = [sizeValue longLongValue];
        DLog(@"DuplicatiPhotos: DuplicatiPhotosGetAssetSize returning size: %lld", *sizeOut);
        return 0;
    }
}

void DuplicatiPhotosCloseAsset(void *handle) {
    DLog(@"DuplicatiPhotos: DuplicatiPhotosCloseAsset called");
    if (!handle) {
        DLog(@"DuplicatiPhotos: DuplicatiPhotosCloseAsset called with NULL handle");
        return;
    }

    DuplicatiPhotosReader *reader = (__bridge_transfer DuplicatiPhotosReader *)handle;
    [reader close];
}

void DuplicatiPhotosFreeString(char *value) {
    DLog(@"DuplicatiPhotos: DuplicatiPhotosFreeString called");
    if (value) {
        free(value);
    }
}
