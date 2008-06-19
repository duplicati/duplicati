The files and folders here are not owned by Duplicati.
Each folder has a "License.txt" file that describes the license for that item.
Each folder has a "Homepage.txt" file that contains a link to the page where the files were originally downloaded.

Duplicity has been modified slightly for use with Windows.
The most work is providing sensible defaults for missing operations.
The way GnuPGInterface.py interacts with gpg.exe was changed. 
Due to limitations in Windows, the handles/pipes 
cannot be transfered between processes.

Hopefully these changes will make it back into Duplicity.

None of the other packages have been modified, and thus does not contain sourcecode.

