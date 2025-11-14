# Environment variables for tests

On Github actions these are mapped 1:1 to secrets, even the non password fields are stored in secrets.

## General

These control the size and number of files generated.

```
MAX_FILE_SIZE    default is 1000kb
NUMBER_OF_FILES  default is 20
```

## Backends CI Status

| Backend        | CI Planned       | CI Status |
| -------------- | ---------------- | --------- |
| pCloud         | Planned          | Live      |
| WebDav         | Planned          | Live      |
| Shhv2          | Planned          | Live      |
| S3             | Planned          | Live      |
| Ftp            | Planned          | Live      |
| Planned        | Planned          | Live      |
| Dropbox        | Planned          | Live      |
| AlternativeFTP | Planned          | Live      |
| AzureBlob      | Planned          | Live      |
| CIFS           | Planned          | Live      |
| Google Drive   | Planned          | Live      |
| Box            | Planned          | Live      |
| FileJump       | Planed           | Live      |
| OneDrive       | Planned          | Live      |
| IDrive2        | Planned          | Live      |
| backBlaze      | Planned          | Live      |
| Filen          | Planned          | Live      |
| Jottacloud     | Pannned          | Live      |
| GCS            | Planned          | Live      |
| OpenStack      | Planned          | Live      |
| File           | Planned          |           |
| TahoeLAFS      | Planned          |           |
| TescentCOS     | Pending Decision |           |
| Storj          | Pending Decision |           |
| SharePoint     | Pending Decision |           |
| Rclone         | Pending Decision |           |
| Mega           | Deprecated       |           |
| AliyunOSS      | Pending Decision |           |

## Backends that do not require Environment variables

- FTP _(TestContainers required)_
- SSH _(TestContainers required)_
- Webdav _(TestContainers required)_
- CIFS _(TestContainers required)_

Please note that TestContainers token has to be configured in secrets/Github actions yml.

## Backends that require Environment variables


# CloudStack

```
TESTCREDENTIAL_CLOUDSTACK_USERNAME
TESTCREDENTIAL_CLOUDSTACK_PASSWORD
TESTCREDENTIAL_CLOUDSTACK_LOCATION
TESTCREDENTIAL_CLOUDSTACK_TENANT
TESTCREDENTIAL_CLOUDSTACK_DOMAIN
TESTCREDENTIAL_CLOUDSTACK_REGION
TESTCREDENTIAL_CLOUDSTACK_FOLDER
```

# JottaCloud

```
TESTCREDENTIAL_JOTTACLOUD_AUTHID
TESTCREDENTIAL_JOTTACLOUD_FOLDER
```

# iDrivee2

```
TESTCREDENTIAL_IDRIVEE2_BUCKET
TESTCREDENTIAL_IDRIVEE2_ACCESS_KEY
TESTCREDENTIAL_IDRIVEE2_SECRET_KEY
TESTCREDENTIAL_IDRIVEE2_FOLDER
```

# Filejump

```
TESTCREDENTIAL_FILEJUMP_FOLDER
TESTCREDENTIAL_FILEJUMP_TOKEN
```

## FileN

```
TESTCREDENTIAL_FILEN_FOLDER
TESTCREDENTIAL_FILEN_USERNAME
TESTCREDENTIAL_FILEN_PASSWORD
```

## Box.com

```
TESTCREDENTIAL_BOX_FOLDER
TESTCREDENTIAL_BOX_AUTHID
```

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

## Google Cloud Services

```
TESTCREDENTIAL_GCS_BUCKET
TESTCREDENTIAL_GCS_FOLDER
TESTCREDENTIAL_GCS_TOKEN
```

## Running the tests

[TestContainers](https://testcontainers.org/) is a pre-requisite for SSH, FTP and Webdav tests. It is not required for the other tests.

Set the environment variables as described above, then run the tests using the following commands:

Minimal Verbosity:

`dotnet test Duplicati.Backend.Tests.slnx --logger:"console;verbosity=normal"`

Running with full verbosity (useful if tests are failing):

`dotnet test Duplicati.Backend.Tests.slnx --logger:"console;verbosity=detailed"`

Running specific tests:

`dotnet test Duplicati.Backend.Tests.slnx --logger:"console;verbosity=detailed" --filter="Name=TestDropBox"`
