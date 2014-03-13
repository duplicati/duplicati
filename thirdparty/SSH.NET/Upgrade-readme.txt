The file Renci.SshNet has been signed with Signer.exe to give it a strong name.
If you want to upgrade Renci.SshNet to a new version, please run:

Signer.exe -k Duplicati.snk -a Renci.SshNet -outdir out

Unfortunately, this gives errors with the "dynamic" keyword,
so you must use the .Net 3.5 version.