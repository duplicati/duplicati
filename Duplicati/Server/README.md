## Update style sheets

Install [less CSS](https://lesscss.org) and minify plugin. We also use [Stylelint](https://stylelint.io/) with a plugin for LESS ([stylelint-config-standard-less](https://www.npmjs.com/package/stylelint-config-standard-less)).

Run the commands below to install the packages on the project root directory:

```
npm install less -g
npm install -g less-plugin-clean-css
npm install stylelint stylelint-config-standard-less --save-dev --prefix ../../
```

Then, run the commands below to use stylelint and compile the LESS files:

```
npx stylelint "**/less/*.less"
lessc webroot/ngax/less/dark.less styles/dark.css --clean-css -m=always
lessc webroot/ngax/less/default.less styles/default.css --clean-css -m=always
```

Add `--fix` option to have Stylelint fix some fixable errors.

Note: there are warnings about math=always on running `lessc`, but to fix those all divisions in `.less` need to be wrapped in parens.
