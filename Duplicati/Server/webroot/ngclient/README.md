The current Duplicati UI is called "ngclient" and is managed in the repo:
https://github.com/duplicati/ngclient

If the Duplicati solution is running in Debug mode, the ngclient package will be downloaded in the version stated in package.json, unpacked into a temporary directory, and served there so no setup is needed for Debug runs.

For production deployments, the ngclient package will be installed via npm and bundled into the webroot.
