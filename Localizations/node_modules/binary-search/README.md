binary-search
=============

This is a really tiny, stupid, simple binary search library for Node.JS. We
wrote it because existing solutions were bloated and incorrect.

This version is a straight port of the Java version mentioned by Joshua Bloch
in his article, [Nearly All Binary Searches and Merge Sorts are Broken](http://googleresearch.blogspot.com/2006/06/extra-extra-read-all-about-it-nearly.html).

Thanks to [Conrad Irwin](https://github.com/ConradIrwin) and [Michael
Marino](https://github.com/mgmarino) for, ironically, pointing out bugs.

Example
-------

```js
var bs = require("binary-search");

bs([1, 2, 3, 4], 3, function(element, needle) { return element - needle; });
// => 2

bs([1, 2, 4, 5], 3, function(element, needle) { return element - needle; });
// => -3
```

Be advised that passing in a comparator function is *required*. Since you're
probably using one for your sort function anyway, this isn't a big deal.

The comparator takes a 1st and 2nd argument of element and needle, respectively.

The comparator also takes a 3rd and 4th argument, the current index and array,
respectively. You shouldn't normally need the index or array to compare values,
but it's there if you do.

You may also, optionally, specify an input range as the final two parameters,
in case you want to limit the search to a particular range of inputs. However,
be advised that this is generally a bad idea (but sometimes bad ideas are
necessary).

License
-------

To the extent possible by law, The Dark Sky Company, LLC has [waived all
copyright and related or neighboring rights][cc0] to this library.

[cc0]: http://creativecommons.org/publicdomain/zero/1.0/
