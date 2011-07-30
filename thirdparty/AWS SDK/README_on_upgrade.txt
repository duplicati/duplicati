The AWSSDK version found here is patched with the AWSSDK.diff file.
The patch adds support for ReadWriteTimeout to the GET/PUT requests,
which makes it more robust when used in Duplicati where the connection
speeds are highly varying.

Before upgrading the DLL, make sure the patch is applied.

Related forum post:
https://forums.aws.amazon.com/thread.jspa?threadID=72493&tstart=0