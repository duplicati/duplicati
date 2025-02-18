
# Environment variables for tests

On Github actions these are mapped 1:1 to secrets, even the non password fields are stored in secrets.

## General

These control the size and number of files generated.

```
MAX_FILE_SIZE    default is 1000kb
NUMBER_OF_FILES  default is 20
```

## Backends CI Status

| Backend | CI Planned | CI Status |
|----------|------------|-----------|
| pCloud | Planned | Live      |
| WebDav | Planned | Live      |
| Shhv2 | Planned | Live      |
| S3 | Planned | Live      |
| Ftp | Planned | Live      |
| Planned | Planned | Live      |
| Dropbox | Planned | Live      |
| AlternativeFTP | Planned | Live      |
| AzureBlob | Planned | Live      |
| CIFS | Planned | Live      |
| Google Drive | Planned | Live      |
| TahoeLAFS | Pending Decision |           |
| TescentCOS | Pending Decision |           |
| Storj | Pending Decision |           |
| Sia | Pending Decision |           |
| SharePoint | Pending Decision |           |
| Rclone | Pending Decision |           |
| OneDrive | Pending Decision |           |
| OpenStack | Pending Decision |           |
| Mega | Pending Decision |           |
| Jottacloud | Pending Decision |           |
| IDrive2 | Planned |           |
| File | Planned |           |
| Box | Pending Decision |           |
| CloudFiles | Pending Decision |           |
| backBlaze | Planned | Live      |
| AliyunOSS | Pending Decision |           |


## Backends that do not require Environment variables

* FTP _(TestContainers required)_
* SSH _(TestContainers required)_
* Webdav _(TestContainers required)_
* CIFS _(TestContainers required)_

Please note that TestContainers token has to be configured in secrets/Github actions yml.

## Backends that require Environment variables

## Backblaze B2

Backblaze B2 credentials are mapped to the following environment variables:

```
TESTCREDENTIAL_B2_BUCKET
TESTCREDENTIAL_B2_FOLDER
TESTCREDENTIAL_B2_USERNAME
TESTCREDENTIAL_B2_PASSWORD
```

## Google Drive:

Google Drive credentials are mapped to the following environment variables:

```
TESTCREDENTIAL_GOOGLEDRIVE_FOLDER
TESTCREDENTIAL_GOOGLEDRIVE_TOKEN
```

## S3

S3 credentials are mapped to the following environment variables:

Attention: **AWS TESTCREDENTIAL_S3_SECRET is URI escaped automatically, supply the raw value.**

```
TESTCREDENTIAL_S3_KEY
TESTCREDENTIAL_S3_SECRET
TESTCREDENTIAL_S3_BUCKETNAME
TESTCREDENTIAL_S3_REGION
```

## Dropbox

Dropbox credentials are mapped to the following environment variables:

```
TESTCREDENTIAL_DROPBOX_FOLDER
TESTCREDENTIAL_DROPBOX_TOKEN
```

## Azure Blob

Attention: **TESTCREDENTIAL_AZURE_ACCESSKEY is URI escaped automatically, supply the raw value.**


```
TESTCREDENTIAL_AZURE_ACCOUNTNAME
TESTCREDENTIAL_AZURE_ACCESSKEY
TESTCREDENTIAL_AZURE_CONTAINERNAME
```

## pCloud Native API

```
TESTCREDENTIAL_PCLOUD_SERVER
TESTCREDENTIAL_PCLOUD_TOKEN
TESTCREDENTIAL_PCLOUD_FOLDER
```
For PCloud the server is the API server(eapi.pcloud.com for EU hosted account or api.pcloud.com for non EU). The token is the OAuth token.

## Running the tests

[TestContainers](https://testcontainers.org/) is a pre-requisite for SSH, FTP and Webdav tests. It is not required for the other tests.

Set the environment variables as described above, then run the tests using the following commands:

Minimal Verbosity:

`dotnet test Duplicati.Backend.Tests.sln --logger:"console;verbosity=normal"`

Running with full verbosity (useful if tests are failing):

`dotnet test Duplicati.Backend.Tests.sln --logger:"console;verbosity=detailed"`

Running specific tests:

`dotnet test Duplicati.Backend.Tests.sln --logger:"console;verbosity=detailed" --filter="Name=TestDropBox"`


