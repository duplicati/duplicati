mkdir ./example/dist

# Extract test
npm run example-extract

cmp example/extract-reference.pot example/dist/extract.pot && echo 'Extract test passed' || exit 123

# Compile test
npm run example-compile

cmp example/compile-reference.js example/dist/compiled.js && echo 'Compile test passed' || exit 123