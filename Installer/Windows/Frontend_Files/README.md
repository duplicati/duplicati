# DUPLICATI SSH CONFIGURATION (with private key)

# Instructions to build

1. Download the files of this repo [here](https://gitlab.com/KanellakisK/Duplicati-V2-0-7-3) or [here](https://github.com/Kanellaman/Duplicati-V2.0.7.3)

2. Change the contents of the `key` and store your private key (copy-paste the private key to file)

3. Zip all the files contained on the folder

4. Fill the credentials needed for the Host server in the file [EditUriBackendConfig.js](webroot\ngax\scripts\services\EditUriBackendConfig.js)

5. Download Duplicati files from [here](https://gitlab.com/KanellakisK/duplicati) or [here](https://github.com/Kanellaman/duplicati)

6. Store the zipped folder (from step 3) in the Duplicati folder downloaded in step 4 to the path ...\Installer\Windows

7. Run
   `$...\duplicati\Installer\Windows:> artifact_win Files.zip`

8. Run the executable created and follow the instructions of the Installer</br>

## Notes

- The key file must be named `key` and of type rsa
