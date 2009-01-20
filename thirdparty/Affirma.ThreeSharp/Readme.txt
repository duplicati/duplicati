**********************************************
ThreeSharp - C# Library and Code for Amazon S3
Release 2.0
Affirma Consulting
jwetzel@affirmaconsulting.com
**********************************************

-------------------
Project Description
-------------------

An advanced C# library for interfacing with the Amazon S3 and CloudFront systems. Among its powerful features are:
- Full support for data streaming. No need to load data into memory before sending to S3.
- Data encryption.
- Thread safety and live statistics. Perform multiple simultaneous uploads and downloads and show
     progress in real-time.
- A powerful, unified object model that simplifies maintenance and extensions.
- Support for S3's new EU buckets.

The solution contains four projects:
- ThreeSharp. The C# library.
- ThreeSharp.Wrapper. A helper that wraps basic common procedures into single-line calls.
- ThreeSharp.ConsoleSample. A console application that demonstrates and describes the
     various procedures available in the ThreeSharp Library and the Wrapper.
- ThreeSharp.FormSample. A Windows Forms app that demonstrates the use of the Library
     in a multi-threaded, graphical environment.

------------------------
Release 1.1 New Features
------------------------

- Support for EU buckets, along with examples.  The trick with EU buckets is that they require the use
  of the subdomain calling format.  And when you first create an EU bucket, it takes Amazon a few
  minutes to create the DNS entries to support the subdomain calling format, so in that time period, they
  return an HTTP 307-TemporaryRedirect, which must be handled.  This is now demonstrated in the Simple
  Uploader forms app, included with ThreeSharp.
- Headers are now a property of the Transfer object, allowing access to the headers that Amazon responds
  with.  This allows you to make an HTTP HEAD request, which is also demonstrated in the Simple Uploader.
- Fixed a bug in the URI generator for the subdomain calling format.
- The ThreeSharpConfig now has a property to set the ConnectionLimit - the number of concurrent connections
  it will allow to S3.

------------------------
Release 1.2 New Features
------------------------

- Fixed a cross-threading GUI issue.
- Fixed an issue with byte padding of encrypted streams.

------------------------
Release 1.3 New Features
------------------------

- The Transfer object now implements IDisposable, to help with releasing unmanaged resources.  Clients should use "using" clauses with all requests and responses.

------------------------
Release 1.4 New Features
------------------------

- Now supports the new object copy functionality in Amazon S3.
- Fixed a regular expression bug in the ThrowIfErrors method of the ThreeSharpQuery class.
- Fixed a problem with incorrect ordering of metadata headers containing dashes.
- Request and Response now use UTF8 encoding for strings, instead of ASCII encoding.
- Added overloads to Request.LoadStreamWithString and Request.LoadStreamWithBytes to allow content type to be set manually.

------------------------
Release 1.5 New Features
------------------------

- Fixed an issue with byte padding of encrypted streams.
- Now easier to use alternate encryption methods.

------------------------
Release 2.0 New Features
------------------------

- Added support to create and manage CloudFront distributions through HTTPS requests.  
- Added several business objects to support CloudFront communication.
- Separated Request and Response objects by type.

------------------------
Release 2.1 New Features
------------------------

- Added ACLChange request and response objects.

-----------------------
ThreeSharp Object Model
-----------------------

ThreeSharp interacts with Amazon S3 through REST requests.  Both requests and responses are streamed.

To model this, ThreeSharp provides an object for each type of request or response, and a query object
which works with these request and response objects.

This is best illustrated with an example.  The first thing we want to do is set up a config object
and pass it to our query object.

	ThreeSharpConfig config = new ThreeSharpConfig();
	config.AwsAccessKeyID = awsAccessKeyId;
	config.AwsSecretAccessKey = awsSecretAccessKey;

	IThreeSharp service = new ThreeSharpQuery(config);

Notice that the ThreeSharpQuery class is fulfilling the IThreeSharp interface contract.  If you
were developing an application that used the ThreeSharp library, but didn't want to actually
talk to Amazon S3 during development, you could build a mock object that also implemented IThreeSharp.

Now, Suppose we wanted to perform a streamed retrieve of a file called Example.zip, in a bucket called 
TestBucket, from Amazon S3.  First, we would want to instantiate an ObjectGetRequest.  (This 
object will be interpreted into a request to get an object. Pretty simple!)  So we would write:

	using (ObjectGetRequest request = new ObjectGetRequest("TestBucket", "Example.zip")) {

We then pass our request to the query object, which will return a response.

	using (ObjectGetResponse response = service.ObjectGet(objectGetRequest)) {

This response contains a data stream, which we can stream to disk.

	response.StreamResponseToFile("c:\\Example.zip");

One thing to note is that all responses in this model have data streams, and you will have problems
if these streams aren't closed.  For this reason, the Transfer object now implements IDisposable.  This is why requests and responses should be created with "using" clauses.

All the operations supported by ThreeSharp for S3 proceed in the manner outlined here.  Examples of each call
can be found in ConsoleSample app, with explanatory comments.  The console application does not currently support CloudFront.  
We have tried to make the ThreeSharp code readable and self-explanatory, as well as powerful.  
However, the best way to polish a framework is through actual use, so comments and feedback are always welcome.




