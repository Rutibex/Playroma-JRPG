# plugins.js Lossless Document

`plugins.js` is JavaScript source containing a JSON-compatible `$plugins` array.
Playroma locates the array without evaluating the file and stores three regions:

1. prefix bytes before the opening bracket;
2. the lossless mutable JSON array;
3. suffix bytes after the closing bracket.

This preserves comments, assignment style, whitespace, plugin order, duplicate
entries, disabled entries, descriptions, and parameter strings. Editing one
parameter changes only its string token inside the array.
