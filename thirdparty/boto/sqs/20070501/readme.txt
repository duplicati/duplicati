The main SQS implementation now uses the 2008-01-01 API verson.  To use the older API version
(2007-05-01) you need to edit your /etc/boto.cfg or ~/.boto file to add the following line:

boto.sqs_extend = 20070501

This will allow the code in the boto.sqs.20070501 module to override the code in boto.sqs.
