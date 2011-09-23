To remove the heavy load on SVN with the many translated files,
the system is now changed to rely on a single CSV file pr. translation.

To work with it, use the CSV file, either here on from google docs:
https://docs.google.com/leaf?id=0B9tKh3OEoqEqOTZmNWI1NWEtOTZkNi00OTk4LTkzMWEtOGI1M2UzMTc3NWYy

To generate the structure needed to build the translation, run:
LocalizationTool.exe import fr-FR report.fr-FR.csv

This will generate the fr-FR folder, in which you can
perform a UI customization (move buttons etc).

Once done, export the changes back to the CSV file:
LocalizationTool.exe export fr-FR

To build the assemblies run:
LocalizationTool.exe build