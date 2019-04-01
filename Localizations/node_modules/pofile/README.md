# pofile - gettext .po parsing for JavaScript

> Parse and serialize Gettext PO files.

[![Build Status](https://travis-ci.org/rubenv/pofile.png?branch=master)](https://travis-ci.org/rubenv/pofile)

## Usage
Add pofile to your project:

### Installation (Node.JS, browser via Browserified)
```
npm install --save pofile
```

Reference it in your code:

```js
var PO = require('pofile');
```

### Installation (via bower)
```
bower install --save pofile
```

Add it to your HTML file:

```html
<script src="bower_components/pofile/dist/pofile.js"></script>
```

Reference it in your code:

```js
var PO = require('pofile');
```

### Loading and parsing

You can create a new empty PO file by using the class:

```js
var po = new PO();
```

Or by loading a file (Node.JS only):

```js
PO.load('text.po', function (err, po) {
    // Handle err if needed
    // Do things with po
});
```

Or by parsing a string:

```js
var po = PO.parse(myString);
```

### The PO class

The `PO` class exposes three members:

* `comments`: An array of comments (found at the header of the file).
* `headers`: A dictionary of the headers.
* `items`: An array of `PO.Item` objects, each of which represents a string
  from the gettext catalog.

There are two methods available:

* `save`: Accepts a filename and callback, writes the po file to disk.

```js
po.save('out.po', function (err) {
    // Handle err if needed
});
```

* `toString`: Serializes the po file to a string.

### The PO.Item class

The `PO.Item` class exposes the following members:

* `msgid`: The message id.
* `msgid_plural`: The plural message id (null if absent).
* `msgstr`: An array of translated strings. Items that have no plural msgid
  only have one element in this array.
* `references`: An array of reference strings.
* `comments`: An array of string translator comments.
* `extractedComments`: An array of string extracted comments.
* `flags`: A dictionary of the string flags. Each flag is mapped to a key with
  value true. For instance, a string with the fuzzy flag set will have
  `item.flags.fuzzy == true`.
* `msgctxt`: Context of the message, an arbitrary string, can be used for disambiguation.


## Contributing

In lieu of a formal styleguide, take care to maintain the existing coding
style. Add unit tests for any new or changed functionality. Lint and test your
code using [Grunt](http://gruntjs.com/).

## Credits

Originally based on node-po (written by Michael Holly). Rebranded because
node-po is unmaintained and because this library is no longer limited to
Node.JS: it works in the browser too.

### Changes compared to node-po

* Proper handling of async methods that won't crash your Node.JS process when
  something goes wrong.
* Support for parsing string flags (e.g. fuzzy).
* A test suite.
* Browser support (through Browserified and bower).

### Migrating from node-po

You'll need to update the module reference: `require('pofile')` instead of
`require('node-po')`.

At the initial release, node-po and pofile have identical APIs, with one small
exception: the `save` and `load` methods now take a callback that has an `err`
parameter: `(err)` for `save` and `(err, po)` for `load`. This is similar to
Node.JS conventions.

Change code such as:

```js
PO.load('text.po', function (po) {
```

To:

```js
PO.load('text.po', function (err, po) {
    // Handle err if needed
```

## License 

    (The MIT License)

    Copyright (C) 2013-2017 by Ruben Vermeersch <ruben@rocketeer.be>
    Copyright (C) 2012 by Michael Holly

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in
    all copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
    THE SOFTWARE.
