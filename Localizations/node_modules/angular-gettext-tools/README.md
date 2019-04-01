# angular-gettext-tools

> Tools for extracting/compiling angular-gettext strings.

Used to construct build tools for [`angular-gettext`](https://github.com/rubenv/angular-gettext).

[![Build Status](https://travis-ci.org/rubenv/angular-gettext-tools.png?branch=master)](https://travis-ci.org/rubenv/angular-gettext-tools)

Implementations:

* [Grunt plugin](https://github.com/rubenv/grunt-angular-gettext)
* [Gulp plugin](https://github.com/gabegorelick/gulp-angular-gettext)
* [CLI utility](https://github.com/huston007/angular-gettext-cli)
* [Webpack loader (compilation)](https://github.com/princed/angular-gettext-loader)
* [Webpack plugin (compilation and extraction)](https://github.com/augusto-altman/angular-gettext-plugin)

Check the website for usage instructions: [http://angular-gettext.rocketeer.be/](http://angular-gettext.rocketeer.be/).

## Options

All options and defaults are displayed below: 

```JSON
{
    "startDelim": "{{",
    "endDelim": "}}",
    "markerName": "gettext",
    "markerNames": [],
    "moduleName": "gettextCatalog",
    "moduleMethodString": "getString",
    "moduleMethodPlural": "getPlural",
    "attribute": "translate",
    "attributes": [],
    "lineNumbers": true,
    "format": "javascript",
    "defaultLanguage": false,
    "requirejs": false
}
```

## License 

    (The MIT License)

    Copyright (C) 2013-2015 by Ruben Vermeersch <ruben@rocketeer.be>

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
