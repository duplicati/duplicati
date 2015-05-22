Google Cloud Storage backend for Duplicati2
===========================================

This backend connects directly to the <a href="https://cloud.google.com/storage/">Storage</a> product on <a href="https://cloud.google.com/">Google Cloud</a>.

In order to use it, you need:

* A Google Cloud project
* At least one Bucket in Cloud Storage

For all these you can go to the <a href="https://consile.developer.google.com/">Google Developers Console</a>

Important notes
---------------

1. This backend requires a Service Account to be created in your project. After creating the Service Account, download a .p12 file for that account and place it in the Duplicati program folder.
2. This backend is not able to create buckets, any buckets used must be created in advance.

